using System.Diagnostics;
using LiveDLSS.Core.Gpu;
using LiveDLSS.Core.Logging;
using LiveDLSS.Core.Net;
using LiveDLSS.Features.Discovery;
using LiveDLSS.Features.Egress;
using LiveDLSS.Features.Ingest;
using LiveDLSS.Features.Inspector;
using LiveDLSS.Features.NeuralRendering;
using LiveDLSS.Features.Upscale;
using LiveDLSS.Protocol;
using LiveDLSS.Server.Pipeline;
using LiveDLSS.Server.Platform;
using Vortice.Direct3D12;

namespace LiveDLSS.Server;

internal sealed class Application : IAsyncDisposable
{
    private readonly GpuContext _gpu;
    private readonly Win32Window _window;
    private readonly SwapChain _swapChain;
    private readonly ImGuiHost _imgui;
    private readonly IngestService _ingest = new();
    private readonly TcpServer _server;
    private readonly ServiceAdvertiser? _advertiser;
    private readonly VideoDecoder _decoder = new();
    private readonly IUpscaler _upscaler;
    private readonly NeuralRenderPass? _neuralRenderer;
    private readonly FramePipeline _pipeline;
    private readonly EgressWorker _egress;
    private readonly InspectorState _ui = new();
    private readonly Queue<long> _fpsWindow = new();
    private long _framesProcessed;

    public Application(ServerOptions options)
    {
        // Streamline must be initialized before the D3D12 device is created.
        UpscalerFactory.InitBeforeDevice(options.NvidiaAppId, out var slStatus);
        Log.Info(slStatus);

        _gpu = new GpuContext(options.DebugLayer);
        _window = new Win32Window("LiveDLSS Server", 1600, 1000);
        _swapChain = new SwapChain(_gpu, _window.Handle);
        _imgui = new ImGuiHost(_gpu, _window.Handle, SwapChain.BackBufferFormat, SwapChain.BufferCount);
        _upscaler = UpscalerFactory.Create(_gpu, out var upscalerStatus);

        _neuralRenderer = NeuralRenderPass.TryCreate(_gpu, options.NeuralRuntimeDirectory, options.NvidiaAppId, out var nrStatus);
        _ui.NeuralRenderingAvailable = _neuralRenderer is not null;
        _ui.NeuralRenderingEnabled = _neuralRenderer is not null;
        _ui.NeuralRenderingStatus = nrStatus;
        Log.Info(nrStatus);

        _ui.GpuName = _gpu.AdapterName;
        _ui.UpscalerName = _upscaler.Name;
        _ui.UpscalerStatus = upscalerStatus;

        _server = new TcpServer(options.Port);
        _egress = new EgressWorker(_server.SendVideo);
        _pipeline = new FramePipeline(_gpu, _upscaler, _neuralRenderer);

        _server.ConnectionChanged += _ingest.OnConnection;
        _server.MessageReceived += OnNetworkMessage;
        _server.Start();

        try
        {
            _advertiser = new ServiceAdvertiser($"LiveDLSS on {Environment.MachineName}", (ushort)options.Port);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Log.Warn($"mDNS unavailable, connect the iPhone by IP: {e.Message}");
        }

        _window.ModalTick = RunFrame;
    }

    public void Run()
    {
        while (_window.PumpMessages()) RunFrame();
    }

    private void RunFrame()
    {
        if (_window.ConsumeResize() is { } size) _swapChain.Resize(size.W, size.H);
        if (_ingest.ConsumeSessionReset()) ResetSession();

        _gpu.BeginFrame();

        // P-frames reference earlier ones, so every packet is decoded; only the latest is processed.
        FramePacket? packet = null;
        DecodedPicture? picture = null;
        var decodeMs = 0.0;
        foreach (var candidate in _ingest.DrainFrames())
        {
            var started = Stopwatch.GetTimestamp();
            if (_decoder.Decode(candidate.Camera.Data) is { } decoded)
            {
                packet = candidate;
                picture = decoded;
                decodeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
        }

        _ui.ClientSettings = _ingest.Settings;
        var settings = _ui.EffectiveSettings;
        if (picture is not null)
        {
            var neuralOptions = _ui.NeuralRenderingEnabled
                ? new NeuralOptions(_ui.NeuralIntensity, _ui.NeuralStyle, _ui.NeuralPreset)
                : null;
            _pipeline.Record(packet!, picture, settings, neuralOptions);
            if (_pipeline.RequiresKeyframe) _egress.RequestKeyframe();
            UpdatePipelineState();
        }

        RecordUi();
        _gpu.SubmitAndWait();
        _swapChain.Present();

        if (picture is not null) PostToEgress(packet!, decodeMs, settings);
        else Thread.Sleep(1);
    }

    private void OnNetworkMessage(Message message)
    {
        if (message is Hello) _server.BeginVideoSession();
        _ingest.OnMessage(message, _server.VideoSessionId);
        switch (message)
        {
            case Hello:
                _server.Send(new HelloAck(Environment.MachineName, _gpu.AdapterName, _upscaler.IsDlss, _ui.UpscalerStatus));
                break;
            case RequestKeyframe:
                _egress.RequestKeyframe();
                break;
        }
    }

    private void ResetSession()
    {
        _pipeline.ResetSession();
        _decoder.Reset();
        _egress.RequestKeyframe();
        _server.Send(new RequestKeyframe());

        var client = _ingest.Client;
        _ui.ClientConnected = client.Connected;
        _ui.ClientPeer = client.Peer;
        _ui.ClientName = client.Hello?.ClientName ?? "";
        _ui.ClientHasDepth = client.Hello?.HasDepth ?? false;
    }

    private void PostToEgress(FramePacket packet, double decodeMs, Settings settings)
    {
        var upscaleMs = _pipeline.UpscaleMs;
        var nv12 = EgressWorker.RentBuffer(_pipeline.Nv12Size);
        _pipeline.CopyNv12(nv12);
        _egress.Post(new EgressFrame(
            packet.SessionId, packet.Camera.FrameId, _pipeline.EncodedWidth, _pipeline.EncodedHeight, settings.DownlinkBitrateKbps,
            nv12, (uint)(decodeMs * 1000), (uint)(upscaleMs * 1000)));

        _framesProcessed++;
        var now = Stopwatch.GetTimestamp();
        _fpsWindow.Enqueue(now);
        while (_fpsWindow.Count > 0 && Stopwatch.GetElapsedTime(_fpsWindow.Peek(), now).TotalSeconds > 1) _fpsWindow.Dequeue();
        _ui.EncoderName = _egress.EncoderName;
        _ui.Stats = new PipelineStats(_fpsWindow.Count, decodeMs, _pipeline.MotionMs, upscaleMs, _egress.LastEncodeMs,
            _framesProcessed, _ingest.DroppedFrames, _pipeline.NeuralMs);
    }

    private void UpdatePipelineState()
    {
        _ui.RenderSize = _pipeline.RenderSize;
        _ui.OutputSize = _pipeline.OutputSize;
        if (_pipeline.NeuralError is { } error)
        {
            _ui.NeuralRenderingEnabled = false;
            _ui.NeuralRenderingStatus = error;
        }
        else if (_pipeline.NeuralApplied) _ui.NeuralRenderingStatus = _neuralRenderer!.Status;
        else if (_neuralRenderer is { HasFailed: false })
            _ui.NeuralRenderingStatus = _ui.NeuralRenderingEnabled ? Strings.NeuralPausedDebugView : Strings.NeuralOff;
    }

    private void RecordUi()
    {
        var cmd = _gpu.Cmd;
        _ui.Previews.Clear();
        foreach (var (kind, texture) in _pipeline.Previews())
        {
            var label = kind switch
            {
                PipelinePreview.Input => Strings.PreviewInput,
                PipelinePreview.Upscaled => Strings.PreviewUpscaled,
                PipelinePreview.NeuralOutput => Strings.PreviewNeuralOutput,
                PipelinePreview.Depth => Strings.PreviewDepth,
                PipelinePreview.MotionVectors => Strings.PreviewMotionVectors,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            texture.Transition(cmd, ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource);
            var handle = _gpu.CopyToTable([texture.Srv]);
            _ui.Previews.Add(new TexturePreview(label, handle.Ptr, texture.Width, texture.Height));
        }

        _imgui.NewFrame();
        InspectorView.Draw(_ui);
        _swapChain.Begin(cmd);
        _gpu.BindDescriptorHeap();
        _imgui.Render(cmd);
        _swapChain.End(cmd);
    }

    public async ValueTask DisposeAsync()
    {
        _window.ModalTick = null;
        await _server.DisposeAsync().ConfigureAwait(false);
        _advertiser?.Dispose();
        _egress.Dispose();
        _gpu.WaitIdle();
        _pipeline.Dispose();
        _neuralRenderer?.Dispose();
        _upscaler.Dispose();
        _decoder.Dispose();
        _imgui.Dispose();
        _swapChain.Dispose();
        _window.Dispose();
        _gpu.Dispose();
        UpscalerFactory.Shutdown();
    }
}
