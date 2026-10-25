import CoreMedia
import Foundation

public final class H264SampleBufferBuilder {
    private var sps: Data?
    private var pps: Data?
    private var format: CMVideoFormatDescription?

    public init() {}

    public var hasFormat: Bool { format != nil }

    public func makeSampleBuffer(annexB: Data) -> CMSampleBuffer? {
        var slices: [Data] = []
        for nal in AnnexB.splitNALUnits(annexB) {
            switch AnnexB.nalType(nal) {
            case .sps:
                if nal != sps { sps = nal; format = nil }
            case .pps:
                if nal != pps { pps = nal; format = nil }
            case .aud:
                continue
            default:
                slices.append(nal)
            }
        }
        if format == nil { format = makeFormat() }
        guard let format, !slices.isEmpty else { return nil }

        var avcc = Data()
        for nal in slices {
            var len = UInt32(nal.count).bigEndian
            withUnsafeBytes(of: &len) { avcc.append(contentsOf: $0) }
            avcc.append(nal)
        }
        return makeSample(avcc: avcc, format: format)
    }

    private func makeFormat() -> CMVideoFormatDescription? {
        guard let sps, let pps else { return nil }
        var format: CMVideoFormatDescription?
        let status = sps.withUnsafeBytes { spsPtr in
            pps.withUnsafeBytes { ppsPtr in
                let pointers = [
                    spsPtr.bindMemory(to: UInt8.self).baseAddress!,
                    ppsPtr.bindMemory(to: UInt8.self).baseAddress!,
                ]
                let sizes = [sps.count, pps.count]
                return CMVideoFormatDescriptionCreateFromH264ParameterSets(
                    allocator: nil, parameterSetCount: 2, parameterSetPointers: pointers,
                    parameterSetSizes: sizes, nalUnitHeaderLength: 4, formatDescriptionOut: &format
                )
            }
        }
        return status == noErr ? format : nil
    }

    private func makeSample(avcc: Data, format: CMVideoFormatDescription) -> CMSampleBuffer? {
        var block: CMBlockBuffer?
        guard
            CMBlockBufferCreateWithMemoryBlock(
                allocator: nil, memoryBlock: nil, blockLength: avcc.count, blockAllocator: nil,
                customBlockSource: nil, offsetToData: 0, dataLength: avcc.count, flags: 0, blockBufferOut: &block
            ) == kCMBlockBufferNoErr, let block
        else { return nil }

        let copied = avcc.withUnsafeBytes {
            CMBlockBufferReplaceDataBytes(
                with: $0.baseAddress!, blockBuffer: block, offsetIntoDestination: 0, dataLength: avcc.count)
        }
        guard copied == kCMBlockBufferNoErr else { return nil }

        var sample: CMSampleBuffer?
        var size = avcc.count
        guard
            CMSampleBufferCreateReady(
                allocator: nil, dataBuffer: block, formatDescription: format, sampleCount: 1,
                sampleTimingEntryCount: 0, sampleTimingArray: nil, sampleSizeEntryCount: 1,
                sampleSizeArray: &size, sampleBufferOut: &sample
            ) == noErr, let sample
        else { return nil }

        if let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: true),
            CFArrayGetCount(attachments) > 0
        {
            let dict = unsafeBitCast(CFArrayGetValueAtIndex(attachments, 0), to: CFMutableDictionary.self)
            CFDictionarySetValue(
                dict,
                Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(),
                Unmanaged.passUnretained(kCFBooleanTrue).toOpaque()
            )
        }
        return sample
    }
}
