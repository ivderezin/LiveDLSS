import DesignSystem
import SharedDomain
import SwiftUI
import WireProtocol

public struct SettingsView: View {
    @Bindable var store: SettingsStore
    private let hasDepth: Bool

    public init(store: SettingsStore, hasDepth: Bool) {
        self.store = store
        self.hasDepth = hasDepth
    }

    public var body: some View {
        Form {
            Section {
                Toggle(isOn: $store.settings.enabled) { Text("Enable DLSS", bundle: .module) }
                Picker(selection: $store.settings.qualityMode) {
                    ForEach(QualityMode.allCases, id: \.self) { Text($0.title).tag($0) }
                } label: {
                    Text("Mode", bundle: .module)
                }
                Picker(selection: $store.settings.preset) {
                    ForEach(DLSSPreset.allCases) { Text($0.title).tag($0) }
                } label: {
                    Text("Model preset", bundle: .module)
                }
                VStack(alignment: .leading) {
                    Text(
                        "Sharpness: \(store.settings.sharpness, format: .number.precision(.fractionLength(2)))",
                        bundle: .module)
                    Slider(value: $store.settings.sharpness, in: 0...1)
                }
            } header: {
                Text(verbatim: "DLSS")
            }
            .disabled(!store.settings.enabled)

            Section {
                Picker(selection: $store.settings.depthSource) {
                    ForEach(DepthSource.allCases, id: \.self) { Text($0.title).tag($0) }
                } label: {
                    Text("Source", bundle: .module)
                }
                .disabled(!hasDepth)
                VStack(alignment: .leading) {
                    Text(
                        "Plane distance: \(store.settings.constantDepthMeters, format: .number.precision(.fractionLength(1))) m",
                        bundle: .module)
                    Slider(value: $store.settings.constantDepthMeters, in: 0.3...10)
                }
            } header: {
                Text("Depth / motion vectors", bundle: .module)
            } footer: {
                if hasDepth {
                    Text(
                        "Motion vectors are computed on the PC from the camera pose and sensor depth (LiDAR or TrueDepth).",
                        bundle: .module)
                } else {
                    Text(
                        "No depth sensor: motion vectors are computed from the camera pose and a plane at the set distance.",
                        bundle: .module)
                }
            }

            Section {
                Picker(selection: $store.settings.debugView) {
                    ForEach(DebugView.allCases, id: \.self) { Text($0.title).tag($0) }
                } label: {
                    Text("Return to iPhone", bundle: .module)
                }
            } header: {
                Text("Debug", bundle: .module)
            }

            Section {
                Picker(selection: $store.settings.captureResolution) {
                    ForEach(CaptureResolution.allCases) { Text(verbatim: $0.rawValue).tag($0) }
                } label: {
                    Text("Send resolution", bundle: .module)
                }
                Stepper(value: $store.settings.uplinkBitrateKbps, in: 2_000...50_000, step: 1_000) {
                    Text("Uplink: \(store.settings.uplinkBitrateKbps / 1000) Mbps", bundle: .module)
                }
                Stepper(value: $store.settings.downlinkBitrateKbps, in: 5_000...100_000, step: 5_000) {
                    Text("Downlink: \(store.settings.downlinkBitrateKbps / 1000) Mbps", bundle: .module)
                }
            } header: {
                Text("Network", bundle: .module)
            } footer: {
                let (w, h) = store.settings.captureResolution.size
                let f = store.settings.enabled ? store.settings.qualityMode.upscaleFactor : 1
                Text("Output ≈ \(Int(Float(w) * f))×\(Int(Float(h) * f))", bundle: .module)
            }
        }
        .navigationTitle(Text("Settings", bundle: .module))
    }
}
