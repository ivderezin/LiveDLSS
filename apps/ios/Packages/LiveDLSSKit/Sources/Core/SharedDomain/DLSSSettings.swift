import Foundation
import WireProtocol

public enum CaptureResolution: String, Sendable, Codable, CaseIterable, Identifiable {
    case p480 = "640×480"
    case p720 = "960×720"
    case p960 = "1280×960"
    case p1440 = "1920×1440"

    public var id: String { rawValue }

    public var size: (width: Int, height: Int) {
        switch self {
        case .p480: (640, 480)
        case .p720: (960, 720)
        case .p960: (1280, 960)
        case .p1440: (1920, 1440)
        }
    }
}

public enum DLSSPreset: UInt8, Sendable, Codable, CaseIterable, Identifiable {
    case `default` = 0
    case j = 10, k = 11, l = 12, m = 13

    public var id: UInt8 { rawValue }
    public var title: String {
        self == .default
            ? String(localized: "Default", bundle: .module)
            : String(localized: "Preset \(String(describing: self).uppercased())", bundle: .module)
    }
}

public struct DLSSSettings: Sendable, Codable, Equatable {
    public var enabled = true
    public var qualityMode: QualityMode = .performance
    public var preset: DLSSPreset = .default
    public var sharpness: Float = 0
    public var debugView: DebugView = .final
    public var depthSource: DepthSource = .sensor
    public var constantDepthMeters: Float = 2
    public var captureResolution: CaptureResolution = .p720
    public var uplinkBitrateKbps: Int = 8_000
    public var downlinkBitrateKbps: Int = 25_000

    public init() {}

    public var wireMessage: SettingsMessage {
        SettingsMessage(
            enabled: enabled,
            qualityMode: qualityMode,
            preset: preset.rawValue,
            debugView: debugView,
            depthSource: depthSource,
            sharpness: sharpness,
            constantDepthMeters: constantDepthMeters,
            downlinkBitrateKbps: UInt32(downlinkBitrateKbps)
        )
    }
}

extension QualityMode {
    public var title: String {
        switch self {
        case .dlaa: "DLAA (1×)"
        case .quality: String(localized: "Quality (1.5×)", bundle: .module)
        case .balanced: String(localized: "Balanced (1.72×)", bundle: .module)
        case .performance: String(localized: "Performance (2×)", bundle: .module)
        case .ultraPerformance: String(localized: "Ultra Performance (3×)", bundle: .module)
        }
    }
}

extension DebugView {
    public var title: String {
        switch self {
        case .final: String(localized: "Result", bundle: .module)
        case .input: String(localized: "Input", bundle: .module)
        case .depth: String(localized: "Depth", bundle: .module)
        case .motionVectors: String(localized: "Motion vectors", bundle: .module)
        }
    }
}

extension DepthSource {
    public var title: String {
        switch self {
        case .sensor: String(localized: "Depth sensor", bundle: .module)
        case .constantPlane: String(localized: "Plane", bundle: .module)
        }
    }
}
