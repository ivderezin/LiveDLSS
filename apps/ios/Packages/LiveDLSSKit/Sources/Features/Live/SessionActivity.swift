import ActivityKit
import Foundation
import LiveSession

@MainActor
final class SessionActivity {
    private var activity: Activity<LiveSessionAttributes>?
    private var state: LiveSessionAttributes.ContentState?

    static func endStale() async {
        for activity in Activity<LiveSessionAttributes>.activities {
            await activity.end(nil, dismissalPolicy: .immediate)
        }
    }

    func start(serverName: String, gpuName: String, dlssEnabled: Bool) {
        guard activity == nil, ActivityAuthorizationInfo().areActivitiesEnabled else {
            update(isPaused: false, dlssEnabled: dlssEnabled)
            return
        }
        let state = LiveSessionAttributes.ContentState(isPaused: false, startedAt: .now, dlssEnabled: dlssEnabled)
        activity = try? Activity.request(
            attributes: LiveSessionAttributes(serverName: serverName, gpuName: gpuName),
            content: ActivityContent(state: state, staleDate: nil))
        self.state = state
    }

    @discardableResult
    func update(isPaused: Bool, dlssEnabled: Bool) -> Task<Void, Never> {
        guard let activity, var state else { return Task {} }
        state.isPaused = isPaused
        state.dlssEnabled = dlssEnabled
        guard state != self.state else { return Task {} }
        self.state = state
        let content = ActivityContent(state: state, staleDate: nil)
        nonisolated(unsafe) let target = activity
        return Task { await target.update(content) }
    }

    func end() {
        guard let activity else { return }
        self.activity = nil
        state = nil
        nonisolated(unsafe) let target = activity
        Task { await target.end(nil, dismissalPolicy: .immediate) }
    }
}
