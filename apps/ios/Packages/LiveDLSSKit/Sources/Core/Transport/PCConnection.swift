import Foundation
import Network
import WireProtocol

public enum ServiceConstants {
    public static let bonjourType = "_livedlss._tcp"
    public static let defaultPort: UInt16 = 47800
}

public enum ConnectionState: Equatable, Sendable {
    case idle
    case connecting
    case ready
    case failed(String)
    case closed
}

public final class PCConnection: @unchecked Sendable {
    public let endpointDescription: String
    public let messages: AsyncStream<Message>
    public let videoFrames: AsyncStream<ProcessedFrame>
    public let states: AsyncStream<ConnectionState>

    private let endpoint: NWEndpoint
    private let connection: NWConnection
    private let queue = DispatchQueue(label: "livedlss.transport", qos: .userInteractive)
    private let messagesContinuation: AsyncStream<Message>.Continuation
    private let videoContinuation: AsyncStream<ProcessedFrame>.Continuation
    private let statesContinuation: AsyncStream<ConnectionState>.Continuation

    private var inFlightVideo = 0
    private var awaitingKeyframe = false
    private let maxInFlightVideo = 3

    public convenience init(host: String, port: UInt16 = ServiceConstants.defaultPort) {
        self.init(
            endpoint: .hostPort(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!),
            description: "\(host):\(port)"
        )
    }

    public init(endpoint: NWEndpoint, description: String) {
        self.endpoint = endpoint
        endpointDescription = description
        let tcp = NWProtocolTCP.Options()
        tcp.noDelay = true
        tcp.enableKeepalive = true
        tcp.keepaliveIdle = 5
        tcp.keepaliveInterval = 1
        tcp.keepaliveCount = 3
        let params = NWParameters(tls: nil, tcp: tcp)
        params.serviceClass = .interactiveVideo
        connection = NWConnection(to: endpoint, using: params)

        (messages, messagesContinuation) = AsyncStream.makeStream()
        (videoFrames, videoContinuation) = AsyncStream.makeStream(bufferingPolicy: .bufferingOldest(4))
        (states, statesContinuation) = AsyncStream.makeStream(bufferingPolicy: .bufferingNewest(4))
    }

    public func start() {
        statesContinuation.yield(.connecting)
        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                statesContinuation.yield(.ready)
                receiveHeader()
            case .failed(let error):
                finish(.failed(error.localizedDescription))
            case .waiting(let error):
                statesContinuation.yield(.failed(error.localizedDescription))
            case .cancelled:
                finish(.closed)
            default:
                break
            }
        }
        connection.start(queue: queue)
    }

    public func reopened() -> PCConnection {
        PCConnection(endpoint: endpoint, description: endpointDescription)
    }

    public func close() {
        send(.bye)
        connection.cancel()
    }

    public func send(_ message: Message) {
        connection.send(content: MessageCodec.encode(message), completion: .contentProcessed { _ in })
    }

    public func resyncVideo() {
        queue.async { self.awaitingKeyframe = true }
        send(.requestKeyframe)
    }

    @discardableResult
    public func sendVideo(_ messages: [Message]) -> Bool {
        queue.sync {
            guard inFlightVideo < maxInFlightVideo else { return false }
            inFlightVideo += 1
            var data = Data()
            for m in messages { data.append(MessageCodec.encode(m)) }
            connection.send(
                content: data,
                completion: .contentProcessed { [weak self] _ in
                    self?.inFlightVideo -= 1
                })
            return true
        }
    }

    private func receiveHeader() {
        receiveExactly(MessageHeader.size) { [weak self] data in
            guard let self else { return }
            do {
                let header = try MessageHeader.decode(data)
                receivePayload(header)
            } catch {
                finish(.failed(String(localized: "Protocol error: \(String(describing: error))", bundle: .module)))
            }
        }
    }

    private func receivePayload(_ header: MessageHeader) {
        guard header.payloadSize > 0 else {
            deliver(header, Data())
            return
        }
        receiveExactly(Int(header.payloadSize)) { [weak self] payload in
            self?.deliver(header, payload)
        }
    }

    private func deliver(_ header: MessageHeader, _ payload: Data) {
        do {
            let message = try MessageCodec.decode(header: header, payload: payload)
            if message == .bye {
                finish(.closed)
                return
            }
            if case .processedFrame(let frame) = message {
                deliverVideo(frame)
            } else {
                messagesContinuation.yield(message)
            }
            receiveHeader()
        } catch {
            finish(.failed(String(localized: "Decode error: \(String(describing: error))", bundle: .module)))
        }
    }

    private func deliverVideo(_ frame: ProcessedFrame) {
        if awaitingKeyframe {
            guard frame.isKeyframe else { return }
            awaitingKeyframe = false
        }
        if case .dropped = videoContinuation.yield(frame) {
            awaitingKeyframe = true
            send(.requestKeyframe)
        }
    }

    private func receiveExactly(_ count: Int, _ completion: @escaping @Sendable (Data) -> Void) {
        connection.receive(minimumIncompleteLength: count, maximumLength: count) {
            [weak self] data, _, isComplete, error in
            guard let self else { return }
            if let error {
                finish(.failed(error.localizedDescription))
            } else if let data, data.count == count {
                completion(data)
            } else if isComplete {
                finish(.closed)
            } else {
                finish(.failed(String(localized: "Connection interrupted", bundle: .module)))
            }
        }
    }

    private func finish(_ state: ConnectionState) {
        statesContinuation.yield(state)
        statesContinuation.finish()
        messagesContinuation.finish()
        videoContinuation.finish()
        connection.cancel()
    }
}
