using LiveDLSS.Protocol;

namespace LiveDLSS.Features.Inspector;

public sealed record TexturePreview(string Label, ulong GpuHandle, int Width, int Height);

public sealed record PipelineStats(
    double Fps = 0,
    double DecodeMs = 0,
    double MotionMs = 0,
    double UpscaleMs = 0,
    double EncodeMs = 0,
    long FramesProcessed = 0,
    long DroppedFrames = 0,
    double NeuralMs = 0);

public sealed class InspectorState
{
    public string GpuName { get; set; } = "";
    public string UpscalerName { get; set; } = "";
    public string UpscalerStatus { get; set; } = "";
    public string EncoderName { get; set; } = "";
    public bool ClientConnected { get; set; }
    public string ClientPeer { get; set; } = "";
    public string ClientName { get; set; } = "";
    public bool ClientHasDepth { get; set; }
    public (int W, int H) RenderSize { get; set; }
    public (int W, int H) OutputSize { get; set; }
    public PipelineStats Stats { get; set; } = new();
    public List<TexturePreview> Previews { get; } = [];
    public Settings ClientSettings { get; set; } = new();

    public bool NeuralRenderingAvailable { get; set; }
    public string NeuralRenderingStatus { get; set; } = Strings.NeuralNotLoaded;
    public bool NeuralRenderingEnabled { get; set; }
    public float NeuralIntensity { get; set; } = 1f;
    public int NeuralStyle { get; set; }
    public int NeuralPreset { get; set; }

    public bool OverrideClientSettings { get; set; }
    public Settings LocalSettings { get; set; } = new();

    public Settings EffectiveSettings => OverrideClientSettings ? LocalSettings : ClientSettings;
}
