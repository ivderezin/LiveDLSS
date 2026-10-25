import CoreMedia
import CoreVideo
import Foundation
import MediaCodec
import SharedDomain
import Transport
import WireProtocol

actor UplinkPipeline {
    private let connection: PCConnection
    private let latency: LatencyTracker
    private var settings: DLSSSettings
    private var encoder: H264Encoder?
    private var encoderBitrate = 0
    private var forceKeyframe = true

    init(connection: PCConnection, settings: DLSSSettings, latency: LatencyTracker) {
        self.connection = connection
        self.settings = settings
        self.latency = latency
    }

    func update(_ settings: DLSSSettings) {
        self.settings = settings
    }

    func requestKeyframe() {
        forceKeyframe = true
    }

    func resetForCameraChange() {
        encoder = nil
        encoderBitrate = 0
        forceKeyframe = true
    }

    func run(frames: AsyncStream<CapturedFrame>) async {
        for await frame in frames {
            guard !Task.isCancelled else { break }
            process(frame)
        }
    }

    private func process(_ frame: CapturedFrame) {
        let width = settings.captureResolution.size.width
        let sourceWidth = CVPixelBufferGetWidth(frame.pixelBuffer)
        let sourceHeight = CVPixelBufferGetHeight(frame.pixelBuffer)
        let height = Int((Double(width) * Double(sourceHeight) / Double(sourceWidth) / 2).rounded()) * 2
        if encoder?.width != width || encoder?.height != height || encoderBitrate != settings.uplinkBitrateKbps {
            encoder = try? H264Encoder(width: width, height: height, bitrateKbps: settings.uplinkBitrateKbps)
            encoderBitrate = settings.uplinkBitrateKbps
            forceKeyframe = true
        }
        guard let encoder else { return }

        let scaleX = Float(width) / Float(sourceWidth)
        let scaleY = Float(height) / Float(sourceHeight)
        let intrinsics = CameraIntrinsics(
            fx: frame.intrinsics.fx * scaleX, fy: frame.intrinsics.fy * scaleY,
            cx: frame.intrinsics.cx * scaleX, cy: frame.intrinsics.cy * scaleY)
        let depth = settings.depthSource == .sensor ? frame.depth : nil
        let keyframe = forceKeyframe
        forceKeyframe = false

        let pts = CMTime(value: CMTimeValue(frame.timestampNs), timescale: 1_000_000_000)
        encoder.encode(frame.pixelBuffer, pts: pts, forceKeyframe: keyframe) { [connection, latency] encoded in
            var messages: [Message] = []
            if let depth {
                messages.append(
                    .depthFrame(
                        DepthFrame(
                            frameId: frame.frameId,
                            width: UInt16(depth.width), height: UInt16(depth.height),
                            format: .float16Meters, compression: depth.compression,
                            rawSize: UInt32(depth.rawSize), data: depth.data
                        )))
            }
            messages.append(
                .cameraFrame(
                    CameraFrame(
                        frameId: frame.frameId,
                        timestampNs: frame.timestampNs,
                        codec: .h264,
                        isKeyframe: encoded.isKeyframe,
                        hasDepth: depth != nil,
                        width: UInt16(encoded.width), height: UInt16(encoded.height),
                        intrinsics: intrinsics,
                        cameraToWorld: frame.cameraToWorld,
                        data: encoded.data
                    )))

            if connection.sendVideo(messages) {
                latency.markSent(frame.frameId)
            } else {
                latency.markDropped()
                Task { await self.requestKeyframe() }
            }
        }
    }
}

final class LatencyTracker: @unchecked Sendable {
    private let lock = NSLock()
    private var sent: [UInt64: UInt64] = [:]
    private(set) var droppedFrames = 0

    func markSent(_ frameId: UInt64) {
        lock.withLock {
            sent[frameId] = DispatchTime.now().uptimeNanoseconds
            if sent.count > 240 { sent = sent.filter { $0.key + 120 > frameId } }
        }
    }

    func markDropped() {
        lock.withLock { droppedFrames += 1 }
    }

    func markReceived(_ frameId: UInt64) -> Double? {
        lock.withLock {
            guard let t0 = sent.removeValue(forKey: frameId) else { return nil }
            return Double(DispatchTime.now().uptimeNanoseconds - t0) / 1_000_000
        }
    }

    var dropped: Int { lock.withLock { droppedFrames } }
}
