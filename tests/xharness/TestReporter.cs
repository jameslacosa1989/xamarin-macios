﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Runtime.Serialization.Json;
using Xharness.Execution;
using Xharness.Listeners;
using Xharness.Logging;
using Xharness.Utilities;

namespace Xharness {

	public class TestReporterFactory : ITestReporterFactory {

		public ITestReporter Create (IAppRunner appRunner, string device, ISimpleListener simpleListener, ILog log, ICrashSnapshotReporter crashReports, IResultParser parser)
			=> new TestReporter (appRunner, device, simpleListener, log, crashReports, parser);
	}

	// main class that gets the result of an executed test application, parses the results and provides information
	// about the success or failure of the execution.
	public class TestReporter : ITestReporter {

		const string timeoutMessage = "Test run timed out after {0} minute(s).";
		const string completionMessage = "Test run completed";
		const string failureMessage = "Test run failed";

		public TimeSpan Timeout { get; private set; }
		public ILog CallbackLog { get; private set; }

		public bool? Success { get; private set; }
		public Stopwatch TimeoutWatch { get; private set; }
		public CancellationToken CancellationToken => cancellationTokenSource.Token;

		public bool ResultsUseXml => runner.XmlJargon != XmlResultJargon.Missing;

		readonly IAppRunner runner;
		readonly ISimpleListener listener;
		readonly ILogs crashLogs;
		readonly ILog runLog;
		readonly ICrashSnapshotReporter crashReporter;
		readonly IResultParser resultParser;
		readonly string deviceName;
		bool waitedForExit = true;
		bool launchFailure;
		bool isSimulatorTest;
		bool timedout;

		readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource ();

		public TestReporter (IAppRunner appRunner, string device, ISimpleListener simpleListener, ILog log, ICrashSnapshotReporter crashReports, IResultParser parser)
		{
			runner = appRunner ?? throw new ArgumentNullException (nameof (appRunner));
			deviceName = device; // can be null on simulators 
			listener = simpleListener ?? throw new ArgumentNullException (nameof (simpleListener));
			runLog = log ?? throw new ArgumentNullException (nameof (log));
			crashLogs = new Logs (runner.Logs.Directory);
			crashReporter = crashReports ?? throw new ArgumentNullException (nameof (crashReports));
			resultParser = parser ?? throw new ArgumentNullException (nameof (parser));
			Timeout = runner.GetNewTimeout ();
			TimeoutWatch  = new Stopwatch ();
			CallbackLog = new CallbackLog ((line) => {
				// MT1111: Application launched successfully, but it's not possible to wait for the app to exit as requested because it's not possible to detect app termination when launching using gdbserver
				waitedForExit &= line?.Contains ("MT1111: ") != true;
				if (line?.Contains ("error MT1007") == true)
					launchFailure = true;
			});
		}

		// parse the run log and decide if we managed to start the process or not
		async Task<(int pid, bool launchFailure)> GetPidFromRunLog () {
			(int pid, bool launchFailure) pidData = (-1, true);
			using var reader = runLog.GetReader (); // diposed at the end of the method, which is what we want
			if (reader.Peek () == -1) {
				// empty file! we definetly had a launch error in this case
				pidData.launchFailure = true;
			} else {
				while (!reader.EndOfStream) {
					var line = await reader.ReadLineAsync ();
					if (line.StartsWith ("Application launched. PID = ", StringComparison.Ordinal)) {
						var pidstr = line.Substring ("Application launched. PID = ".Length);
						if (!int.TryParse (pidstr, out pidData.pid))
							runner.MainLog.WriteLine ("Could not parse pid: {0}", pidstr);
					} else if (line.Contains ("Xamarin.Hosting: Launched ") && line.Contains (" with pid ")) {
						var pidstr = line.Substring (line.LastIndexOf (' '));
						if (!int.TryParse (pidstr, out pidData.pid))
							runner.MainLog.WriteLine ("Could not parse pid: {0}", pidstr);
					} else if (line.Contains ("error MT1008")) {
						pidData.launchFailure = true;
					}
				}
			}
			return pidData;
		}

		// parse the main log to get the pid 
		async Task<int> GetPidFromMainLog ()
		{
			int pid = -1;
			using var log_reader = runner.MainLog.GetReader (); // dispose when we leave the method, which is what we want
			string line;
			while ((line = await log_reader.ReadLineAsync ()) != null) {
				const string str = "was launched with pid '";
				var idx = line.IndexOf (str, StringComparison.Ordinal);
				if (idx > 0) {
					idx += str.Length;
					var next_idx = line.IndexOf ('\'', idx);
					if (next_idx > idx)
						int.TryParse (line.Substring (idx, next_idx - idx), out pid);
				}
				if (pid != -1)
					return pid;
			}
			return pid;
		}

		// return the reason for a crash found in a log
		void GetCrashReason (int pid, ILog crashLog, out string crashReason)
		{
			crashReason = null;
			using var crashReader = crashLog.GetReader (); // dispose when we leave the method
			var text = crashReader.ReadToEnd ();

			var reader = JsonReaderWriterFactory.CreateJsonReader (Encoding.UTF8.GetBytes (text), new XmlDictionaryReaderQuotas ());
			var doc = new XmlDocument ();
			doc.Load (reader);
			foreach (XmlNode node in doc.SelectNodes ($"/root/processes/item[pid = '" + pid + "']")) {
				Console.WriteLine (node?.InnerXml);
				Console.WriteLine (node?.SelectSingleNode ("reason")?.InnerText);
				crashReason = node?.SelectSingleNode ("reason")?.InnerText;
			}
		}

		// return if the tcp connection with the device failed
		async Task<bool> TcpConnectionFailed ()
		{
			using var reader = new StreamReader (runner.MainLog.FullPath);
			string line;
			while ((line = await reader.ReadLineAsync ()) != null) {
				if (line.Contains ("Couldn't establish a TCP connection with any of the hostnames")) {
					return true;
				}
			}
			return false;
		}

		// kill any process 
		Task KillAppProcess (int pid, CancellationTokenSource cancellationSource) { 
				var launchTimedout = cancellationSource.IsCancellationRequested;
				var timeoutType = launchTimedout ? "Launch" : "Completion";
				var timeoutValue = launchTimedout ? runner.LaunchTimeout : Timeout.TotalSeconds;

				runner.MainLog.WriteLine ($"{timeoutType} timed out after {timeoutValue} seconds");
				return runner.ProcessManager.KillTreeAsync (pid, runner.MainLog, true);
		}

		async Task CollectResult (Task<ProcessExecutionResult> processExecution)
		{ 
			// wait for the execution of the process, once that is done, perform all the parsing operations and
			// leave a clean API to be used by AppRunner, hidding all the diff details
			var result = await processExecution;
			if (!waitedForExit && !result.TimedOut) {
				// mlaunch couldn't wait for exit for some reason. Let's assume the app exits when the test listener completes.
				runner.MainLog.WriteLine ("Waiting for listener to complete, since mlaunch won't tell.");
				if (!await listener.CompletionTask.TimeoutAfter (Timeout - TimeoutWatch.Elapsed)) {
					result.TimedOut = true;
				}
			}

			if (result.TimedOut) {
				timedout = true;
				Success = false;
				runner.MainLog.WriteLine (timeoutMessage, Timeout.TotalMinutes);
			} else if (result.Succeeded) {
				runner.MainLog.WriteLine (completionMessage);
				Success = true;
			} else {
				runner.MainLog.WriteLine (failureMessage);
				Success = false;
			}
		}

		public void LaunchCallback (Task<bool> launchResult)
		{
			if (launchResult.IsFaulted) {
				runner.MainLog.WriteLine ("Test launch failed: {0}", launchResult.Exception);
			} else if (launchResult.IsCanceled) {
				runner.MainLog.WriteLine ("Test launch was cancelled.");
			} else if (launchResult.Result) {
				runner.MainLog.WriteLine ("Test run started");
			} else {
				cancellationTokenSource.Cancel ();
				runner.MainLog.WriteLine ("Test launch timed out after {0} minute(s).", runner.LaunchTimeout);
				timedout = true;
			}
		}

		public async Task CollectSimulatorResult (Task<ProcessExecutionResult> processExecution)
		{
			isSimulatorTest = true;
			await CollectResult (processExecution);

			if (!Success.Value) {
				var (pid, launchFailure) = await GetPidFromRunLog ();
				this.launchFailure = launchFailure;
				if (pid > 0) {
					await KillAppProcess (pid, cancellationTokenSource);
				} else {
					runner.MainLog.WriteLine ("Could not find pid in mtouch output.");
				}
			}
		}

		public async Task CollectDeviceResult (Task<ProcessExecutionResult> processExecution)
		{
			isSimulatorTest = false;
			await CollectResult (processExecution);
		}

		async Task<(string ResultLine, bool Failed)> GetResultLine (string logPath)
		{
			string resultLine = null;
			bool failed = false;
			using var reader = new StreamReader (logPath);
			string line = null;
			while ((line = await reader.ReadLineAsync ()) != null) {
				if (line.Contains ("Tests run:")) {
					Console.WriteLine (line);
					resultLine = line;
					break;
				} else if (line.Contains ("[FAIL]")) {
					Console.WriteLine (line);
					failed = true;
				}
			}
			return (ResultLine: resultLine, Failed: failed);
		}

		async Task<(string resultLine, bool failed, bool crashed)> ParseResultFile (AppBundleInformation appInfo, string test_log_path, bool timed_out)
		{
			(string resultLine, bool failed, bool crashed) parseResult = (null, false, false);
			if (!File.Exists (test_log_path)) {
				parseResult.crashed = true; // if we do not have a log file, the test crashes
				return parseResult;
			}
			// parsing the result is different if we are in jenkins or not.
			// When in Jenkins, Touch.Unit produces an xml file instead of a console log (so that we can get better test reporting).
			// However, for our own reporting, we still want the console-based log. This log is embedded inside the xml produced
			// by Touch.Unit, so we need to extract it and write it to disk. We also need to re-save the xml output, since Touch.Unit
			// wraps the NUnit xml output with additional information, which we need to unwrap so that Jenkins understands it.
			// 
			// On the other hand, the nunit and xunit do not have that data and have to be parsed.
			// 
			// This if statement has a small trick, we found out that internet sharing in some of the bots (VSTS) does not work, in
			// that case, we cannot do a TCP connection to xharness to get the log, this is a problem since if we did not get the xml
			// from the TCP connection, we are going to fail when trying to read it and not parse it. Therefore, we are not only
			// going to check if we are in CI, but also if the listener_log is valid.
			var path = Path.ChangeExtension (test_log_path, "xml");
			resultParser.CleanXml (test_log_path, path);

			if (ResultsUseXml && resultParser.IsValidXml (path, out var xmlType)) {
				try {
					var newFilename = resultParser.GetXmlFilePath (path, xmlType);

					// at this point, we have the test results, but we want to be able to have attachments in vsts, so if the format is
					// the right one (NUnitV3) add the nodes. ATM only TouchUnit uses V3.
					var testRunName = $"{appInfo.AppName} {appInfo.Variation}";
					if (xmlType == XmlResultJargon.NUnitV3) {
						var logFiles = new List<string> ();
						// add our logs AND the logs of the previous task, which is the build task
						logFiles.AddRange (Directory.GetFiles (runner.Logs.Directory));
						if (runner.BuildTask != null) // when using the run command, we do not have a build task, ergo, there are no logs to add.
							logFiles.AddRange (Directory.GetFiles (runner.BuildTask.LogDirectory));
						// add the attachments and write in the new filename
						// add a final prefix to the file name to make sure that the VSTS test uploaded just pick
						// the final version, else we will upload tests more than once
						newFilename = XmlResultParser.GetVSTSFilename (newFilename);
						resultParser.UpdateMissingData (path, newFilename, testRunName, logFiles);
					} else {
						// rename the path to the correct value
						File.Move (path, newFilename);
					}
					path = newFilename;

					// write the human readable results in a tmp file, which we later use to step on the logs
					var tmpFile = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ());
					(parseResult.resultLine, parseResult.failed) = resultParser.GenerateHumanReadableResults (path, tmpFile, xmlType);
					File.Copy (tmpFile, test_log_path, true);
					File.Delete (tmpFile);

					// we do not longer need the tmp file
					runner.Logs.AddFile (path, LogType.XmlLog.ToString ());
					return parseResult;

				} catch (Exception e) {
					runner.MainLog.WriteLine ("Could not parse xml result file: {0}", e);
					// print file for better debugging
					runner.MainLog.WriteLine ("File data is:");
					runner.MainLog.WriteLine (new string ('#', 10));
					using (var stream = new StreamReader (path)) {
						string line;
						while ((line = await stream.ReadLineAsync ()) != null) {
							runner.MainLog.WriteLine (line);
						}
					}
					runner.MainLog.WriteLine (new string ('#', 10));
					runner.MainLog.WriteLine ("End of xml results.");
					if (timed_out) {
						WrenchLog.WriteLine ($"AddSummary: <b><i>{runner.RunMode} timed out</i></b><br/>");
						return parseResult;
					} else {
						WrenchLog.WriteLine ($"AddSummary: <b><i>{runner.RunMode} crashed</i></b><br/>");
						runner.MainLog.WriteLine ("Test run crashed");
						parseResult.crashed = true;
						return parseResult;
					}
				}

			}
			// delete not needed copy
			File.Delete (path);

			// not the most efficient way but this just happens when we run
			// the tests locally and we usually do not run all tests, we are
			// more interested to be efficent on the bots
			(parseResult.resultLine, parseResult.failed) = await GetResultLine (test_log_path);
			return parseResult;
		}

		async Task<(bool Succeeded, bool Crashed)> TestsSucceeded (AppBundleInformation appInfo, string test_log_path, bool timed_out)
		{
			var (resultLine, failed, crashed) = await ParseResultFile (appInfo, test_log_path, timed_out);
			// read the parsed logs in a human readable way
			if (resultLine != null) {
				var tests_run = resultLine.Replace ("Tests run: ", "");
				if (failed) {
					WrenchLog.WriteLine ("AddSummary: <b>{0} failed: {1}</b><br/>", runner.RunMode, tests_run);
					runner.MainLog.WriteLine ("Test run failed");
					return (false, crashed);
				} else {
					WrenchLog.WriteLine ("AddSummary: {0} succeeded: {1}<br/>", runner.RunMode, tests_run);
					runner.MainLog.WriteLine ("Test run succeeded");
					return (true, crashed);
				}
			} else if (timed_out) {
				WrenchLog.WriteLine ("AddSummary: <b><i>{0} timed out</i></b><br/>", runner.RunMode);
				return (false, false);
			} else {
				WrenchLog.WriteLine ("AddSummary: <b><i>{0} crashed</i></b><br/>", runner.RunMode);
				runner.MainLog.WriteLine ("Test run crashed");
				return (false, true);
			}
		}

		// generate all the xml failures that will help the integration with the CI and return the failure reason
		async Task GenerateXmlFailures (string failureMessage, bool crashed, string crashReason)
		{
			if (!ResultsUseXml) // nothing to do
				return;
			if (!string.IsNullOrEmpty (crashReason)) {
				resultParser.GenerateFailure (
					runner.Logs,
					"crash",
					runner.AppInformation.AppName,
					runner.AppInformation.Variation,
					$"App Crash {runner.AppInformation.AppName} {runner.AppInformation.Variation}",
					$"App crashed: {failureMessage}",
					runner.MainLog.FullPath,
					runner.XmlJargon);
			} else if (launchFailure) {
				resultParser.GenerateFailure (
					runner.Logs,
					"launch",
					runner.AppInformation.AppName,
					runner.AppInformation.Variation,
					$"App Launch {runner.AppInformation.AppName} {runner.AppInformation.Variation} on {deviceName}",
					$"{failureMessage} on {deviceName}",
					runner.MainLog.FullPath,
					runner.XmlJargon);
			} else if (!isSimulatorTest && crashed && string.IsNullOrEmpty (crashReason)) {
				// this happens more that what we would like on devices, the main reason most of the time is that we have had netwoking problems and the
				// tcp connection could not be stablished. We are going to report it as an error since we have not parsed the logs, evne when the app might have
				// not crashed. We need to check the main_log to see if we do have an tcp issue or not
				if (await TcpConnectionFailed ()) {
					resultParser.GenerateFailure (
						runner.Logs,
						"tcp-connection",
						runner.AppInformation.AppName,
						runner.AppInformation.Variation,
						$"TcpConnection on {deviceName}",
						$"Device {deviceName} could not reach the host over tcp.",
						runner.MainLog.FullPath,
						runner.XmlJargon);
				}
			} else if (timedout) {
				resultParser.GenerateFailure (
					runner.Logs,
					"timeout",
					runner.AppInformation.AppName,
					runner.AppInformation.Variation,
					$"App Timeout {runner.AppInformation.AppName} {runner.AppInformation.Variation} on bot {deviceName}",
					$"{runner.AppInformation.AppName} {runner.AppInformation.Variation} Test run timed out after {Timeout.TotalMinutes} minute(s) on bot {deviceName}.",
					runner.MainLog.FullPath,
					runner.XmlJargon);
			}
		}

		public async Task<(TestExecutingResult ExecutingResult, string FailureMessage)> ParseResult ()
		{
			var result = (ExecutingResult: TestExecutingResult.Finished, FailureMessage: (string) null);
			var crashed = false;
			if (File.Exists (listener.TestLog.FullPath)) {
				WrenchLog.WriteLine ("AddFile: {0}", listener.TestLog.FullPath);
				(Success, crashed) = await TestsSucceeded (runner.AppInformation, listener.TestLog.FullPath, timedout);
			} else if (timedout) {
				WrenchLog.WriteLine ("AddSummary: <b><i>{0} never launched</i></b><br/>", runner.RunMode);
				runner.MainLog.WriteLine ("Test run never launched");
				Success = false;
			} else if (launchFailure) {
 				WrenchLog.WriteLine ("AddSummary: <b><i>{0} failed to launch</i></b><br/>", runner.RunMode);
 				runner.MainLog.WriteLine ("Test run failed to launch");
 				Success = false;
			} else {
				WrenchLog.WriteLine ("AddSummary: <b><i>{0} crashed at startup (no log)</i></b><br/>", runner.RunMode);
				runner.MainLog.WriteLine ("Test run crashed before it started (no log file produced)");
				crashed = true;
				Success = false;
			}
				
			if (!Success.HasValue)
				Success = false;

			var crashLogWaitTime = 0;
			if (!Success.Value)
				crashLogWaitTime = 5;
			if (crashed)
				crashLogWaitTime = 30;

			await crashReporter.EndCaptureAsync (TimeSpan.FromSeconds (crashLogWaitTime));

			if (timedout) {
				result.ExecutingResult = TestExecutingResult.TimedOut;
			} else if (crashed) {
				result.ExecutingResult = TestExecutingResult.Crashed;
			} else if (Success.Value) {
				result.ExecutingResult = TestExecutingResult.Succeeded;
			} else {
				result.ExecutingResult = TestExecutingResult.Failed;
			}

			// Check crash reports to see if any of them explains why the test run crashed.
			if (!Success.Value) {
				int pid = -1;
				string crashReason = null;
				foreach (var crashLog in crashLogs) {
					try {
						runner.Logs.Add (crashLog);

						if (pid == -1) {
							// Find the pid
							pid = await GetPidFromMainLog ();
						}

						GetCrashReason (pid, crashLog, out crashReason);
						if (crashReason != null) 
							break;
					} catch (Exception e) {
						runner.LogException (2, "Failed to process crash report '{1}': {0}", e.Message, crashLog.Description);
					}
				}
				if (!string.IsNullOrEmpty (crashReason)) {
					if (crashReason == "per-process-limit") {
						result.FailureMessage = "Killed due to using too much memory (per-process-limit).";
					} else {
						result.FailureMessage = $"Killed by the OS ({crashReason})";
					}
				} else if (launchFailure) {
					// same as with a crash
					result.FailureMessage = $"Launch failure";
				} 
				await GenerateXmlFailures (result.FailureMessage, crashed, crashReason);
			}
			return result;
		}

	}
}
