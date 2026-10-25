import Foundation
import Network
import Transport

public struct DiscoveredServer: Identifiable, Hashable, Sendable {
    public var id: String { name }
    public let name: String
    let endpoint: NWEndpoint
}

final class ServiceBrowser: @unchecked Sendable {
    private var browser: NWBrowser?

    func start(onChange: @escaping @Sendable ([DiscoveredServer]) -> Void) {
        let browser = NWBrowser(for: .bonjour(type: ServiceConstants.bonjourType, domain: nil), using: .tcp)
        browser.browseResultsChangedHandler = { results, _ in
            let servers = results.compactMap { result -> DiscoveredServer? in
                guard case let .service(name, _, _, _) = result.endpoint else { return nil }
                return DiscoveredServer(name: name, endpoint: result.endpoint)
            }
            onChange(servers.sorted { $0.name < $1.name })
        }
        browser.start(queue: .main)
        self.browser = browser
    }

    func stop() {
        browser?.cancel()
        browser = nil
    }
}
