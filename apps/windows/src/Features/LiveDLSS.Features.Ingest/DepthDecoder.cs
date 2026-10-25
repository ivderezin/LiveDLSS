using K4os.Compression.LZ4;
using LiveDLSS.Core.Logging;
using LiveDLSS.Protocol;

namespace LiveDLSS.Features.Ingest;

public sealed record DepthImage(int Width, int Height, byte[] MetersF16)
{
    public int RowPitch => Width * sizeof(ushort);
}

public static class DepthDecoder
{
    public static DepthImage? Decode(DepthFrame frame)
    {
        var size = (long)frame.Width * frame.Height * sizeof(ushort);
        if (frame.Width == 0 || frame.Height == 0 || size > MessageHeader.MaxPayloadSize) return null;
        var expected = (int)size;
        if (frame.Format != 1 || frame.RawSize != expected)
        {
            Log.Warn($"Unexpected depth frame: format={frame.Format} raw={frame.RawSize} expected={expected}");
            return null;
        }

        switch (frame.Compression)
        {
            case DepthCompression.None when frame.Data.Length == expected:
                return new DepthImage(frame.Width, frame.Height, frame.Data);

            case DepthCompression.Lz4:
                var raw = new byte[expected];
                var decoded = LZ4Codec.Decode(frame.Data, raw);
                if (decoded != expected)
                {
                    Log.Warn($"LZ4 depth decode failed: {decoded}");
                    return null;
                }
                return new DepthImage(frame.Width, frame.Height, raw);

            default:
                return null;
        }
    }
}
