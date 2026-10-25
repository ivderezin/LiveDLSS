import CoreGraphics
import CoreVideo
import Foundation
import WireProtocol

public struct PackedDepth: Sendable {
    public var width: Int
    public var height: Int
    public var compression: DepthCompression
    public var rawSize: Int
    public var data: Data

    public init(width: Int, height: Int, compression: DepthCompression, rawSize: Int, data: Data) {
        self.width = width
        self.height = height
        self.compression = compression
        self.rawSize = rawSize
        self.data = data
    }
}

public struct CapturedFrame: @unchecked Sendable {
    public var frameId: UInt64
    public var timestampNs: UInt64
    public var pixelBuffer: CVPixelBuffer
    public var intrinsics: CameraIntrinsics
    public var cameraToWorld: [Float]
    public var depth: PackedDepth?

    public init(
        frameId: UInt64, timestampNs: UInt64, pixelBuffer: CVPixelBuffer,
        intrinsics: CameraIntrinsics, cameraToWorld: [Float], depth: PackedDepth?
    ) {
        self.frameId = frameId
        self.timestampNs = timestampNs
        self.pixelBuffer = pixelBuffer
        self.intrinsics = intrinsics
        self.cameraToWorld = cameraToWorld
        self.depth = depth
    }
}

public struct CapturePresentation: Sendable {
    public var imageSize: CGSize
    public var imageToPortrait: CGAffineTransform

    public init(imageSize: CGSize, imageToPortrait: CGAffineTransform) {
        self.imageSize = imageSize
        self.imageToPortrait = imageToPortrait
    }
}

public enum CaptureCamera: Sendable {
    case rear
    case front
}

public protocol FrameSource: AnyObject, Sendable {
    var supportsFrontCamera: Bool { get }
    var presentation: CapturePresentation? { get }
    func hasDepth(for camera: CaptureCamera) -> Bool
    func start(camera: CaptureCamera) -> AsyncStream<CapturedFrame>
    func stop()
}
