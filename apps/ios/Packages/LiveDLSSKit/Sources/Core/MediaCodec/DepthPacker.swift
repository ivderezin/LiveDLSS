import Accelerate
import Compression
import CoreVideo
import Foundation

public enum DepthPacker {
    public struct Packed: Sendable {
        public var width: Int
        public var height: Int
        public var rawSize: Int
        public var isCompressed: Bool
        public var data: Data
    }

    public static func pack(_ depth: CVPixelBuffer, crop: CGRect) -> Packed? {
        guard CVPixelBufferGetPixelFormatType(depth) == kCVPixelFormatType_DepthFloat32 else { return nil }
        CVPixelBufferLockBaseAddress(depth, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(depth, .readOnly) }

        let bounds = CGRect(x: 0, y: 0, width: CVPixelBufferGetWidth(depth), height: CVPixelBufferGetHeight(depth))
        let rect = crop.intersection(bounds)
        let width = Int(rect.width)
        let height = Int(rect.height)
        guard width > 0, height > 0, let base = CVPixelBufferGetBaseAddress(depth) else { return nil }

        let rowBytes = CVPixelBufferGetBytesPerRow(depth)
        let origin = base + Int(rect.minY) * rowBytes + Int(rect.minX) * MemoryLayout<Float32>.size
        var half = [UInt16](repeating: 0, count: width * height)
        var src = vImage_Buffer(
            data: origin, height: vImagePixelCount(height), width: vImagePixelCount(width), rowBytes: rowBytes
        )
        let converted = half.withUnsafeMutableBytes { dstPtr -> Bool in
            var dst = vImage_Buffer(
                data: dstPtr.baseAddress, height: vImagePixelCount(height), width: vImagePixelCount(width),
                rowBytes: width * 2
            )
            return vImageConvert_PlanarFtoPlanar16F(&src, &dst, 0) == kvImageNoError
        }
        guard converted else { return nil }

        let raw = half.withUnsafeBytes { Data($0) }
        if let lz4 = lz4Compress(raw), lz4.count < raw.count {
            return Packed(width: width, height: height, rawSize: raw.count, isCompressed: true, data: lz4)
        }
        return Packed(width: width, height: height, rawSize: raw.count, isCompressed: false, data: raw)
    }

    private static func lz4Compress(_ input: Data) -> Data? {
        let capacity = input.count + input.count / 255 + 64
        var out = Data(count: capacity)
        let written = out.withUnsafeMutableBytes { dst in
            input.withUnsafeBytes { src in
                compression_encode_buffer(
                    dst.bindMemory(to: UInt8.self).baseAddress!, capacity,
                    src.bindMemory(to: UInt8.self).baseAddress!, input.count,
                    nil, COMPRESSION_LZ4_RAW
                )
            }
        }
        guard written > 0 else { return nil }
        out.count = written
        return out
    }
}
