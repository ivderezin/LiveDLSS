import CaptureFeature
import ConnectionFeature
import LiveFeature
import Observation
import SharedDomain
import Transport

@MainActor
@Observable
final class AppContainer {
    enum Route: Equatable {
        case connection
        case live
    }

    let settingsStore = SettingsStore()
    let frameSource: FrameSource = ARFrameSource()

    private(set) var route: Route = .connection
    private(set) var liveModel: LiveModel?
    var isSettingsPresented = false

    @ObservationIgnored
    private(set) lazy var connectionModel = ConnectionModel { [weak self] connection in
        self?.startLive(with: connection)
    }

    init() {
        Task { await LiveModel.endStaleActivities() }
    }

    private func startLive(with connection: PCConnection) {
        liveModel = LiveModel(connection: connection, source: frameSource, settingsStore: settingsStore)
        route = .live
    }

    func closeLive() {
        liveModel = nil
        route = .connection
    }
}
