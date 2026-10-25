import DesignSystem
import SwiftUI

public struct ConnectionView: View {
    @Bindable var model: ConnectionModel
    private let onOpenSettings: () -> Void

    public init(model: ConnectionModel, onOpenSettings: @escaping () -> Void) {
        self.model = model
        self.onOpenSettings = onOpenSettings
    }

    public var body: some View {
        List {
            Section {
                if model.servers.isEmpty {
                    HStack(spacing: DS.Spacing.s) {
                        ProgressView()
                        Text("Looking for LiveDLSS Server on the network…", bundle: .module).foregroundStyle(.secondary)
                    }
                }
                ForEach(model.servers) { server in
                    Button {
                        model.connect(to: server)
                    } label: {
                        Label(server.name, systemImage: "desktopcomputer")
                    }
                }
            } header: {
                Text("Discovered PCs", bundle: .module)
            }

            Section {
                TextField(text: $model.manualHost) { Text(verbatim: "192.168.1.10:47800") }
                    .keyboardType(.URL)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                Button(action: model.connectManually) { Text("Connect", bundle: .module) }
                    .disabled(model.manualHost.isEmpty)
            } header: {
                Text("Manual", bundle: .module)
            }
        }
        .navigationTitle(Text(verbatim: "LiveDLSS"))
        .toolbar {
            Button(action: onOpenSettings) {
                Image(systemName: "slider.horizontal.3")
            }
            .accessibilityLabel(Text("Settings", bundle: .module))
        }
        .onAppear(perform: model.startDiscovery)
        .onDisappear(perform: model.stopDiscovery)
    }
}
