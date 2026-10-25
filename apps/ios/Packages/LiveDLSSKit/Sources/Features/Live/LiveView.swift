import DesignSystem
import SharedDomain
import SwiftUI

public struct LiveView: View {
    let model: LiveModel
    let settingsStore: SettingsStore
    @Environment(\.scenePhase) private var scenePhase
    private let onOpenSettings: () -> Void
    private let onClose: () -> Void

    public init(
        model: LiveModel, settingsStore: SettingsStore,
        onOpenSettings: @escaping () -> Void, onClose: @escaping () -> Void
    ) {
        self.model = model
        self.settingsStore = settingsStore
        self.onOpenSettings = onOpenSettings
        self.onClose = onClose
    }

    public var body: some View {
        ZStack {
            RemoteVideoView(renderer: model.renderer, presentation: model.presentation)
                .ignoresSafeArea()

            VStack {
                header
                Spacer()
                statsBar
                controls
            }
            .padding(DS.Spacing.m)

            if case .connecting = model.phase {
                ProgressView { Text("Connecting to \(model.endpointDescription)…", bundle: .module) }
                    .tint(.white).foregroundStyle(.white)
            }
        }
        .background(.black)
        .onAppear(perform: model.start)
        .onDisappear(perform: model.stop)
        .onChange(of: settingsStore.settings) { model.settingsChanged() }
        .onChange(of: scenePhase) { _, phase in
            switch phase {
            case .background: model.enterBackground()
            case .active: model.enterForeground()
            default: break
            }
        }
    }

    private var header: some View {
        HStack(spacing: DS.Spacing.s) {
            StatusDot(statusColor)
            Text(statusText).font(.footnote.weight(.semibold))
            Spacer()
            if let server = model.server {
                Text(server.dlssAvailable ? "DLSS ✓" : "DLSS ✗ (fallback)", bundle: .module)
                    .font(.caption.weight(.bold))
                    .foregroundStyle(server.dlssAvailable ? DS.Colors.accent : DS.Colors.warning)
            }
        }
        .padding(DS.Spacing.s)
        .background(DS.Colors.overlay, in: Capsule())
        .foregroundStyle(.white)
    }

    private var statsBar: some View {
        let s = model.stats
        return HStack(spacing: DS.Spacing.s) {
            StatBadge("FPS", s.fps.formatted(.number.precision(.fractionLength(0))))
            StatBadge("RTT", milliseconds(s.roundTripMs, digits: 0))
            StatBadge("DLSS", milliseconds(s.upscaleMs, digits: 1))
            StatBadge(String(localized: "Out", bundle: .module), s.outputSize)
            if s.droppedFrames > 0 { StatBadge(String(localized: "Drop", bundle: .module), "\(s.droppedFrames)") }
        }
    }

    private var controls: some View {
        HStack {
            Button(action: onClose) {
                Image(systemName: "xmark").frame(width: 44, height: 44)
            }
            .accessibilityLabel(Text("Disconnect", bundle: .module))
            Spacer()
            Toggle(
                isOn: Binding(
                    get: { settingsStore.settings.enabled },
                    set: { settingsStore.settings.enabled = $0 }
                )
            ) {
                Text(verbatim: "DLSS")
            }
            .fixedSize()
            Spacer()
            Button(action: model.switchCamera) {
                Image(systemName: "camera.rotate.fill").frame(width: 44, height: 44)
            }
            .accessibilityLabel(
                model.camera == .rear
                    ? Text("Switch to front camera", bundle: .module)
                    : Text("Switch to rear camera", bundle: .module)
            )
            .disabled(!model.supportsFrontCamera || model.isSwitchingCamera || model.phase != .streaming)
            Button(action: onOpenSettings) {
                Image(systemName: "slider.horizontal.3").frame(width: 44, height: 44)
            }
            .accessibilityLabel(Text("Settings", bundle: .module))
        }
        .buttonStyle(.plain)
        .padding(.horizontal, DS.Spacing.m)
        .background(DS.Colors.overlay, in: Capsule())
        .foregroundStyle(.white)
        .tint(DS.Colors.accent)
    }

    private var statusColor: Color {
        switch model.phase {
        case .streaming: DS.Colors.accent
        case .connecting: DS.Colors.warning
        case .failed, .closed: DS.Colors.danger
        }
    }

    private var statusText: String {
        switch model.phase {
        case .connecting: String(localized: "Connecting…", bundle: .module)
        case .streaming:
            model.server.map { "\($0.serverName) · \($0.gpuName)" }
                ?? String(localized: "Streaming", bundle: .module)
        case .failed(let reason): reason
        case .closed: String(localized: "Connection closed", bundle: .module)
        }
    }

    private func milliseconds(_ value: Double, digits: Int) -> String {
        let number = value.formatted(.number.precision(.fractionLength(digits)))
        return String(localized: "\(number) ms", bundle: .module)
    }
}
