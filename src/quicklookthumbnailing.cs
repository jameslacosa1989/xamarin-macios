// Copyright 2019 Microsoft Corporation

using System;

using CoreGraphics;
using Foundation;
using ObjCRuntime;

#if MONOMAC
using AppKit;
using UIImage = Foundation.NSObject;
#else
using UIKit;
using NSImage = Foundation.NSObject;
#endif

namespace QuickLookThumbnailing {

	[Mac (10,15), iOS (13,0)]
	[Native]
	[ErrorDomain ("QLThumbnailErrorDomain")]
	enum QLThumbnailError : long {
		GenerationFailed,
		SavingToUrlFailed,
		NoCachedThumbnail,
		NoCloudThumbnail,
		RequestInvalid,
		RequestCancelled,
	}

	[Mac (10,15), iOS (13,0)]
	[Flags]
	[Native]
	enum QLThumbnailGenerationRequestRepresentationTypes : ulong {
		None = 0,
		Icon = 1,
		LowQualityThumbnail = 1 << 1,
		Thumbnail = 1 << 2,
		All = UInt64.MaxValue,
	}

	[Mac (10,15), iOS (13,0)]
	[Native]
	public enum QLThumbnailRepresentationType : long {
		Icon = 0,
		LowQualityThumbnail = 1,
		Thumbnail = 2,
	}

	[Mac (10,15), iOS (13,0)]
	[BaseType (typeof (NSObject))]
	[DisableDefaultCtor]
	interface QLThumbnailGenerator {

		[Static]
		[Export ("sharedGenerator")]
		QLThumbnailGenerator SharedGenerator { get; }

		[Async]
		[Export ("generateBestRepresentationForRequest:completionHandler:")]
		void GenerateBestRepresentation (QLThumbnailGenerationRequest request, Action<QLThumbnailRepresentation, NSError> completionHandler);

		[Async (ResultTypeName = "QLThumbnailGeneratorResult")]
		[Export ("generateRepresentationsForRequest:updateHandler:")]
		void GenerateRepresentations (QLThumbnailGenerationRequest request, [NullAllowed] Action<QLThumbnailRepresentation, QLThumbnailRepresentationType, NSError> updateHandler);

		[Export ("cancelRequest:")]
		void CancelRequest (QLThumbnailGenerationRequest request);

		[Async]
		[Export ("saveBestRepresentationForRequest:toFileAtURL:withContentType:completionHandler:")]
		void SaveBestRepresentation (QLThumbnailGenerationRequest request, NSUrl fileUrl, string contentType, Action<NSError> completionHandler);
	}

	[Mac (10,15), iOS (13,0)]
	[BaseType (typeof (NSObject))]
	[DisableDefaultCtor]
	interface QLThumbnailGenerationRequest : NSCopying, NSSecureCoding {

		[Export ("initWithFileAtURL:size:scale:representationTypes:")]
		IntPtr Constructor (NSUrl url, CGSize size, nfloat scale, QLThumbnailGenerationRequestRepresentationTypes representationTypes);

		[Export ("minimumDimension")]
		nfloat MinimumDimension { get; set; }

		[Export ("iconMode")]
		bool IconMode { get; set; }

		[Export ("size")]
		CGSize Size { get; }

		[Export ("scale")]
		nfloat Scale { get; }

		[Export ("representationTypes")]
		QLThumbnailGenerationRequestRepresentationTypes RepresentationTypes { get; }
	}

	[Mac (10,15), iOS (13,0)]
	[BaseType (typeof (NSObject))]
	interface QLThumbnailRepresentation {

		[Export ("type")]
		QLThumbnailRepresentationType Type { get; }

		[Export ("CGImage")]
		CGImage CGImage { get; }

		[NoMac]
		[Export ("UIImage", ArgumentSemantic.Strong)]
		UIImage UIImage { get; }

		[NoiOS]
		[Export ("NSImage", ArgumentSemantic.Strong)]
		NSImage NSImage { get; }
	}
}

#if IOS && !XAMCORE_4_0
namespace QuickLook {
#else
namespace QuickLookThumbnailing {
#endif

	[Mac (10,15)]
	[iOS (11,0)]
	[BaseType (typeof (NSObject))]
	interface QLThumbnailProvider {
		[Export ("provideThumbnailForFileRequest:completionHandler:")]
		void ProvideThumbnail (QLFileThumbnailRequest request, Action<QLThumbnailReply, NSError> handler);
	}

	[ThreadSafe] // Members get called inside 'QLThumbnailProvider.ProvideThumbnail' which runs on a background thread.
	[iOS (11,0)]
	[Mac (10,15)]
	[BaseType (typeof (NSObject))]
	[DisableDefaultCtor]
	interface QLThumbnailReply {
		[Static]
		[Export ("replyWithContextSize:drawingBlock:")]
		QLThumbnailReply CreateReply (CGSize contextSize, Func<CGContext, bool> drawingBlock);

		[Static]
		[Export ("replyWithContextSize:currentContextDrawingBlock:")]
		QLThumbnailReply CreateReply (CGSize contextSize, Func<bool> drawingBlock);

		[Static]
		[Export ("replyWithImageFileURL:")]
		QLThumbnailReply CreateReply (NSUrl fileUrl);
	}

	[ThreadSafe]
	[iOS (11,0)]
	[Mac (10,15)]
	[BaseType (typeof (NSObject))]
	interface QLFileThumbnailRequest {
		[Export ("maximumSize")]
		CGSize MaximumSize { get; }

		[Export ("minimumSize")]
		CGSize MinimumSize { get; }

		[Export ("scale")]
		nfloat Scale { get; }

		[Export ("fileURL", ArgumentSemantic.Copy)]
		NSUrl FileUrl { get; }
	}
}
