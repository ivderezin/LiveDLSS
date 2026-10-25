using System.Buffers.Binary;
using System.Text;

namespace LiveDLSS.Protocol;

public sealed class ProtocolException(string message) : Exception(message);

public readonly record struct MessageHeader(ushort Type, uint PayloadSize)
{
    public const int Size = 16;
    public const ushort Version = 1;
    public const uint MaxPayloadSize = 16 * 1024 * 1024;
    public const ushort DefaultPort = 47800;
    public static ReadOnlySpan<byte> Magic => "LDLS"u8;

    public static MessageHeader Decode(ReadOnlySpan<byte> bytes)
    {
        var r = new ByteReader(bytes);
        if (!r.Take(4).SequenceEqual(Magic)) throw new ProtocolException("bad magic");
        var version = r.U16();
        if (version != Version) throw new ProtocolException($"unsupported protocol version {version}");
        var header = new MessageHeader(r.U16(), r.U32());
        if (header.PayloadSize > MaxPayloadSize) throw new ProtocolException("payload too large");
        return header;
    }
}

public static class MessageCodec
{
    public static byte[] Encode(Message message)
    {
        var p = new ByteWriter();
        var type = message switch
        {
            Hello m => EncodeHello(p, m),
            HelloAck m => EncodeHelloAck(p, m),
            CameraFrame m => EncodeCameraFrame(p, m),
            DepthFrame m => EncodeDepthFrame(p, m),
            Settings m => EncodeSettings(p, m),
            RequestKeyframe => MessageType.RequestKeyframe,
            ProcessedFrame m => EncodeProcessedFrame(p, m),
            Bye => MessageType.Bye,
            UnknownMessage u => (MessageType)u.Type,
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        };

        var w = new ByteWriter(MessageHeader.Size + p.Length);
        w.Bytes(MessageHeader.Magic);
        w.U16(MessageHeader.Version);
        w.U16((ushort)type);
        w.U32((uint)p.Length);
        w.U32(0);
        w.Bytes(p.WrittenSpan);
        return w.ToArray();
    }

    public static Message Decode(MessageHeader header, ReadOnlySpan<byte> payload)
    {
        var r = new ByteReader(payload);
        switch ((MessageType)header.Type)
        {
            case MessageType.Hello:
                return new Hello(r.Str(), r.U8() != 0, (VideoCodec)r.U8(), r.U16(), r.U16());

            case MessageType.HelloAck:
                return new HelloAck(r.Str(), r.Str(), r.U8() != 0, r.Str());

            case MessageType.CameraFrame:
                {
                    var frameId = r.U64();
                    var ts = r.U64();
                    var codec = (VideoCodec)r.U8();
                    var flags = r.U8();
                    var w = r.U16();
                    var h = r.U16();
                    var k = new CameraIntrinsics(r.F32(), r.F32(), r.F32(), r.F32());
                    var m = new float[16];
                    for (var i = 0; i < 16; i++) m[i] = r.F32();
                    return new CameraFrame(frameId, ts, codec, (flags & 1) != 0, (flags & 2) != 0, w, h, k, m, r.Blob());
                }

            case MessageType.DepthFrame:
                return new DepthFrame(r.U64(), r.U16(), r.U16(), r.U8(), (DepthCompression)r.U8(), r.U32(), r.Blob());

            case MessageType.Settings:
                {
                    var enabled = r.U8() != 0;
                    var mode = (QualityMode)r.U8();
                    var preset = r.U8();
                    var view = (DebugView)r.U8();
                    var depth = (DepthSource)r.U8();
                    r.Skip(3);
                    return new Settings(enabled, mode, preset, view, depth, r.F32(), r.F32(), r.U32());
                }

            case MessageType.RequestKeyframe:
                return new RequestKeyframe();

            case MessageType.ProcessedFrame:
                return new ProcessedFrame(
                    r.U64(), (VideoCodec)r.U8(), (r.U8() & 1) != 0, r.U16(), r.U16(), r.U32(), r.U32(), r.U32(), r.Blob());

            case MessageType.Bye:
                return new Bye();

            default:
                return new UnknownMessage(header.Type);
        }
    }

    private static MessageType EncodeHello(ByteWriter w, Hello m)
    {
        w.Str(m.ClientName);
        w.U8(m.HasDepth ? (byte)1 : (byte)0);
        w.U8((byte)m.VideoCodec);
        w.U16(m.CaptureWidth);
        w.U16(m.CaptureHeight);
        return MessageType.Hello;
    }

    private static MessageType EncodeHelloAck(ByteWriter w, HelloAck m)
    {
        w.Str(m.ServerName);
        w.Str(m.GpuName);
        w.U8(m.DlssAvailable ? (byte)1 : (byte)0);
        w.Str(m.StatusMessage);
        return MessageType.HelloAck;
    }

    private static MessageType EncodeCameraFrame(ByteWriter w, CameraFrame m)
    {
        if (m.CameraToWorld.Length != 16) throw new ArgumentException("CameraToWorld must have 16 elements");
        w.U64(m.FrameId);
        w.U64(m.TimestampNs);
        w.U8((byte)m.Codec);
        w.U8((byte)((m.IsKeyframe ? 1 : 0) | (m.HasDepth ? 2 : 0)));
        w.U16(m.Width);
        w.U16(m.Height);
        w.F32(m.Intrinsics.Fx);
        w.F32(m.Intrinsics.Fy);
        w.F32(m.Intrinsics.Cx);
        w.F32(m.Intrinsics.Cy);
        foreach (var v in m.CameraToWorld) w.F32(v);
        w.Blob(m.Data);
        return MessageType.CameraFrame;
    }

    private static MessageType EncodeDepthFrame(ByteWriter w, DepthFrame m)
    {
        w.U64(m.FrameId);
        w.U16(m.Width);
        w.U16(m.Height);
        w.U8(m.Format);
        w.U8((byte)m.Compression);
        w.U32(m.RawSize);
        w.Blob(m.Data);
        return MessageType.DepthFrame;
    }

    private static MessageType EncodeSettings(ByteWriter w, Settings m)
    {
        w.U8(m.Enabled ? (byte)1 : (byte)0);
        w.U8((byte)m.QualityMode);
        w.U8(m.Preset);
        w.U8((byte)m.DebugView);
        w.U8((byte)m.DepthSource);
        w.Zeros(3);
        w.F32(m.Sharpness);
        w.F32(m.ConstantDepthMeters);
        w.U32(m.DownlinkBitrateKbps);
        return MessageType.Settings;
    }

    private static MessageType EncodeProcessedFrame(ByteWriter w, ProcessedFrame m)
    {
        w.U64(m.FrameId);
        w.U8((byte)m.Codec);
        w.U8(m.IsKeyframe ? (byte)1 : (byte)0);
        w.U16(m.Width);
        w.U16(m.Height);
        w.U32(m.DecodeUs);
        w.U32(m.UpscaleUs);
        w.U32(m.EncodeUs);
        w.Blob(m.Data);
        return MessageType.ProcessedFrame;
    }
}

internal sealed class ByteWriter(int capacity = 256)
{
    private byte[] _buffer = new byte[capacity];

    public int Length { get; private set; }
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, Length);
    public byte[] ToArray() => WrittenSpan.ToArray();

    public void U8(byte v) => Grab(1)[0] = v;
    public void U16(ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(Grab(2), v);
    public void U32(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Grab(4), v);
    public void U64(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Grab(8), v);
    public void F32(float v) => BinaryPrimitives.WriteSingleLittleEndian(Grab(4), v);
    public void Zeros(int n) => Grab(n).Clear();
    public void Bytes(ReadOnlySpan<byte> b) => b.CopyTo(Grab(b.Length));

    public void Str(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var n = Math.Min(bytes.Length, ushort.MaxValue);
        U16((ushort)n);
        Bytes(bytes.AsSpan(0, n));
    }

    public void Blob(byte[] b)
    {
        U32((uint)b.Length);
        Bytes(b);
    }

    private Span<byte> Grab(int n)
    {
        if (Length + n > _buffer.Length) Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + n));
        var span = _buffer.AsSpan(Length, n);
        Length += n;
        return span;
    }
}

internal ref struct ByteReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _offset;

    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public void Skip(int n) => Take(n);
    public string Str() => Encoding.UTF8.GetString(Take(U16()));
    public byte[] Blob()
    {
        var length = U32();
        if (length > (uint)(_data.Length - _offset)) throw new ProtocolException("truncated blob");
        return Take((int)length).ToArray();
    }

    public ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || n > _data.Length - _offset) throw new ProtocolException("truncated message");
        var s = _data.Slice(_offset, n);
        _offset += n;
        return s;
    }
}
