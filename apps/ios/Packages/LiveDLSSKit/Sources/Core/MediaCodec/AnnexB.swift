import CoreMedia
import Foundation

public enum AnnexB {
    public static let startCode: [UInt8] = [0, 0, 0, 1]

    public enum H264NALType: UInt8 {
        case slice = 1, idr = 5, sei = 6, sps = 7, pps = 8, aud = 9
    }

    public static func fromSampleBuffer(_ sample: CMSampleBuffer, isKeyframe: Bool) -> Data? {
        guard let block = CMSampleBufferGetDataBuffer(sample) else { return nil }
        var out = Data()

        if isKeyframe, let format = CMSampleBufferGetFormatDescription(sample) {
            var count = 0
            CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
                format, parameterSetIndex: 0, parameterSetPointerOut: nil, parameterSetSizeOut: nil,
                parameterSetCountOut: &count, nalUnitHeaderLengthOut: nil
            )
            for i in 0..<count {
                var ptr: UnsafePointer<UInt8>?
                var size = 0
                if CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
                    format, parameterSetIndex: i, parameterSetPointerOut: &ptr, parameterSetSizeOut: &size,
                    parameterSetCountOut: nil, nalUnitHeaderLengthOut: nil
                ) == noErr, let ptr {
                    out.append(contentsOf: startCode)
                    out.append(ptr, count: size)
                }
            }
        }

        var totalLength = 0
        var dataPointer: UnsafeMutablePointer<CChar>?
        guard
            CMBlockBufferGetDataPointer(
                block, atOffset: 0, lengthAtOffsetOut: nil, totalLengthOut: &totalLength, dataPointerOut: &dataPointer
            ) == kCMBlockBufferNoErr, let dataPointer
        else { return nil }

        let avcc = Data(bytesNoCopy: dataPointer, count: totalLength, deallocator: .none)
        guard let nals = avccToAnnexB(avcc) else { return nil }
        out.append(nals)
        return out
    }

    public static func avccToAnnexB(_ avcc: Data) -> Data? {
        var out = Data(capacity: avcc.count + 16)
        var offset = avcc.startIndex
        while offset + 4 <= avcc.endIndex {
            let len = avcc[offset..<offset + 4].reduce(0) { ($0 << 8) | Int($1) }
            offset += 4
            guard len > 0, offset + len <= avcc.endIndex else { return nil }
            out.append(contentsOf: startCode)
            out.append(avcc[offset..<offset + len])
            offset += len
        }
        return out
    }

    public static func splitNALUnits(_ data: Data) -> [Data] {
        let bytes = [UInt8](data)
        var starts: [(codeStart: Int, nalStart: Int)] = []
        var i = 0
        while i + 3 <= bytes.count {
            if bytes[i] == 0, bytes[i + 1] == 0 {
                if bytes[i + 2] == 1 {
                    starts.append((i, i + 3)); i += 3; continue
                }
                if i + 4 <= bytes.count, bytes[i + 2] == 0, bytes[i + 3] == 1 {
                    starts.append((i, i + 4)); i += 4; continue
                }
            }
            i += 1
        }
        return starts.enumerated().compactMap { idx, s in
            let end = idx + 1 < starts.count ? starts[idx + 1].codeStart : bytes.count
            return end > s.nalStart ? Data(bytes[s.nalStart..<end]) : nil
        }
    }

    public static func nalType(_ nal: Data) -> H264NALType? {
        nal.first.flatMap { H264NALType(rawValue: $0 & 0x1F) }
    }
}
