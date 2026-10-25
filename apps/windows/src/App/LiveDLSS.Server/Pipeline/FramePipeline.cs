using LiveDLSS.Core.Gpu;
using LiveDLSS.Core.Logging;
using LiveDLSS.Core.Math;
using LiveDLSS.Features.Egress;
using LiveDLSS.Features.Ingest;
using LiveDLSS.Features.MotionVectors;
using LiveDLSS.Features.NeuralRendering;
using LiveDLSS.Features.Upscale;
using LiveDLSS.Protocol;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace LiveDLSS.Server.Pipeline;

internal sealed class FramePipeline : IDisposable
{
    private const int TsMotionBegin = 0, TsMotionEnd = 1, TsUpscaleEnd = 2, TsNeuralEnd = 3;
    private const int MaxOutputSide = 4096;
    private const int MaxSensorDepthReuseFrames = 8;

    private readonly GpuContext _gpu;
    private readonly IUpscaler _upscaler;
    private readonly NeuralRenderPass? _neuralRenderer;
    private readonly YuvToRgbPass _yuvToRgb;
    private readonly MotionVectorPass _motionPass;
    private readonly BilinearUpscaler _baselineUpscaler;
    private readonly RgbToNv12Pass _nv12;

    private Targets? _targets;
    private Texture2D? _sensorDepth;
    private int _framesSinceSensorDepth = int.MaxValue;
    private Matrix4x4? _previousPose;
    private bool _resetHistory = true;
    private bool? _previousDlssEnabled;

    public FramePipeline(
        GpuContext gpu, IUpscaler upscaler, NeuralRenderPass? neuralRenderer)
    {
        _gpu = gpu;
        _upscaler = upscaler;
        _neuralRenderer = neuralRenderer;
        _yuvToRgb = new YuvToRgbPass(gpu);
        _motionPass = new MotionVectorPass(gpu);
        _baselineUpscaler = new BilinearUpscaler(gpu);
        _nv12 = new RgbToNv12Pass(gpu);
    }

    public bool NeuralApplied { get; private set; }
    public string? NeuralError { get; private set; }
    public bool RequiresKeyframe { get; private set; }
    public (int W, int H) RenderSize { get; private set; }
    public (int W, int H) OutputSize { get; private set; }
    public int EncodedWidth => _nv12.Width;
    public int EncodedHeight => _nv12.Height;
    public int Nv12Size => _nv12.Nv12Size;
    public double MotionMs => _gpu.TimestampDeltaMs(TsMotionBegin, TsMotionEnd);
    public double UpscaleMs => _gpu.TimestampDeltaMs(TsMotionEnd, TsUpscaleEnd);
    public double NeuralMs => NeuralApplied ? _gpu.TimestampDeltaMs(TsUpscaleEnd, TsNeuralEnd) : 0;

    public void ResetSession()
    {
        _neuralRenderer?.ResetHistory();
        _previousPose = null;
        _framesSinceSensorDepth = int.MaxValue;
        _resetHistory = true;
    }

    public void CopyNv12(Span<byte> destination) => _nv12.CopyTo(destination);

    public IEnumerable<(PipelinePreview Kind, Texture2D Texture)> Previews()
    {
        if (_targets is not { } t) yield break;
        yield return (PipelinePreview.Input, t.Color);
        yield return (PipelinePreview.Upscaled, t.Output);
        if (NeuralApplied) yield return (PipelinePreview.NeuralOutput, t.NeuralOutput);
        yield return (PipelinePreview.Depth, t.DepthViz);
        yield return (PipelinePreview.MotionVectors, t.MotionViz);
    }

    public void Record(FramePacket packet, DecodedPicture picture, Settings settings, NeuralOptions? neuralOptions)
    {
        var cmd = _gpu.Cmd;
        var camera = packet.Camera;
        int renderW = picture.Width, renderH = picture.Height;
        // SR on/off must not change NR dimensions or recreate its temporal history.
        var factor = settings.QualityMode.UpscaleFactor();
        var outW = Math.Min(MaxOutputSide, (int)MathF.Round(renderW * factor / 2) * 2);
        var outH = Math.Min(MaxOutputSide, (int)MathF.Round(renderH * factor / 2) * 2);
        var targets = EnsureTargets(renderW, renderH, outW, outH);

        NeuralApplied = false;
        NeuralError = null;
        RequiresKeyframe = false;
        var useNeural = neuralOptions is not null && settings.DebugView == DebugView.Final && _neuralRenderer is not null;
        if (useNeural)
        {
            useNeural = _neuralRenderer!.Prepare(outW, outH, neuralOptions!);
            if (!useNeural)
            {
                NeuralError = _neuralRenderer.Status;
                Log.Warn(_neuralRenderer.Status);
            }
        }
        else _neuralRenderer?.ResetHistory();

        _yuvToRgb.Execute(cmd, picture, targets.Color);
        var sensorDepth = settings.DepthSource == DepthSource.Sensor ? UploadSensorDepth(cmd, packet.Depth) : null;

        var pose = CameraMath.FromArKitColumnMajor(camera.CameraToWorld);
        _gpu.Timestamp(TsMotionBegin);
        _motionPass.Execute(cmd,
            new MotionInputs(sensorDepth, camera.Intrinsics, pose, _previousPose, settings.ConstantDepthMeters),
            targets.Motion, targets.DepthNdc);
        _motionPass.Visualize(cmd, targets.Motion, targets.DepthNdc, targets.MotionViz, targets.DepthViz);
        _gpu.Timestamp(TsMotionEnd);

        if (_previousDlssEnabled != settings.Enabled)
        {
            _resetHistory = true;
            _neuralRenderer?.ResetHistory();
            RequiresKeyframe = true;
            _previousDlssEnabled = settings.Enabled;
        }
        IUpscaler scaler = settings.Enabled ? _upscaler : _baselineUpscaler;
        scaler.Evaluate(_gpu,
            new UpscaleResources(targets.Color, targets.DepthNdc, targets.Motion, targets.Output),
            new UpscaleParams(settings.QualityMode, settings.Preset, settings.Sharpness, _resetHistory,
                camera.Intrinsics, pose, _previousPose ?? pose));
        _resetHistory = !settings.Enabled;
        _gpu.Timestamp(TsUpscaleEnd);
        _previousPose = pose;

        var final = targets.Output;
        if (useNeural)
        {
            NeuralApplied = _neuralRenderer!.Evaluate(final, targets.DepthNdc, targets.Motion, targets.NeuralOutput);
            if (NeuralApplied) final = targets.NeuralOutput;
            else
            {
                NeuralError = _neuralRenderer.Status;
                Log.Warn(_neuralRenderer.Status);
            }
        }
        _gpu.Timestamp(TsNeuralEnd);

        var result = settings.DebugView switch
        {
            DebugView.Input => targets.Color,
            DebugView.Depth => targets.DepthViz,
            DebugView.MotionVectors => targets.MotionViz,
            _ => final,
        };
        _nv12.Execute(cmd, result);

        RenderSize = (renderW, renderH);
        OutputSize = (outW, outH);
    }

    private Targets EnsureTargets(int renderW, int renderH, int outW, int outH)
    {
        if (_targets is { } t && t.Color.Width == renderW && t.Color.Height == renderH &&
            t.Output.Width == outW && t.Output.Height == outH)
        {
            return t;
        }

        _gpu.WaitIdle();
        _targets?.Dispose();
        _neuralRenderer?.ResetHistory();
        _resetHistory = true;
        _targets = new Targets(
            Color: Target(renderW, renderH, Format.R8G8B8A8_UNorm, "color"),
            Motion: Target(renderW, renderH, Format.R16G16_Float, "motion"),
            DepthNdc: Target(renderW, renderH, Format.R32_Float, "depthNdc"),
            MotionViz: Target(renderW, renderH, Format.R8G8B8A8_UNorm, "motionViz"),
            DepthViz: Target(renderW, renderH, Format.R8G8B8A8_UNorm, "depthViz"),
            Output: Target(outW, outH, Format.R8G8B8A8_UNorm, "output"),
            NeuralOutput: Target(outW, outH, Format.R8G8B8A8_UNorm, "nr.final"));
        Log.Info($"Targets: render {renderW}x{renderH} → output {outW}x{outH}");
        return _targets;
    }

    private Texture2D Target(int width, int height, Format format, string name) =>
        new(_gpu, new TextureDesc(width, height, format, Uav: true, Name: name));

    private Texture2D? UploadSensorDepth(ID3D12GraphicsCommandList cmd, DepthFrame? frame)
    {
        if (frame is not null && DepthDecoder.Decode(frame) is { } depth)
        {
            var texture = EnsureSensorDepthTexture(depth.Width, depth.Height);
            texture.Upload(cmd, depth.MetersF16, depth.RowPitch);
            _framesSinceSensorDepth = 0;
            return texture;
        }
        // TrueDepth arrives at a lower rate than video; reuse the last map for a few frames.
        if (_sensorDepth is null || _framesSinceSensorDepth >= MaxSensorDepthReuseFrames) return null;
        _framesSinceSensorDepth++;
        return _sensorDepth;
    }

    private Texture2D EnsureSensorDepthTexture(int width, int height)
    {
        if (_sensorDepth is { } d && d.Width == width && d.Height == height) return d;
        _gpu.WaitIdle();
        _sensorDepth?.Dispose();
        _sensorDepth = new Texture2D(_gpu, new TextureDesc(width, height, Format.R16_Float, CpuUpload: true, Name: "sensorDepth"));
        return _sensorDepth;
    }

    public void Dispose()
    {
        _targets?.Dispose();
        _sensorDepth?.Dispose();
        _nv12.Dispose();
        _baselineUpscaler.Dispose();
        _motionPass.Dispose();
        _yuvToRgb.Dispose();
    }

    private sealed record Targets(
        Texture2D Color,
        Texture2D Motion,
        Texture2D DepthNdc,
        Texture2D MotionViz,
        Texture2D DepthViz,
        Texture2D Output,
        Texture2D NeuralOutput)
        : IDisposable
    {
        public void Dispose()
        {
            Color.Dispose();
            Motion.Dispose();
            DepthNdc.Dispose();
            MotionViz.Dispose();
            DepthViz.Dispose();
            Output.Dispose();
            NeuralOutput.Dispose();
        }
    }
}

internal enum PipelinePreview
{
    Input,
    Upscaled,
    NeuralOutput,
    Depth,
    MotionVectors,
}
