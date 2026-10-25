import ActivityKit
import Foundation

public struct LiveSessionAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable, Sendable {
        public var isPaused: Bool
        public var startedAt: Date
        public var dlssEnabled: Bool

        public init(isPaused: Bool, startedAt: Date, dlssEnabled: Bool) {
            self.isPaused = isPaused
            self.startedAt = startedAt
            self.dlssEnabled = dlssEnabled
        }
    }

    public var serverName: String
    public var gpuName: String

    public init(serverName: String, gpuName: String) {
        self.serverName = serverName
        self.gpuName = gpuName
    }
}
