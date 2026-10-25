import ARKit
import AVFoundation
import CoreVideo
import Foundation
import MediaCodec
import SharedDomain
import WireProtocol
import os

public final class ARFrameSource: NSObject, FrameSource, ARSessionDelegate, @unchecked Sendable {
    public let supportsFrontCamera: Bool

    private let hasLiDAR: Bool
    private let session = ARSession()
    private let queue = DispatchQueue(label: "livedlss.capture", qos: .userInteractive)
    private var continuation: AsyncStream<CapturedFrame>.Continuation?
    private var nextFrameId: UInt64 = 0
    private let capturePresentation = OSAllocatedUnfairLock<CapturePresentation?>(initialState: nil)
    private var lastDepthTimestamp: TimeInterval = 0

    public var presentation: CapturePresentation? { capturePresentation.withLock { $0 } }

    public override init() {
        hasLiDAR = ARWorldTrackingConfiguration.supportsFrameSemantics(.sceneDepth)
        supportsFrontCamera =
            ARFaceTrackingConfiguration.isSupported && ARFaceTrackingConfiguration.supportsWorldTracking
        super.init()
        session.delegate = self
        session.delegateQueue = queue
    }

    public func hasDepth(for camera: CaptureCamera) -> Bool {
        switch camera {
        case .rear: hasLiDAR
        case .front: supportsFrontCamera
        }
    }

    public func start(camera: CaptureCamera) -> AsyncStream<CapturedFrame> {
        let (stream, continuation) = AsyncStream.makeStream(
            of: CapturedFrame.self,
            bufferingPolicy: .bufferingNewest(1)
        )
        queue.sync {
            self.continuation?.finish()
            self.continuation = continuation
            capturePresentation.withLock { $0 = nil }
            lastDepthTimestamp = 0

            let config: ARConfiguration
            let formats: [ARConfiguration.VideoFormat]
            if camera == .front && supportsFrontCamera {
                let front = ARFaceTrackingConfiguration()
                front.isWorldTrackingEnabled = true
                config = front
                formats = ARFaceTrackingConfiguration.supportedVideoFormats
            } else {
                let rear = ARWorldTrackingConfiguration()
                if hasLiDAR { rear.frameSemantics.insert(.sceneDepth) }
                rear.isAutoFocusEnabled = true
                config = rear
                formats = ARWorldTrackingConfiguration.supportedVideoFormats
            }
            if let format = formats.filter({ $0.framesPerSecond >= 60 })
                .max(by: { $0.imageResolution.width < $1.imageResolution.width })
            {
                config.videoFormat = format
            }
            session.run(config, options: [.resetTracking, .removeExistingAnchors])
        }
        return stream
    }

    public func stop() {
        queue.sync {
            session.pause()
            continuation?.finish()
            continuation = nil
        }
    }

    public func session(_ session: ARSession, didUpdate frame: ARFrame) {
        guard let continuation else { return }
        guard case .normal = frame.camera.trackingState else { return }

        let imageSize = CGSize(
            width: CVPixelBufferGetWidth(frame.capturedImage),
            height: CVPixelBufferGetHeight(frame.capturedImage))
        if presentation == nil {
            let viewport = CGSize(
                width: min(imageSize.width, imageSize.height), height: max(imageSize.width, imageSize.height))
            let current = CapturePresentation(
                imageSize: imageSize,
                imageToPortrait: frame.displayTransform(for: .portrait, viewportSize: viewport))
            capturePresentation.withLock { $0 = current }
        }

        let k = frame.camera.intrinsics
        let intrinsics = CameraIntrinsics(fx: k[0][0], fy: k[1][1], cx: k[2][0], cy: k[2][1])

        let t = frame.camera.transform
        let cameraToWorld = [t.columns.0, t.columns.1, t.columns.2, t.columns.3].flatMap { [$0.x, $0.y, $0.z, $0.w] }

        // Pack before yielding: holding on to the ARFrame makes ARKit drop frames.
        let depth = depthMap(of: frame, imageSize: imageSize).flatMap { map in
            DepthPacker.pack(map.buffer, crop: map.crop).map {
                PackedDepth(
                    width: $0.width, height: $0.height,
                    compression: $0.isCompressed ? .lz4 : .none, rawSize: $0.rawSize, data: $0.data
                )
            }
        }

        nextFrameId += 1
        continuation.yield(
            CapturedFrame(
                frameId: nextFrameId,
                timestampNs: UInt64(frame.timestamp * 1_000_000_000),
                pixelBuffer: frame.capturedImage,
                intrinsics: intrinsics,
                cameraToWorld: cameraToWorld,
                depth: depth
            ))
    }

    private func depthMap(of frame: ARFrame, imageSize: CGSize) -> (buffer: CVPixelBuffer, crop: CGRect)? {
        if let sceneDepth = frame.sceneDepth {
            let map = sceneDepth.depthMap
            return (map, CGRect(x: 0, y: 0, width: CVPixelBufferGetWidth(map), height: CVPixelBufferGetHeight(map)))
        }
        // TrueDepth delivers at a lower rate than video; send each sample once.
        guard let data = frame.capturedDepthData, frame.capturedDepthDataTimestamp != lastDepthTimestamp
        else { return nil }
        lastDepthTimestamp = frame.capturedDepthDataTimestamp
        let map = data.converting(toDepthDataType: kCVPixelFormatType_DepthFloat32).depthDataMap
        return (map, centerCrop(of: map, toAspectOf: imageSize))
    }

    private func centerCrop(of map: CVPixelBuffer, toAspectOf imageSize: CGSize) -> CGRect {
        let width = CGFloat(CVPixelBufferGetWidth(map))
        let height = CGFloat(CVPixelBufferGetHeight(map))
        let aspect = imageSize.width / imageSize.height
        if width / height > aspect {
            let cropped = (height * aspect).rounded()
            return CGRect(x: ((width - cropped) / 2).rounded(), y: 0, width: cropped, height: height)
        }
        let cropped = (width / aspect).rounded()
        return CGRect(x: 0, y: ((height - cropped) / 2).rounded(), width: width, height: cropped)
    }
}
