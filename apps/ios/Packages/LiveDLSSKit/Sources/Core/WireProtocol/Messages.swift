import Foundation

public enum VideoCodec: UInt8, Sendable, Codable, CaseIterable {
    case h264 = 1
    case hevc = 2
}

public enum QualityMode: UInt8, Sendable, Codable, CaseIterable {
    case dlaa = 0, quality, balanced, performance, ultraPerformance

    public var upscaleFactor: Float {
        switch self {
        case .dlaa: 1.0
        case .quality: 1.5
        case .balanced: 1.72
        case .performance: 2.0
        case .ultraPerformance: 3.0
        }
    }
}

public enum DebugView: UInt8, Sendable, Codable, CaseIterable {
    case final = 0, input, depth, motionVectors
}

public enum DepthSource: UInt8, Sendable, Codable, CaseIterable {
    case sensor = 0, constantPlane
}

public struct Hello: Equatable, Sendable {
    public var clientName: String
    public var hasDepth: Bool
    public var videoCodec: VideoCodec
    public var captureWidth: UInt16
    public var captureHeight: UInt16

    public init(clientName: String, hasDepth: Bool, videoCodec: VideoCodec, captureWidth: UInt16, captureHeight: UInt16)
    {
        self.clientName = clientName
        self.hasDepth = hasDepth
        self.videoCodec = videoCodec
        self.captureWidth = captureWidth
        self.captureHeight = captureHeight
    }
}

public struct HelloAck: Equatable, Sendable {
    public var serverName: String
    public var gpuName: String
    public var dlssAvailable: Bool
    public var statusMessage: String

    public init(serverName: String, gpuName: String, dlssAvailable: Bool, statusMessage: String) {
        self.serverName = serverName
        self.gpuName = gpuName
        self.dlssAvailable = dlssAvailable
        self.statusMessage = statusMessage
    }
}

public struct CameraIntrinsics: Equatable, Sendable {
    public var fx: Float, fy: Float, cx: Float, cy: Float

    public init(fx: Float, fy: Float, cx: Float, cy: Float) {
        self.fx = fx; self.fy = fy; self.cx = cx; self.cy = cy
    }

    public func scaled(by s: Float) -> CameraIntrinsics {
        CameraIntrinsics(fx: fx * s, fy: fy * s, cx: cx * s, cy: cy * s)
    }
}

public struct CameraFrame: Equatable, Sendable {
    public var frameId: UInt64
    public var timestampNs: UInt64
    public var codec: VideoCodec
    public var isKeyframe: Bool
    public var hasDepth: Bool
    public var width: UInt16
    public var height: UInt16
    public var intrinsics: CameraIntrinsics
    public var cameraToWorld: [Float]
    public var data: Data

    public init(
        frameId: UInt64, timestampNs: UInt64, codec: VideoCodec, isKeyframe: Bool, hasDepth: Bool,
        width: UInt16, height: UInt16, intrinsics: CameraIntrinsics, cameraToWorld: [Float], data: Data
    ) {
        precondition(cameraToWorld.count == 16)
        self.frameId = frameId
        self.timestampNs = timestampNs
        self.codec = codec
        self.isKeyframe = isKeyframe
        self.hasDepth = hasDepth
        self.width = width
        self.height = height
        self.intrinsics = intrinsics
        self.cameraToWorld = cameraToWorld
        self.data = data
    }
}

public enum DepthFormat: UInt8, Sendable { case float16Meters = 1 }
public enum DepthCompression: UInt8, Sendable { case none = 0, lz4 = 1 }

public struct DepthFrame: Equatable, Sendable {
    public var frameId: UInt64
    public var width: UInt16
    public var height: UInt16
    public var format: DepthFormat
    public var compression: DepthCompression
    public var rawSize: UInt32
    public var data: Data

    public init(
        frameId: UInt64, width: UInt16, height: UInt16, format: DepthFormat,
        compression: DepthCompression, rawSize: UInt32, data: Data
    ) {
        self.frameId = frameId
        self.width = width
        self.height = height
        self.format = format
        self.compression = compression
        self.rawSize = rawSize
        self.data = data
    }
}

public struct SettingsMessage: Equatable, Sendable {
    public var enabled: Bool
    public var qualityMode: QualityMode
    public var preset: UInt8
    public var debugView: DebugView
    public var depthSource: DepthSource
    public var sharpness: Float
    public var constantDepthMeters: Float
    public var downlinkBitrateKbps: UInt32

    public init(
        enabled: Bool, qualityMode: QualityMode, preset: UInt8, debugView: DebugView,
        depthSource: DepthSource, sharpness: Float, constantDepthMeters: Float, downlinkBitrateKbps: UInt32
    ) {
        self.enabled = enabled
        self.qualityMode = qualityMode
        self.preset = preset
        self.debugView = debugView
        self.depthSource = depthSource
        self.sharpness = sharpness
        self.constantDepthMeters = constantDepthMeters
        self.downlinkBitrateKbps = downlinkBitrateKbps
    }
}

public struct ProcessedFrame: Equatable, Sendable {
    public var frameId: UInt64
    public var codec: VideoCodec
    public var isKeyframe: Bool
    public var width: UInt16
    public var height: UInt16
    public var decodeUs: UInt32
    public var upscaleUs: UInt32
    public var encodeUs: UInt32
    public var data: Data

    public init(
        frameId: UInt64, codec: VideoCodec, isKeyframe: Bool, width: UInt16, height: UInt16,
        decodeUs: UInt32, upscaleUs: UInt32, encodeUs: UInt32, data: Data
    ) {
        self.frameId = frameId
        self.codec = codec
        self.isKeyframe = isKeyframe
        self.width = width
        self.height = height
        self.decodeUs = decodeUs
        self.upscaleUs = upscaleUs
        self.encodeUs = encodeUs
        self.data = data
    }
}

public enum Message: Equatable, Sendable {
    case hello(Hello)
    case helloAck(HelloAck)
    case cameraFrame(CameraFrame)
    case depthFrame(DepthFrame)
    case settings(SettingsMessage)
    case requestKeyframe
    case processedFrame(ProcessedFrame)
    case bye
    case unknown(type: UInt16)
}
