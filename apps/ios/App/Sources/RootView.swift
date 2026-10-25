import ConnectionFeature
import LiveFeature
import SettingsFeature
import SwiftUI

struct RootView: View {
    @Bindable var container: AppContainer

    var body: some View {
        Group {
            switch container.route {
            case .connection:
                NavigationStack {
                    ConnectionView(model: container.connectionModel) {
                        container.isSettingsPresented = true
                    }
                }
            case .live:
                if let live = container.liveModel {
                    LiveView(
                        model: live,
                        settingsStore: container.settingsStore,
                        onOpenSettings: { container.isSettingsPresented = true },
                        onClose: container.closeLive
                    )
                }
            }
        }
        .sheet(isPresented: $container.isSettingsPresented) {
            NavigationStack {
                SettingsView(
                    store: container.settingsStore,
                    hasDepth: container.frameSource.hasDepth(for: .rear) || container.frameSource.hasDepth(for: .front)
                )
                .toolbar {
                    Button("Done") { container.isSettingsPresented = false }
                }
            }
            .presentationDetents([.medium, .large])
        }
    }
}
