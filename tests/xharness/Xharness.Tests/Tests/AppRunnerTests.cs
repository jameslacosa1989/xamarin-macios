﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Xharness.Execution;
using Xharness.Execution.Mlaunch;
using Xharness.Hardware;
using Xharness.Listeners;
using Xharness.Logging;
using Xharness.Utilities;

namespace Xharness.Tests {
	[TestFixture]
	public class AppRunnerTests {

		const string appName = "com.xamarin.bcltests.SystemXunit";
		const string xcodePath = "/path/to/xcode";
		const string mlaunchPath = "/path/to/mlaunch";

		static readonly string outputPath = Path.GetDirectoryName (Assembly.GetAssembly (typeof (AppRunnerTests)).Location);
		static readonly string sampleProjectPath = Path.Combine (outputPath, "Samples", "TestProject");
		static readonly string appPath = Path.Combine (sampleProjectPath, "bin", appName + ".app");
		static readonly string projectFilePath = Path.Combine (sampleProjectPath, "SystemXunit.csproj");

		static readonly IHardwareDevice [] mockDevices = new IHardwareDevice [] {
			new Device() {
				BuildVersion = "17A577",
				DeviceClass = DeviceClass.iPhone,
				DeviceIdentifier = "8A450AA31EA94191AD6B02455F377CC1",
				InterfaceType = "Usb",
				IsUsableForDebugging = true,
				Name = "Test iPhone",
				ProductType = "iPhone12,1",
				ProductVersion = "13.0",
				UDID = "58F21118E4D34FD69EAB7860BB9B38A0",
			},
			new Device() {
				BuildVersion = "13G36",
				DeviceClass = DeviceClass.iPad,
				DeviceIdentifier = "E854B2C3E7C8451BAF8053EC4DAAEE49",
				InterfaceType = "Usb",
				IsUsableForDebugging = true,
				Name = "Test iPad",
				ProductType = "iPad2,1",
				ProductVersion = "9.3.5",
				UDID = "51F3354D448D4814825D07DC5658C19B",
			}
		};

		Mock<IProcessManager> processManager;
		Mock<ISimulatorsLoader> simulators;
		Mock<IDeviceLoader> devices;
		Mock<ISimpleListener> simpleListener;
		Mock<ICrashSnapshotReporter> snapshotReporter;
		Mock<ILogs> logs;
		Mock<ILog> mainLog;

		ISimulatorsLoaderFactory simulatorsFactory;
		IDeviceLoaderFactory devicesFactory;
		ISimpleListenerFactory listenerFactory;
		ICrashSnapshotReporterFactory snapshotReporterFactory;
		IAppBundleInformationParser appBundleInformationParser;

		[SetUp]
		public void SetUp ()
		{
			logs = new Mock<ILogs> ();
			logs.SetupGet (x => x.Directory).Returns (Path.Combine (outputPath, "logs"));

			processManager = new Mock<IProcessManager> ();
			simulators = new Mock<ISimulatorsLoader> ();
			devices = new Mock<IDeviceLoader> ();
			simpleListener = new Mock<ISimpleListener> ();
			snapshotReporter = new Mock<ICrashSnapshotReporter> ();

			var mock1 = new Mock<ISimulatorsLoaderFactory> ();
			mock1.Setup (m => m.CreateLoader ()).Returns (simulators.Object);
			simulatorsFactory = mock1.Object;

			var mock2 = new Mock<IDeviceLoaderFactory> ();
			mock2.Setup (m => m.CreateLoader ()).Returns (devices.Object);
			devicesFactory = mock2.Object;

			var mock3 = new Mock<ISimpleListenerFactory> ();
			mock3
				.Setup (m => m.Create (It.IsAny<RunMode> (), It.IsAny<ILog> (), It.IsAny<ILog> (), It.IsAny<bool> (), It.IsAny<bool> (), It.IsAny<bool> ()))
				.Returns ((ListenerTransport.Tcp, simpleListener.Object, "listener-temp-file"));
			listenerFactory = mock3.Object;
			simpleListener.SetupGet (x => x.Port).Returns (1020);

			var mock4 = new Mock<ICrashSnapshotReporterFactory> ();
			mock4.Setup (m => m.Create (It.IsAny<ILog> (), It.IsAny<ILogs> (), It.IsAny<bool> (), It.IsAny<string> ())).Returns (snapshotReporter.Object);
			snapshotReporterFactory = mock4.Object;

			var mock5 = new Mock<IAppBundleInformationParser> ();
			mock5
				.Setup (x => x.ParseFromProject (projectFilePath, It.IsAny<TestTarget> (), "Debug"))
				.Returns (new AppBundleInformation (appName, appName, appPath, appPath, null));

			appBundleInformationParser = mock5.Object;

			mainLog = new Mock<ILog> ();

			Directory.CreateDirectory (appPath);
		}

		[TearDown]
		public void TearDown ()
		{
			Directory.Delete (appPath, true);
		}

		[Test]
		public void InitializeTest ()
		{
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				snapshotReporterFactory,
				Mock.Of<ICaptureLogFactory> (),
				Mock.Of<IDeviceLogCapturerFactory> (),
				Mock.Of<ITestReporterFactory> (),
				TestTarget.Simulator_iOS64,
				Mock.Of<IHarness> (),
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug");

			Assert.AreEqual (appName, appRunner.AppInformation.AppName);
			Assert.AreEqual (appPath, appRunner.AppInformation.AppPath);
			Assert.AreEqual (appPath, appRunner.AppInformation.LaunchAppPath);
			Assert.AreEqual (appName, appRunner.AppInformation.BundleIdentifier);
		}

		[Test]
		public void InstallToSimulatorTest ()
		{
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				snapshotReporterFactory,
				Mock.Of<ICaptureLogFactory> (),
				Mock.Of<IDeviceLogCapturerFactory> (),
				Mock.Of<ITestReporterFactory> (),
				TestTarget.Simulator_iOS64,
				Mock.Of<IHarness> (),
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug");

			var exception = Assert.ThrowsAsync<InvalidOperationException> (
				async () => await appRunner.InstallAsync (new CancellationToken ()),
				"Install should not be allowed on a simulator");
		}

		[Test]
		public void UninstallToSimulatorTest ()
		{
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				snapshotReporterFactory,
				Mock.Of<ICaptureLogFactory> (),
				Mock.Of<IDeviceLogCapturerFactory> (),
				Mock.Of<ITestReporterFactory> (),
				TestTarget.Simulator_iOS64,
				Mock.Of<IHarness> (),
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug");

			var exception = Assert.ThrowsAsync<InvalidOperationException> (
				async () => await appRunner.UninstallAsync (),
				"Uninstall should not be allowed on a simulator");
		}

		[Test]
		public void InstallWhenNoDevicesTest ()
		{
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				snapshotReporterFactory,
				Mock.Of<ICaptureLogFactory> (),
				Mock.Of<IDeviceLogCapturerFactory> (),
				Mock.Of<ITestReporterFactory> (),
				TestTarget.Device_iOS,
				Mock.Of<IHarness> (),
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug");

			devices.Setup (x => x.ConnectedDevices).Returns (new IHardwareDevice [0]);

			Assert.ThrowsAsync<NoDeviceFoundException> (
				async () => await appRunner.InstallAsync (new CancellationToken ()),
				"Install requires connected devices");
		}

		[Test]
		public async Task InstallOnDeviceTest ()
		{
			var harness = Mock.Of<IHarness> (x => x.Verbosity == 2);

			var processResult = new ProcessExecutionResult () { ExitCode = 1, TimedOut = false };
			processManager.SetReturnsDefault (Task.FromResult (processResult));

			devices.Setup (x => x.ConnectedDevices).Returns (mockDevices);

			// Act
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				snapshotReporterFactory,
				Mock.Of<ICaptureLogFactory> (),
				Mock.Of<IDeviceLogCapturerFactory> (),
				Mock.Of<ITestReporterFactory> (),
				TestTarget.Device_iOS,
				harness,
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug");

			CancellationToken cancellationToken = new CancellationToken ();
			var result = await appRunner.InstallAsync (cancellationToken);

			// Verify
			Assert.AreEqual (1, result.ExitCode);

			var expectedArgs = $"-v -v -v --installdev {StringUtils.FormatArguments (appPath)} --devname \"Test iPad\"";

			processManager.Verify (x => x.ExecuteCommandAsync (
				It.Is<MlaunchArguments> (args => args.AsCommandLine() == expectedArgs),
				mainLog.Object,
				TimeSpan.FromHours (1),
				null,
				cancellationToken));
		}

		[Test]
		public async Task UninstallFromDeviceTest ()
		{
			var harness = Mock.Of<IHarness> (x => x.Verbosity == 1);

			var processResult = new ProcessExecutionResult () { ExitCode = 3, TimedOut = false };
			processManager.SetReturnsDefault (Task.FromResult (processResult));

			devices.Setup (x => x.ConnectedDevices).Returns (mockDevices.Reverse ());

			// Act
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				snapshotReporterFactory,
				Mock.Of<ICaptureLogFactory> (),
				Mock.Of<IDeviceLogCapturerFactory> (),
				Mock.Of<ITestReporterFactory> (),
				TestTarget.Device_iOS,
				harness,
				mainLog.Object,
				logs.Object,
				projectFilePath: Path.Combine (sampleProjectPath, "SystemXunit.csproj"),
				buildConfiguration: "Debug");

			var result = await appRunner.UninstallAsync ();

			// Verify
			Assert.AreEqual (3, result.ExitCode);

			var expectedArgs = $"-v -v --uninstalldevbundleid {StringUtils.FormatArguments (appName)} --devname \"Test iPad\"";

			processManager.Verify (x => x.ExecuteCommandAsync (
				It.Is<MlaunchArguments> (args => args.AsCommandLine() == expectedArgs),
				mainLog.Object,
				TimeSpan.FromMinutes (1),
				null,
				null));
		}

		[Test]
		public async Task RunOnSimulatorWithNoAvailableSimulatorTest ()
		{
			devices.Setup (x => x.ConnectedDevices).Returns (mockDevices.Reverse ());

			// Crash reporter
			var crashReporterFactory = new Mock<ICrashSnapshotReporterFactory> ();
			crashReporterFactory
				.Setup (x => x.Create (mainLog.Object, It.IsAny<ILogs> (), false, null))
				.Returns (snapshotReporter.Object);

			// Mock finding simulators
			simulators
				.Setup (x => x.LoadAsync (It.IsAny<ILog> (), false, false))
				.Returns (Task.CompletedTask);

			string simulatorLogPath = Path.Combine (Path.GetTempPath (), "simulator-logs");

			simulators
				.Setup (x => x.FindAsync (TestTarget.Simulator_tvOS, mainLog.Object, true, false))
				.ReturnsAsync ((ISimulatorDevice []) null);

			var listenerLogFile = new Mock<ILog> ();

			logs
				.Setup (x => x.Create (It.IsAny<string> (), "TestLog", It.IsAny<bool> ()))
				.Returns (listenerLogFile.Object);

			simpleListener.SetupGet (x => x.ConnectedTask).Returns (Task.CompletedTask);

			var captureLog = new Mock<ICaptureLog> ();
			captureLog.SetupGet (x => x.FullPath).Returns (simulatorLogPath);

			var captureLogFactory = new Mock<ICaptureLogFactory> ();
			captureLogFactory
				.Setup (x => x.Create (
					Path.Combine (logs.Object.Directory, "tvos.log"),
					"/path/to/simulator.log",
					true,
					It.IsAny<string> ()))
				.Returns (captureLog.Object);
			var testReporterFactory = new Mock<ITestReporterFactory> ();
			var testReporter = new Mock<ITestReporter> ();
			testReporterFactory.Setup (f => f.Create (
				It.IsAny<IAppRunner> (),
				It.IsAny<string> (),
				It.IsAny<ISimpleListener> (),
				It.IsAny<ILog> (),
				It.IsAny<ICrashSnapshotReporter> (),
				It.IsAny<IResultParser> ())).Returns (testReporter.Object);
			testReporter.Setup (r => r.TimeoutWatch).Returns (new System.Diagnostics.Stopwatch ());
			testReporter.Setup (r => r.ParseResult ()).Returns (() => {
				return Task.FromResult<(TestExecutingResult, string)> ((TestExecutingResult.Succeeded, null));
			});
			testReporter.Setup (r => r.Success).Returns (true);

			// Act
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				crashReporterFactory.Object,
				captureLogFactory.Object,
				Mock.Of<IDeviceLogCapturerFactory> (),
				testReporterFactory.Object,
				TestTarget.Simulator_tvOS,
				GetMockedHarness (),
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug",
				timeoutMultiplier: 2);

			var result = await appRunner.RunAsync ();

			// Verify
			Assert.AreEqual (1, result);

			mainLog.Verify (x => x.WriteLine ("Test run completed"), Times.Never);

			simpleListener.Verify (x => x.Initialize (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.StartAsync (), Times.AtLeastOnce);

			simulators.VerifyAll ();
		}

		[Test]
		public async Task RunOnSimulatorSuccessfullyTest ()
		{
			var harness = GetMockedHarness ();

			devices.Setup (x => x.ConnectedDevices).Returns (mockDevices.Reverse ());

			// Crash reporter
			var crashReporterFactory = new Mock<ICrashSnapshotReporterFactory> ();
			crashReporterFactory
				.Setup (x => x.Create (mainLog.Object, It.IsAny<ILogs> (), false, null))
				.Returns (snapshotReporter.Object);

			// Mock finding simulators
			simulators
				.Setup (x => x.LoadAsync (It.IsAny<ILog> (), false, false))
				.Returns (Task.CompletedTask);

			string simulatorLogPath = Path.Combine (Path.GetTempPath (), "simulator-logs");

			var simulator = new Mock<ISimulatorDevice> ();
			simulator.SetupGet (x => x.Name).Returns ("Test iPhone simulator");
			simulator.SetupGet (x => x.UDID).Returns ("58F21118E4D34FD69EAB7860BB9B38A0");
			simulator.SetupGet (x => x.LogPath).Returns (simulatorLogPath);
			simulator.SetupGet (x => x.SystemLog).Returns (Path.Combine (simulatorLogPath, "system.log"));

			simulators
				.Setup (x => x.FindAsync (TestTarget.Simulator_iOS64, mainLog.Object, true, false))
				.ReturnsAsync (new ISimulatorDevice [] { simulator.Object });

			var testResultFilePath = Path.GetTempFileName ();
			var listenerLogFile = Mock.Of<ILog> (x => x.FullPath == testResultFilePath);
			File.WriteAllLines (testResultFilePath, new [] { "Some result here", "Tests run: 124", "Some result there" });

			logs
				.Setup (x => x.Create (It.Is<string> (s => s.StartsWith ("test-sim64-")), "TestLog", It.IsAny<bool?> ()))
				.Returns (listenerLogFile);

			simpleListener.SetupGet (x => x.ConnectedTask).Returns (Task.CompletedTask);

			var captureLog = new Mock<ICaptureLog> ();
			captureLog.SetupGet (x => x.FullPath).Returns (simulatorLogPath);

			var captureLogFactory = new Mock<ICaptureLogFactory> ();
			captureLogFactory
				.Setup (x => x.Create (
					Path.Combine (logs.Object.Directory, simulator.Object.Name + ".log"),
					simulator.Object.SystemLog,
					true,
					It.IsAny<string> ()))
				.Returns (captureLog.Object);

			var expectedArgs = $"--sdkroot {StringUtils.FormatArguments (xcodePath)} -v -v " +
				$"-argument=-connection-mode -argument=none -argument=-app-arg:-autostart " +
				$"-setenv=NUNIT_AUTOSTART=true -argument=-app-arg:-autoexit -setenv=NUNIT_AUTOEXIT=true " +
				$"-argument=-app-arg:-enablenetwork -setenv=NUNIT_ENABLE_NETWORK=true " +
				$"-setenv=DISABLE_SYSTEM_PERMISSION_TESTS=1 -argument=-app-arg:-hostname:127.0.0.1 " +
				$"-setenv=NUNIT_HOSTNAME=127.0.0.1 -argument=-app-arg:-transport:Tcp -setenv=NUNIT_TRANSPORT=TCP " +
				$"-argument=-app-arg:-hostport:{simpleListener.Object.Port} -setenv=NUNIT_HOSTPORT={simpleListener.Object.Port} " +
				$"-setenv=env1=value1 -setenv=env2=value2 --launchsim {StringUtils.FormatArguments (appPath)} " +
				$"--stdout=tty1 --stderr=tty1 --device=:v2:udid={simulator.Object.UDID}";

			processManager
				.Setup (x => x.ExecuteCommandAsync (
					It.Is<MlaunchArguments> (args => args.AsCommandLine() == expectedArgs),
					mainLog.Object,
					TimeSpan.FromMinutes (harness.Timeout * 2),
					null,
					It.IsAny<CancellationToken> ()))
				.ReturnsAsync (new ProcessExecutionResult () { ExitCode = 0 });

			var xmlResultFile = Path.ChangeExtension (testResultFilePath, "xml");
			var testReporterFactory = new Mock<ITestReporterFactory> ();
			var testReporter = new Mock<ITestReporter> ();
			testReporterFactory.Setup (f => f.Create (
				It.IsAny<IAppRunner> (),
				It.IsAny<string> (),
				It.IsAny<ISimpleListener> (),
				It.IsAny<ILog> (),
				It.IsAny<ICrashSnapshotReporter> (),
				It.IsAny<IResultParser> ())).Returns (testReporter.Object);
			testReporter.Setup (r => r.Timeout).Returns (TimeSpan.FromMinutes (harness.Timeout * 2));
			testReporter.Setup (r => r.TimeoutWatch).Returns (new System.Diagnostics.Stopwatch ());
			testReporter.Setup (r => r.ParseResult ()).Returns (() => {
				return Task.FromResult<(TestExecutingResult, string)> ((TestExecutingResult.Succeeded, null));
			});
			testReporter.Setup (r => r.Success).Returns (true);

			// Act
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				crashReporterFactory.Object,
				captureLogFactory.Object,
				Mock.Of<IDeviceLogCapturerFactory> (), // Use for devices only
				testReporterFactory.Object,
				TestTarget.Simulator_iOS64,
				harness,
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug",
				timeoutMultiplier: 2,
				ensureCleanSimulatorState: true);

			var result = await appRunner.RunAsync ();

			// Verify
			Assert.AreEqual (0, result);

			simpleListener.Verify (x => x.Initialize (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.StartAsync (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.Cancel (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.Dispose (), Times.AtLeastOnce);

			simulators.VerifyAll ();

			captureLog.Verify (x => x.StartCapture (), Times.AtLeastOnce);
			captureLog.Verify (x => x.StopCapture (), Times.AtLeastOnce);

			// When ensureCleanSimulatorState == true
			simulator.Verify (x => x.PrepareSimulatorAsync (mainLog.Object, appName));
			simulator.Verify (x => x.KillEverythingAsync (mainLog.Object));
		}

		[Test]
		public void RunOnDeviceWithNoAvailableSimulatorTest ()
		{
			devices.Setup (x => x.ConnectedDevices).Returns (mockDevices.Reverse ());

			// Crash reporter
			var crashReporterFactory = new Mock<ICrashSnapshotReporterFactory> ();
			crashReporterFactory
				.Setup (x => x.Create (mainLog.Object, It.IsAny<ILogs> (), false, null))
				.Returns (snapshotReporter.Object);

			var listenerLogFile = new Mock<ILog> ();

			logs
				.Setup (x => x.Create (It.IsAny<string> (), "TestLog", It.IsAny<bool> ()))
				.Returns (listenerLogFile.Object);

			simpleListener.SetupGet (x => x.ConnectedTask).Returns (Task.CompletedTask);

			// Act
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				crashReporterFactory.Object,
				Mock.Of<ICaptureLogFactory> (),
				Mock.Of<IDeviceLogCapturerFactory> (),
				Mock.Of<ITestReporterFactory> (),
				TestTarget.Device_tvOS,
				GetMockedHarness (),
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug",
				timeoutMultiplier: 2);

			Assert.ThrowsAsync<NoDeviceFoundException> (
				async () => await appRunner.RunAsync (),
				"Running requires connected devices");
		}

		[Test]
		public async Task RunOnDeviceSuccessfullyTest ()
		{
			var harness = GetMockedHarness ();

			devices.Setup (x => x.ConnectedDevices).Returns (mockDevices.Reverse ());

			// Crash reporter
			var crashReporterFactory = new Mock<ICrashSnapshotReporterFactory> ();
			crashReporterFactory
				.Setup (x => x.Create (mainLog.Object, It.IsAny<ILogs> (), true, "Test iPad"))
				.Returns (snapshotReporter.Object);

			var deviceSystemLog = new Mock<ILog> ();
			deviceSystemLog.SetupGet (x => x.FullPath).Returns (Path.GetTempFileName ());

			var testResultFilePath = Path.GetTempFileName ();
			var listenerLogFile = Mock.Of<ILog> (x => x.FullPath == testResultFilePath);
			File.WriteAllLines (testResultFilePath, new [] { "Some result here", "Some result there", "Tests run: 3" });

			logs
				.Setup (x => x.Create (It.Is<string> (s => s.StartsWith ("test-ios-")), "TestLog", It.IsAny<bool?> ()))
				.Returns (listenerLogFile);

			logs
				.Setup (x => x.Create (It.Is<string> (s => s.StartsWith ("device-Test iPad-")), "Device log", It.IsAny<bool?> ()))
				.Returns (deviceSystemLog.Object);

			simpleListener.SetupGet (x => x.ConnectedTask).Returns (Task.CompletedTask);

			var deviceLogCapturer = new Mock<IDeviceLogCapturer> ();

			var deviceLogCapturerFactory = new Mock<IDeviceLogCapturerFactory> ();
			deviceLogCapturerFactory
				.Setup (x => x.Create (mainLog.Object, deviceSystemLog.Object, "Test iPad"))
				.Returns (deviceLogCapturer.Object);

			var ips = new StringBuilder ();
			var ipAddresses = System.Net.Dns.GetHostEntry (System.Net.Dns.GetHostName ()).AddressList;
			for (int i = 0; i < ipAddresses.Length; i++) {
				if (i > 0)
					ips.Append (',');
				ips.Append (ipAddresses [i].ToString ());
			}

			var expectedArgs = $"-v -v -argument=-connection-mode -argument=none " +
				$"-argument=-app-arg:-autostart -setenv=NUNIT_AUTOSTART=true -argument=-app-arg:-autoexit " +
				$"-setenv=NUNIT_AUTOEXIT=true -argument=-app-arg:-enablenetwork -setenv=NUNIT_ENABLE_NETWORK=true " +
				$"-setenv=DISABLE_SYSTEM_PERMISSION_TESTS=1 -argument=-app-arg:-hostname:{ips} -setenv=NUNIT_HOSTNAME={ips} " +
				$"-argument=-app-arg:-transport:Tcp -setenv=NUNIT_TRANSPORT=TCP -argument=-app-arg:-hostport:{simpleListener.Object.Port} " +
				$"-setenv=NUNIT_HOSTPORT={simpleListener.Object.Port} -setenv=env1=value1 -setenv=env2=value2 " +
				$"--launchdev {StringUtils.FormatArguments (appPath)} --disable-memory-limits --wait-for-exit --devname \"Test iPad\"";

			processManager
				.Setup (x => x.ExecuteCommandAsync (
					It.Is<MlaunchArguments> (args => args.AsCommandLine () == expectedArgs),
					It.IsAny<ILog> (),
					TimeSpan.FromMinutes (harness.Timeout * 2),
					null,
					It.IsAny<CancellationToken> ()))
				.ReturnsAsync (new ProcessExecutionResult () { ExitCode = 0 });

			var xmlResultFile = Path.ChangeExtension (testResultFilePath, "xml");
			var testReporterFactory = new Mock<ITestReporterFactory> ();
			var testReporter = new Mock<ITestReporter> ();
			testReporterFactory
				.Setup (f => f.Create (
					It.IsAny<IAppRunner> (),
					"Test iPad",
					It.IsAny<ISimpleListener> (),
					mainLog.Object,
					snapshotReporter.Object,
					It.IsAny<IResultParser> ()))
				.Returns (testReporter.Object);
			testReporter.Setup (r => r.Timeout).Returns (TimeSpan.FromMinutes (harness.Timeout * 2));
			testReporter.Setup (r => r.TimeoutWatch).Returns (new System.Diagnostics.Stopwatch ());
			testReporter.Setup (r => r.ParseResult ()).Returns (() => Task.FromResult<(TestExecutingResult, string)> ((TestExecutingResult.Succeeded, null)));
			testReporter.Setup (r => r.Success).Returns (true);

			// Act
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				crashReporterFactory.Object,
				Mock.Of<ICaptureLogFactory> (), // Used for simulators only
				deviceLogCapturerFactory.Object,
				testReporterFactory.Object,
				TestTarget.Device_iOS,
				harness,
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug",
				timeoutMultiplier: 2);

			var result = await appRunner.RunAsync ();

			// Verify
			Assert.AreEqual (0, result);

			processManager.VerifyAll ();

			simpleListener.Verify (x => x.Initialize (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.StartAsync (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.Cancel (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.Dispose (), Times.AtLeastOnce);

			snapshotReporter.Verify (x => x.StartCaptureAsync (), Times.AtLeastOnce);
			snapshotReporter.Verify (x => x.StartCaptureAsync (), Times.AtLeastOnce);

			deviceSystemLog.Verify (x => x.Dispose (), Times.AtLeastOnce);
		}

		[Test]
		public async Task RunOnDeviceWithFailedTestsTest ()
		{
			var harness = GetMockedHarness ();

			devices.Setup (x => x.ConnectedDevices).Returns (mockDevices.Reverse ());

			// Crash reporter
			var crashReporterFactory = new Mock<ICrashSnapshotReporterFactory> ();
			crashReporterFactory
				.Setup (x => x.Create (mainLog.Object, It.IsAny<ILogs> (), true, "Test iPad"))
				.Returns (snapshotReporter.Object);

			var deviceSystemLog = new Mock<ILog> ();
			deviceSystemLog.SetupGet (x => x.FullPath).Returns (Path.GetTempFileName ());

			var testResultFilePath = Path.GetTempFileName ();
			var listenerLogFile = Mock.Of<ILog> (x => x.FullPath == testResultFilePath);
			File.WriteAllLines (testResultFilePath, new [] { "Some result here", "[FAIL] This test failed", "Some result there", "Tests run: 3" });

			logs
				.Setup (x => x.Create (It.Is<string> (s => s.StartsWith ("test-ios-")), "TestLog", It.IsAny<bool?> ()))
				.Returns (listenerLogFile);

			logs
				.Setup (x => x.Create (It.Is<string> (s => s.StartsWith ("device-Test iPad-")), "Device log", It.IsAny<bool?> ()))
				.Returns (deviceSystemLog.Object);

			simpleListener.SetupGet (x => x.ConnectedTask).Returns (Task.CompletedTask);

			var deviceLogCapturer = new Mock<IDeviceLogCapturer> ();

			var deviceLogCapturerFactory = new Mock<IDeviceLogCapturerFactory> ();
			deviceLogCapturerFactory
				.Setup (x => x.Create (mainLog.Object, deviceSystemLog.Object, "Test iPad"))
				.Returns (deviceLogCapturer.Object);

			var ips = new StringBuilder ();
			var ipAddresses = System.Net.Dns.GetHostEntry (System.Net.Dns.GetHostName ()).AddressList;
			for (int i = 0; i < ipAddresses.Length; i++) {
				if (i > 0)
					ips.Append (',');
				ips.Append (ipAddresses [i].ToString ());
			}

			var expectedArgs = $"-v -v -argument=-connection-mode -argument=none " +
				$"-argument=-app-arg:-autostart -setenv=NUNIT_AUTOSTART=true -argument=-app-arg:-autoexit " +
				$"-setenv=NUNIT_AUTOEXIT=true -argument=-app-arg:-enablenetwork -setenv=NUNIT_ENABLE_NETWORK=true " +
				$"-setenv=DISABLE_SYSTEM_PERMISSION_TESTS=1 -argument=-app-arg:-hostname:{ips} -setenv=NUNIT_HOSTNAME={ips} " +
				$"-argument=-app-arg:-transport:Tcp -setenv=NUNIT_TRANSPORT=TCP -argument=-app-arg:-hostport:{simpleListener.Object.Port} " +
				$"-setenv=NUNIT_HOSTPORT={simpleListener.Object.Port} -setenv=env1=value1 -setenv=env2=value2 " +
				$"--launchdev {StringUtils.FormatArguments (appPath)} --disable-memory-limits --wait-for-exit --devname \"Test iPad\"";

			processManager
				.Setup (x => x.ExecuteCommandAsync (
					It.Is<MlaunchArguments> (args => args.AsCommandLine () == expectedArgs),
					It.IsAny<ILog> (),
					TimeSpan.FromMinutes (harness.Timeout * 2),
					null,
					It.IsAny<CancellationToken> ()))
				.ReturnsAsync (new ProcessExecutionResult () { ExitCode = 0 });

			var xmlResultFile = Path.ChangeExtension (testResultFilePath, "xml");
			var testReporterFactory = new Mock<ITestReporterFactory> ();
			var testReporter = new Mock<ITestReporter> ();
			testReporterFactory
				.Setup (f => f.Create (
					It.IsAny<IAppRunner> (),
					"Test iPad",
					It.IsAny<ISimpleListener> (),
					mainLog.Object,
					snapshotReporter.Object,
					It.IsAny<IResultParser> ()))
				.Returns (testReporter.Object);
			testReporter.Setup (r => r.Timeout).Returns (TimeSpan.FromMinutes (harness.Timeout * 2));
			testReporter.Setup (r => r.TimeoutWatch).Returns (new System.Diagnostics.Stopwatch ());
			testReporter.Setup (r => r.ParseResult ()).Returns (() => {
				return Task.FromResult<(TestExecutingResult, string)> ((TestExecutingResult.Failed, null));
			});
			testReporter.Setup (r => r.Success).Returns (false);

			// Act
			var appRunner = new AppRunner (processManager.Object,
				appBundleInformationParser,
				simulatorsFactory,
				listenerFactory,
				devicesFactory,
				crashReporterFactory.Object,
				Mock.Of<ICaptureLogFactory> (), // Used for simulators only
				deviceLogCapturerFactory.Object,
				testReporterFactory.Object,
				TestTarget.Device_iOS,
				harness,
				mainLog.Object,
				logs.Object,
				projectFilePath: projectFilePath,
				buildConfiguration: "Debug",
				timeoutMultiplier: 2);

			var result = await appRunner.RunAsync ();

			// Verify
			Assert.AreEqual (1, result);

			processManager.VerifyAll ();

			simpleListener.Verify (x => x.Initialize (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.StartAsync (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.Cancel (), Times.AtLeastOnce);
			simpleListener.Verify (x => x.Dispose (), Times.AtLeastOnce);

			snapshotReporter.Verify (x => x.StartCaptureAsync (), Times.AtLeastOnce);
			snapshotReporter.Verify (x => x.StartCaptureAsync (), Times.AtLeastOnce);

			deviceSystemLog.Verify (x => x.Dispose (), Times.AtLeastOnce);
		}

		IHarness GetMockedHarness ()
		{
			return Mock.Of<IHarness> (x => x.Action == HarnessAction.Run
				&& x.Verbosity == 1
				&& x.HarnessLog == mainLog.Object
				&& x.InCI == false
				&& x.EnvironmentVariables == new Dictionary<string, string> () {
					{ "env1", "value1" },
					{ "env2", "value2" },
				}
				&& x.Timeout == 1d
				&& x.GetStandardErrorTty () == "tty1");
		}
	}
}
