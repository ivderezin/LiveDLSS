import Foundation
import Observation
import SharedDomain
import Transport
import UIKit
import WireProtocol

public struct LiveStats: Equatable, Sendable {
    public var fps: Double = 0
    public var roundTripMs: Double = 0
    public var serverDecodeMs: Double = 0
    public var upscaleMs: Double = 0
    public var serverEncodeMs: Double = 0
    public var outputSize = "—"
    public var droppedFrames = 0
}

@MainActor
@Observable
public final class LiveModel {
    public enum Phase: Equatable {
        case connecting
        case streaming
        case failed(String)
        case closed
    }

    public private(set) var phase: Phase = .connecting
    public private(set) var server: HelloAck?
    public private(set) var stats = LiveStats()
    public private(set) var camera: CaptureCamera = .rear
    public private(set) var isSwitchingCamera = false
    public private(set) var presentation = CapturePresentation(
        imageSize: CGSize(width: 1920, height: 1440),
        imageToPortrait: CGAffineTransform(a: 0, b: 1, c: -1, d: 0, tx: 1, ty: 0))
    public var supportsFrontCamera: Bool { source.supportsFrontCamera }

    let renderer = RemoteVideoRenderer()

    private var connection: PCConnection
    private let source: FrameSource
    private let settingsStore: SettingsStore
    private let latency = LatencyTracker()
    private var uplink: UplinkPipeline
    private let activity = SessionActivity()
    private var tasks: [Task<Void, Never>] = []
    private var isInBackground = false
    private var connectionIsReady = false
    private var resumeTask: Task<Void, Never>?
    private var backgroundTask: UIBackgroundTaskIdentifier = .invalid
    private var needsPresentationUpdate = true
    private var captureTask: Task<Void, Never>?
    private var cameraSwitchTask: Task<Void, Never>?
    private var fpsWindow: [Date] = []

    public init(connection: PCConnection, source: FrameSource, settingsStore: SettingsStore) {
        self.connection = connection
        self.source = source
        self.settingsStore = settingsStore
        uplink = UplinkPipeline(connection: connection, settings: settingsStore.settings, latency: latency)
    }

    public var endpointDescription: String { connection.endpointDescription }

    public static func endStaleActivities() async {
        await SessionActivity.endStale()
    }

    func start() {
        guard tasks.isEmpty else { return }
        UIApplication.shared.isIdleTimerDisabled = true
        attach()
    }

    private func attach() {
        tasks.append(
            Task { [weak self, connection] in
                for await state in connection.states {
                    guard !Task.isCancelled else { break }
                    self?.handle(state)
                }
            })
        tasks.append(
            Task { [weak self, connection] in
                for await message in connection.messages {
                    guard !Task.isCancelled else { break }
                    self?.handle(message)
                }
            })
        tasks.append(
            Task { [weak self, connection] in
                for await frame in connection.videoFrames {
                    guard !Task.isCancelled else { break }
                    self?.handle(frame)
                }
            })
        connection.start()
    }

    func stop() {
        UIApplication.shared.isIdleTimerDisabled = false
        detach()
        phase = .closed
        activity.end()
        endBackgroundTask()
    }

    func enterBackground() {
        guard !isInBackground else { return }
        isInBackground = true
        cameraSwitchTask?.cancel()
        cameraSwitchTask = nil
        isSwitchingCamera = false
        pauseCapture()
        backgroundTask = UIApplication.shared.beginBackgroundTask(withName: "LiveDLSS pause") { [weak self] in
            self?.endBackgroundTask()
        }
        let update = activity.update(isPaused: true, dlssEnabled: settingsStore.settings.enabled)
        Task { [weak self] in
            await update.value
            self?.endBackgroundTask()
        }
    }

    func enterForeground() {
        guard isInBackground else { return }
        isInBackground = false
        endBackgroundTask()
        switch phase {
        case .streaming:
            resumeCapture()
            activity.update(isPaused: false, dlssEnabled: settingsStore.settings.enabled)
        case .failed, .closed:
            reconnect()
        case .connecting:
            if connectionIsReady {
                sendHello()
                startCapture()
            }
        }
    }

    private func detach() {
        cameraSwitchTask?.cancel()
        cameraSwitchTask = nil
        isSwitchingCamera = false
        pauseCapture()
        connection.close()
        connectionIsReady = false
        tasks.forEach { $0.cancel() }
        tasks.removeAll()
    }

    private func pauseCapture() {
        resumeTask?.cancel()
        resumeTask = nil
        source.stop()
        captureTask?.cancel()
        captureTask = nil
    }

    private func resumeCapture() {
        resumeTask?.cancel()
        renderer.reset()
        resumeTask = Task { [weak self] in
            guard let self else { return }
            await uplink.resetForCameraChange()
            guard !Task.isCancelled, !isInBackground, phase == .streaming else { return }
            sendHello()
            startCapture()
            connection.resyncVideo()
        }
    }

    private func reconnect() {
        detach()
        connection = connection.reopened()
        uplink = UplinkPipeline(connection: connection, settings: settingsStore.settings, latency: latency)
        server = nil
        phase = .connecting
        renderer.reset()
        attach()
    }

    private func endBackgroundTask() {
        guard backgroundTask != .invalid else { return }
        UIApplication.shared.endBackgroundTask(backgroundTask)
        backgroundTask = .invalid
    }

    func settingsChanged() {
        let settings = settingsStore.settings
        connection.send(.settings(settings.wireMessage))
        Task { await uplink.update(settings) }
        if phase == .streaming {
            activity.update(isPaused: isInBackground, dlssEnabled: settings.enabled)
        }
    }

    func switchCamera() {
        guard phase == .streaming, !isInBackground, supportsFrontCamera, !isSwitchingCamera else { return }
        isSwitchingCamera = true
        cameraSwitchTask = Task { [weak self] in
            guard let self else { return }
            source.stop()
            captureTask?.cancel()
            await captureTask?.value
            captureTask = nil
            await uplink.resetForCameraChange()
            guard !Task.isCancelled, !isInBackground, phase == .streaming else {
                isSwitchingCamera = false
                return
            }
            camera = camera == .rear ? .front : .rear
            renderer.reset()
            sendHello()
            startCapture()
            isSwitchingCamera = false
        }
    }

    private func sendHello() {
        let settings = settingsStore.settings
        let (w, h) = settings.captureResolution.size
        connection.send(
            .hello(
                Hello(
                    clientName: UIDevice.current.name, hasDepth: source.hasDepth(for: camera),
                    videoCodec: .h264, captureWidth: UInt16(w), captureHeight: UInt16(h)
                )))
        connection.send(.settings(settings.wireMessage))
    }

    private func handle(_ state: ConnectionState) {
        switch state {
        case .ready:
            connectionIsReady = true
            guard !isInBackground else { return }
            sendHello()
            startCapture()
        case .failed(let reason):
            connectionIsReady = false
            pauseCapture()
            phase = .failed(reason)
            if !isInBackground { activity.end() }
        case .closed:
            connectionIsReady = false
            if case .failed = phase {} else { phase = .closed }
            pauseCapture()
            if !isInBackground { activity.end() }
        case .idle, .connecting:
            break
        }
    }

    private func startCapture() {
        needsPresentationUpdate = true
        let frames = source.start(camera: camera)
        captureTask = Task.detached(priority: .userInitiated) { [uplink] in
            await uplink.run(frames: frames)
        }
    }

    private func handle(_ message: Message) {
        switch message {
        case .helloAck(let ack):
            server = ack
            phase = .streaming
            activity.start(
                serverName: ack.serverName, gpuName: ack.gpuName, dlssEnabled: settingsStore.settings.enabled)
        case .requestKeyframe:
            Task { await uplink.requestKeyframe() }
        default:
            break
        }
    }

    private func handle(_ frame: ProcessedFrame) {
        guard !isInBackground else { return }
        if needsPresentationUpdate, let current = source.presentation {
            presentation = current
            needsPresentationUpdate = false
        }
        if !renderer.enqueue(annexB: frame.data) {
            connection.resyncVideo()
        }
        updateStats(with: frame)
    }

    private func updateStats(with frame: ProcessedFrame) {
        let now = Date()
        fpsWindow.append(now)
        fpsWindow.removeAll { now.timeIntervalSince($0) > 1 }

        var s = stats
        s.fps = Double(fpsWindow.count)
        if let rtt = latency.markReceived(frame.frameId) {
            s.roundTripMs = s.roundTripMs * 0.9 + rtt * 0.1
        }
        s.serverDecodeMs = Double(frame.decodeUs) / 1000
        s.upscaleMs = Double(frame.upscaleUs) / 1000
        s.serverEncodeMs = Double(frame.encodeUs) / 1000
        s.outputSize = "\(frame.width)×\(frame.height)"
        s.droppedFrames = latency.dropped
        stats = s
    }
}
