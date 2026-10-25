import Foundation
import Observation

@MainActor
@Observable
public final class SettingsStore {
    private static let key = "livedlss.settings.v1"
    private let defaults: UserDefaults

    public var settings: DLSSSettings {
        didSet { persist() }
    }

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if let data = defaults.data(forKey: Self.key),
            let saved = try? JSONDecoder().decode(DLSSSettings.self, from: data)
        {
            settings = saved
        } else {
            settings = DLSSSettings()
        }
    }

    private func persist() {
        if let data = try? JSONEncoder().encode(settings) {
            defaults.set(data, forKey: Self.key)
        }
    }
}
