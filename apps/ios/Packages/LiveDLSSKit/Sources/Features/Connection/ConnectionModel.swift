import Foundation
import Observation
import Transport

@MainActor
@Observable
public final class ConnectionModel {
    public private(set) var servers: [DiscoveredServer] = []
    public var manualHost: String {
        didSet { UserDefaults.standard.set(manualHost, forKey: Self.hostKey) }
    }

    private static let hostKey = "livedlss.lastHost"
    private let browser = ServiceBrowser()
    private let onConnect: @MainActor (PCConnection) -> Void

    public init(onConnect: @escaping @MainActor (PCConnection) -> Void) {
        self.onConnect = onConnect
        manualHost = UserDefaults.standard.string(forKey: Self.hostKey) ?? ""
    }

    func startDiscovery() {
        browser.start { [weak self] servers in
            Task { @MainActor in self?.servers = servers }
        }
    }

    func stopDiscovery() {
        browser.stop()
    }

    func connect(to server: DiscoveredServer) {
        onConnect(PCConnection(endpoint: server.endpoint, description: server.name))
    }

    func connectManually() {
        let parts = manualHost.trimmingCharacters(in: .whitespaces).split(separator: ":")
        guard let host = parts.first.map(String.init), !host.isEmpty else { return }
        let port = parts.count > 1 ? UInt16(parts[1]) ?? ServiceConstants.defaultPort : ServiceConstants.defaultPort
        onConnect(PCConnection(host: host, port: port))
    }
}
