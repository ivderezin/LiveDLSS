import Foundation

public enum MessageType: UInt16, Sendable {
    case hello = 0x0001
    case helloAck = 0x0002
    case cameraFrame = 0x0010
    case depthFrame = 0x0011
    case settings = 0x0020
    case requestKeyframe = 0x0021
    case processedFrame = 0x0030
    case bye = 0x00FF
}

public struct MessageHeader: Equatable, Sendable {
    public static let size = 16
    public static let magic = Data("LDLS".utf8)
    public static let version: UInt16 = 1
    public static let maxPayloadSize: UInt32 = 16 * 1024 * 1024

    public var type: UInt16
    public var payloadSize: UInt32

    public init(type: UInt16, payloadSize: UInt32) {
        self.type = type
        self.payloadSize = payloadSize
    }

    public static func decode(_ data: Data) throws -> MessageHeader {
        var r = ByteReader(data)
        guard try r.take(4) == magic else { throw WireError.badMagic }
        let version = try r.u16()
        guard version == Self.version else { throw WireError.unsupportedVersion(version) }
        let type = try r.u16()
        let size = try r.u32()
        guard size <= maxPayloadSize else { throw WireError.payloadTooLarge(size) }
        return MessageHeader(type: type, payloadSize: size)
    }
}

public enum MessageCodec {
    public static func encode(_ message: Message) -> Data {
        var p = ByteWriter(capacity: 256)
        let type: MessageType

        switch message {
        case .hello(let m):
            type = .hello
            p.str(m.clientName)
            p.u8(m.hasDepth ? 1 : 0)
            p.u8(m.videoCodec.rawValue)
            p.u16(m.captureWidth)
            p.u16(m.captureHeight)

        case .helloAck(let m):
            type = .helloAck
            p.str(m.serverName)
            p.str(m.gpuName)
            p.u8(m.dlssAvailable ? 1 : 0)
            p.str(m.statusMessage)

        case .cameraFrame(let m):
            type = .cameraFrame
            p.u64(m.frameId)
            p.u64(m.timestampNs)
            p.u8(m.codec.rawValue)
            p.u8((m.isKeyframe ? 1 : 0) | (m.hasDepth ? 2 : 0))
            p.u16(m.width)
            p.u16(m.height)
            p.f32(m.intrinsics.fx); p.f32(m.intrinsics.fy)
            p.f32(m.intrinsics.cx); p.f32(m.intrinsics.cy)
            m.cameraToWorld.forEach { p.f32($0) }
            p.blob(m.data)

        case .depthFrame(let m):
            type = .depthFrame
            p.u64(m.frameId)
            p.u16(m.width)
            p.u16(m.height)
            p.u8(m.format.rawValue)
            p.u8(m.compression.rawValue)
            p.u32(m.rawSize)
            p.blob(m.data)

        case .settings(let m):
            type = .settings
            p.u8(m.enabled ? 1 : 0)
            p.u8(m.qualityMode.rawValue)
            p.u8(m.preset)
            p.u8(m.debugView.rawValue)
            p.u8(m.depthSource.rawValue)
            p.zeros(3)
            p.f32(m.sharpness)
            p.f32(m.constantDepthMeters)
            p.u32(m.downlinkBitrateKbps)

        case .requestKeyframe:
            type = .requestKeyframe

        case .processedFrame(let m):
            type = .processedFrame
            p.u64(m.frameId)
            p.u8(m.codec.rawValue)
            p.u8(m.isKeyframe ? 1 : 0)
            p.u16(m.width)
            p.u16(m.height)
            p.u32(m.decodeUs)
            p.u32(m.upscaleUs)
            p.u32(m.encodeUs)
            p.blob(m.data)

        case .bye:
            type = .bye

        case .unknown(let raw):
            return header(type: raw, payloadSize: 0)
        }

        var out = header(type: type.rawValue, payloadSize: UInt32(p.data.count))
        out.append(p.data)
        return out
    }

    public static func decode(header: MessageHeader, payload: Data) throws -> Message {
        guard let type = MessageType(rawValue: header.type) else { return .unknown(type: header.type) }
        var r = ByteReader(payload)

        switch type {
        case .hello:
            return .hello(
                Hello(
                    clientName: try r.str(),
                    hasDepth: try r.u8() != 0,
                    videoCodec: VideoCodec(rawValue: try r.u8()) ?? .h264,
                    captureWidth: try r.u16(),
                    captureHeight: try r.u16()
                ))

        case .helloAck:
            return .helloAck(
                HelloAck(
                    serverName: try r.str(),
                    gpuName: try r.str(),
                    dlssAvailable: try r.u8() != 0,
                    statusMessage: try r.str()
                ))

        case .cameraFrame:
            let frameId = try r.u64()
            let ts = try r.u64()
            let codec = VideoCodec(rawValue: try r.u8()) ?? .h264
            let flags = try r.u8()
            let w = try r.u16(), h = try r.u16()
            let k = CameraIntrinsics(fx: try r.f32(), fy: try r.f32(), cx: try r.f32(), cy: try r.f32())
            var m = [Float](); m.reserveCapacity(16)
            for _ in 0..<16 { m.append(try r.f32()) }
            return .cameraFrame(
                CameraFrame(
                    frameId: frameId, timestampNs: ts, codec: codec,
                    isKeyframe: flags & 1 != 0, hasDepth: flags & 2 != 0,
                    width: w, height: h, intrinsics: k, cameraToWorld: m, data: try r.blob()
                ))

        case .depthFrame:
            return .depthFrame(
                DepthFrame(
                    frameId: try r.u64(),
                    width: try r.u16(),
                    height: try r.u16(),
                    format: DepthFormat(rawValue: try r.u8()) ?? .float16Meters,
                    compression: DepthCompression(rawValue: try r.u8()) ?? .none,
                    rawSize: try r.u32(),
                    data: try r.blob()
                ))

        case .settings:
            let enabled = try r.u8() != 0
            let mode = QualityMode(rawValue: try r.u8()) ?? .quality
            let preset = try r.u8()
            let debug = DebugView(rawValue: try r.u8()) ?? .final
            let depth = DepthSource(rawValue: try r.u8()) ?? .sensor
            try r.skip(3)
            return .settings(
                SettingsMessage(
                    enabled: enabled, qualityMode: mode, preset: preset, debugView: debug, depthSource: depth,
                    sharpness: try r.f32(), constantDepthMeters: try r.f32(), downlinkBitrateKbps: try r.u32()
                ))

        case .requestKeyframe:
            return .requestKeyframe

        case .processedFrame:
            return .processedFrame(
                ProcessedFrame(
                    frameId: try r.u64(),
                    codec: VideoCodec(rawValue: try r.u8()) ?? .h264,
                    isKeyframe: try r.u8() & 1 != 0,
                    width: try r.u16(),
                    height: try r.u16(),
                    decodeUs: try r.u32(),
                    upscaleUs: try r.u32(),
                    encodeUs: try r.u32(),
                    data: try r.blob()
                ))

        case .bye:
            return .bye
        }
    }

    private static func header(type: UInt16, payloadSize: UInt32) -> Data {
        var w = ByteWriter(capacity: MessageHeader.size + Int(payloadSize))
        w.bytes(MessageHeader.magic)
        w.u16(MessageHeader.version)
        w.u16(type)
        w.u32(payloadSize)
        w.u32(0)
        return w.data
    }
}
