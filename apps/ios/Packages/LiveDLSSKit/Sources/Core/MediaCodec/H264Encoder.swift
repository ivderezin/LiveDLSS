import CoreMedia
import CoreVideo
import Foundation
import VideoToolbox

public struct EncodedVideoFrame: Sendable {
    public var data: Data
    public var isKeyframe: Bool
    public var width: Int
    public var height: Int
}

public enum MediaCodecError: Error {
    case osStatus(OSStatus, String)
}

public final class H264Encoder: @unchecked Sendable {
    public let width: Int
    public let height: Int

    private var session: VTCompressionSession?
    private var transfer: VTPixelTransferSession?
    private var pool: CVPixelBufferPool?
    private let lock = NSLock()

    public init(width: Int, height: Int, bitrateKbps: Int, expectedFps: Int = 60) throws {
        self.width = width
        self.height = height

        let spec: [CFString: Any] = [
            kVTVideoEncoderSpecification_EnableLowLatencyRateControl: true
        ]
        var session: VTCompressionSession?
        try check(
            VTCompressionSessionCreate(
                allocator: nil, width: Int32(width), height: Int32(height), codecType: kCMVideoCodecType_H264,
                encoderSpecification: spec as CFDictionary, imageBufferAttributes: nil,
                compressedDataAllocator: nil, outputCallback: nil, refcon: nil, compressionSessionOut: &session
            ), "VTCompressionSessionCreate")
        guard let session else { throw MediaCodecError.osStatus(-1, "no session") }

        let props: [CFString: Any] = [
            kVTCompressionPropertyKey_RealTime: true,
            kVTCompressionPropertyKey_ProfileLevel: kVTProfileLevel_H264_High_AutoLevel,
            kVTCompressionPropertyKey_AllowFrameReordering: false,
            kVTCompressionPropertyKey_AverageBitRate: bitrateKbps * 1000,
            kVTCompressionPropertyKey_ExpectedFrameRate: expectedFps,
            kVTCompressionPropertyKey_MaxKeyFrameInterval: expectedFps * 2,
        ]
        for (key, value) in props {
            VTSessionSetProperty(session, key: key, value: value as CFTypeRef)
        }
        VTCompressionSessionPrepareToEncodeFrames(session)
        self.session = session

        var transfer: VTPixelTransferSession?
        try check(
            VTPixelTransferSessionCreate(allocator: nil, pixelTransferSessionOut: &transfer),
            "VTPixelTransferSessionCreate")
        self.transfer = transfer

        let attrs: [CFString: Any] = [
            kCVPixelBufferPixelFormatTypeKey: kCVPixelFormatType_420YpCbCr8BiPlanarFullRange,
            kCVPixelBufferWidthKey: width,
            kCVPixelBufferHeightKey: height,
            kCVPixelBufferIOSurfacePropertiesKey: [:] as [CFString: Any],
        ]
        try check(CVPixelBufferPoolCreate(nil, nil, attrs as CFDictionary, &pool), "CVPixelBufferPoolCreate")
    }

    deinit {
        if let session {
            VTCompressionSessionCompleteFrames(session, untilPresentationTimeStamp: .invalid)
            VTCompressionSessionInvalidate(session)
        }
        if let transfer { VTPixelTransferSessionInvalidate(transfer) }
    }

    public func encode(
        _ pixelBuffer: CVPixelBuffer,
        pts: CMTime,
        forceKeyframe: Bool,
        completion: @escaping @Sendable (EncodedVideoFrame) -> Void
    ) {
        lock.lock(); defer { lock.unlock() }
        guard let session, let source = scaled(pixelBuffer) else { return }

        let frameProps: CFDictionary? =
            forceKeyframe
            ? [kVTEncodeFrameOptionKey_ForceKeyFrame: true] as CFDictionary
            : nil
        let (w, h) = (width, height)

        VTCompressionSessionEncodeFrame(
            session, imageBuffer: source, presentationTimeStamp: pts, duration: .invalid,
            frameProperties: frameProps, infoFlagsOut: nil
        ) { status, _, sample in
            guard status == noErr, let sample else { return }
            let isKey = Self.isKeyframe(sample)
            guard let data = AnnexB.fromSampleBuffer(sample, isKeyframe: isKey) else { return }
            completion(EncodedVideoFrame(data: data, isKeyframe: isKey, width: w, height: h))
        }
    }

    private func scaled(_ src: CVPixelBuffer) -> CVPixelBuffer? {
        if CVPixelBufferGetWidth(src) == width, CVPixelBufferGetHeight(src) == height { return src }
        guard let pool, let transfer else { return nil }
        var dst: CVPixelBuffer?
        guard CVPixelBufferPoolCreatePixelBuffer(nil, pool, &dst) == kCVReturnSuccess, let dst else { return nil }
        guard VTPixelTransferSessionTransferImage(transfer, from: src, to: dst) == noErr else { return nil }
        return dst
    }

    private static func isKeyframe(_ sample: CMSampleBuffer) -> Bool {
        guard
            let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: false)
                as? [[CFString: Any]], let first = attachments.first
        else { return true }
        return !(first[kCMSampleAttachmentKey_NotSync] as? Bool ?? false)
    }
}

func check(_ status: OSStatus, _ what: String) throws {
    guard status == noErr else { throw MediaCodecError.osStatus(status, what) }
}
