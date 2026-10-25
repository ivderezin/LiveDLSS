import Foundation

public struct ByteWriter: Sendable {
    public private(set) var data = Data()

    public init(capacity: Int = 64) {
        data.reserveCapacity(capacity)
    }

    public mutating func u8(_ v: UInt8) { data.append(v) }
    public mutating func u16(_ v: UInt16) { appendLE(v) }
    public mutating func u32(_ v: UInt32) { appendLE(v) }
    public mutating func u64(_ v: UInt64) { appendLE(v) }
    public mutating func f32(_ v: Float) { appendLE(v.bitPattern) }
    public mutating func bytes(_ d: Data) { data.append(d) }
    public mutating func zeros(_ n: Int) { data.append(contentsOf: repeatElement(0, count: n)) }

    public mutating func str(_ s: String) {
        let utf8 = Data(s.utf8.prefix(Int(UInt16.max)))
        u16(UInt16(utf8.count))
        data.append(utf8)
    }

    public mutating func blob(_ d: Data) {
        u32(UInt32(d.count))
        data.append(d)
    }

    private mutating func appendLE<T: FixedWidthInteger>(_ v: T) {
        withUnsafeBytes(of: v.littleEndian) { data.append(contentsOf: $0) }
    }
}

public enum WireError: Error, Equatable, Sendable {
    case truncated
    case badMagic
    case unsupportedVersion(UInt16)
    case payloadTooLarge(UInt32)
}

public struct ByteReader {
    private let data: Data
    private var offset: Int

    public init(_ data: Data) {
        self.data = data
        self.offset = data.startIndex
    }

    public var remaining: Int { data.endIndex - offset }

    public mutating func u8() throws -> UInt8 { try take(1).first! }
    public mutating func u16() throws -> UInt16 { try readLE() }
    public mutating func u32() throws -> UInt32 { try readLE() }
    public mutating func u64() throws -> UInt64 { try readLE() }
    public mutating func f32() throws -> Float { Float(bitPattern: try readLE()) }
    public mutating func skip(_ n: Int) throws { _ = try take(n) }

    public mutating func str() throws -> String {
        let len = Int(try u16())
        return String(decoding: try take(len), as: UTF8.self)
    }

    public mutating func blob() throws -> Data {
        let len = Int(try u32())
        return Data(try take(len))
    }

    public mutating func take(_ n: Int) throws -> Data {
        guard n >= 0, n <= remaining else { throw WireError.truncated }
        defer { offset += n }
        return data[offset..<offset + n]
    }

    private mutating func readLE<T: FixedWidthInteger>() throws -> T {
        let bytes = try take(MemoryLayout<T>.size)
        var v: T = 0
        for (i, b) in bytes.enumerated() { v |= T(b) << (8 * i) }
        return v
    }
}
