import AVFoundation
import MediaCodec
import SharedDomain
import SwiftUI
import UIKit

@MainActor
final class RemoteVideoRenderer {
    let layer = AVSampleBufferDisplayLayer()
    private var builder = H264SampleBufferBuilder()

    init() {
        layer.videoGravity = .resize
        layer.backgroundColor = UIColor.black.cgColor
    }

    func reset() {
        layer.sampleBufferRenderer.flush()
        builder = H264SampleBufferBuilder()
    }

    @discardableResult
    func enqueue(annexB: Data) -> Bool {
        let renderer = layer.sampleBufferRenderer
        if renderer.status == .failed {
            renderer.flush()
            return false
        }
        guard let sample = builder.makeSampleBuffer(annexB: annexB) else {
            return builder.hasFormat
        }
        renderer.enqueue(sample)
        return true
    }
}

struct RemoteVideoView: UIViewRepresentable {
    let renderer: RemoteVideoRenderer
    let presentation: CapturePresentation

    func makeUIView(context: Context) -> VideoLayerView {
        VideoLayerView(videoLayer: renderer.layer, presentation: presentation)
    }

    func updateUIView(_ uiView: VideoLayerView, context: Context) {
        uiView.presentation = presentation
    }

    final class VideoLayerView: UIView {
        private let videoLayer: CALayer
        var presentation: CapturePresentation { didSet { setNeedsLayout() } }

        init(videoLayer: CALayer, presentation: CapturePresentation) {
            self.videoLayer = videoLayer
            self.presentation = presentation
            super.init(frame: .zero)
            backgroundColor = .black
            videoLayer.anchorPoint = .zero
            layer.addSublayer(videoLayer)
        }

        @available(*, unavailable)
        required init?(coder: NSCoder) { fatalError() }

        override func layoutSubviews() {
            super.layoutSubviews()
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            videoLayer.setAffineTransform(.identity)
            let image = presentation.imageSize
            guard image.width > 0, image.height > 0, bounds.width > 0, bounds.height > 0 else {
                CATransaction.commit()
                return
            }
            let portrait = CGSize(width: min(image.width, image.height), height: max(image.width, image.height))
            let scale = min(bounds.width / portrait.width, bounds.height / portrait.height)
            let fitted = CGSize(width: portrait.width * scale, height: portrait.height * scale)
            videoLayer.bounds = CGRect(origin: .zero, size: image)
            videoLayer.position = CGPoint(x: (bounds.width - fitted.width) / 2, y: (bounds.height - fitted.height) / 2)
            videoLayer.setAffineTransform(
                CGAffineTransform(scaleX: 1 / image.width, y: 1 / image.height)
                    .concatenating(presentation.imageToPortrait)
                    .concatenating(CGAffineTransform(scaleX: fitted.width, y: fitted.height)))
            CATransaction.commit()
        }
    }
}
