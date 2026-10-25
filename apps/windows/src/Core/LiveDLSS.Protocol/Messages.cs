namespace LiveDLSS.Protocol;

public enum MessageType : ushort
{
    Hello = 0x0001,
    HelloAck = 0x0002,
    CameraFrame = 0x0010,
    DepthFrame = 0x0011,
    Settings = 0x0020,
    RequestKeyframe = 0x0021,
    ProcessedFrame = 0x0030,
    Bye = 0x00FF,
}

public enum VideoCodec : byte { H264 = 1, Hevc = 2 }

public enum QualityMode : byte { Dlaa = 0, Quality, Balanced, Performance, UltraPerformance }

public enum DebugView : byte { Final = 0, Input, Depth, MotionVectors }

public enum DepthSource : byte { Sensor = 0, ConstantPlane }

public enum DepthCompression : byte { None = 0, Lz4 = 1 }

public static class QualityModeExtensions
{
    public static float UpscaleFactor(this QualityMode mode) => mode switch
    {
        QualityMode.Dlaa => 1.0f,
        QualityMode.Quality => 1.5f,
        QualityMode.Balanced => 1.72f,
        QualityMode.Performance => 2.0f,
        QualityMode.UltraPerformance => 3.0f,
        _ => 1.0f,
    };
}

public readonly record struct CameraIntrinsics(float Fx, float Fy, float Cx, float Cy);

public abstract record Message;

public sealed record Hello(string ClientName, bool HasDepth, VideoCodec VideoCodec, ushort CaptureWidth, ushort CaptureHeight)
    : Message;

public sealed record HelloAck(string ServerName, string GpuName, bool DlssAvailable, string StatusMessage) : Message;

public sealed record CameraFrame(
    ulong FrameId,
    ulong TimestampNs,
    VideoCodec Codec,
    bool IsKeyframe,
    bool HasDepth,
    ushort Width,
    ushort Height,
    CameraIntrinsics Intrinsics,
    float[] CameraToWorld,
    byte[] Data)
    : Message
{
    public bool Equals(CameraFrame? other) =>
        other is not null && FrameId == other.FrameId && TimestampNs == other.TimestampNs && Codec == other.Codec &&
        IsKeyframe == other.IsKeyframe && HasDepth == other.HasDepth && Width == other.Width &&
        Height == other.Height && Intrinsics == other.Intrinsics &&
        CameraToWorld.AsSpan().SequenceEqual(other.CameraToWorld) && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode() => HashCode.Combine(FrameId, TimestampNs, Width, Height);
}

public sealed record DepthFrame(
    ulong FrameId,
    ushort Width,
    ushort Height,
    byte Format,
    DepthCompression Compression,
    uint RawSize,
    byte[] Data)
    : Message
{
    public bool Equals(DepthFrame? other) =>
        other is not null && FrameId == other.FrameId && Width == other.Width && Height == other.Height &&
        Format == other.Format && Compression == other.Compression && RawSize == other.RawSize &&
        Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode() => HashCode.Combine(FrameId, Width, Height);
}

public sealed record Settings(
    bool Enabled = true,
    QualityMode QualityMode = QualityMode.Performance,
    byte Preset = 0,
    DebugView DebugView = DebugView.Final,
    DepthSource DepthSource = DepthSource.Sensor,
    float Sharpness = 0f,
    float ConstantDepthMeters = 2f,
    uint DownlinkBitrateKbps = 25_000)
    : Message;

public sealed record RequestKeyframe : Message;

public sealed record ProcessedFrame(
    ulong FrameId,
    VideoCodec Codec,
    bool IsKeyframe,
    ushort Width,
    ushort Height,
    uint DecodeUs,
    uint UpscaleUs,
    uint EncodeUs,
    byte[] Data)
    : Message
{
    public bool Equals(ProcessedFrame? other) =>
        other is not null && FrameId == other.FrameId && Codec == other.Codec && IsKeyframe == other.IsKeyframe &&
        Width == other.Width && Height == other.Height && DecodeUs == other.DecodeUs &&
        UpscaleUs == other.UpscaleUs && EncodeUs == other.EncodeUs && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode() => HashCode.Combine(FrameId, Width, Height);
}

public sealed record Bye : Message;

public sealed record UnknownMessage(ushort Type) : Message;
