using System.Globalization;
using System.Numerics;
using LiveDLSS.Core.Gpu;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace LiveDLSS.Features.NeuralRendering;

public sealed record NeuralOptions(float Intensity = 1f, int Style = 0, int Preset = 0);

public sealed class NeuralRenderPass : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly ComputeKernel _prepare;
    private readonly ComputeKernel _resolve;
    private Texture2D? _proxy, _modelOutput;
    private NeuralOptions? _builtOptions;
    private bool _reset = true;
    private bool _disposed;

    public string Status { get; private set; } = Strings.LibraryLoaded;
    public bool IsActive { get; private set; }
    public bool HasFailed { get; private set; }

    private NeuralRenderPass(GpuContext gpu)
    {
        _gpu = gpu;
        var assembly = typeof(NeuralRenderPass).Assembly;
        _prepare = new ComputeKernel(gpu, assembly, "NeuralRendering.hlsl", "CSPrepare");
        try { _resolve = new ComputeKernel(gpu, assembly, "NeuralRendering.hlsl", "CSResolve"); }
        catch { _prepare.Dispose(); throw; }
    }

    public static NeuralRenderPass? TryCreate(GpuContext gpu, string runtimeDirectory, uint appId, out string status)
    {
        status = Strings.NvidiaRequired;
        if (!gpu.IsNvidia) return null;
        var model = Path.Combine(runtimeDirectory, "nvngx_dlssnr.dll");
        if (!File.Exists(model))
        {
            status = string.Format(CultureInfo.CurrentCulture, Strings.LibraryNotFound, model);
            return null;
        }
        var initialized = false;
        try
        {
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveDLSS", "NGX");
            Directory.CreateDirectory(cache);
            initialized = true;
            if (NrNative.Init(gpu.Device.NativePointer, Path.GetFullPath(runtimeDirectory), cache, appId) != 0)
            {
                status = NrNative.Error;
                NrNative.Shutdown();
                return null;
            }
            var pass = new NeuralRenderPass(gpu);
            status = pass.Status;
            return pass;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
            IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            status = string.Format(CultureInfo.CurrentCulture, Strings.Error, e.Message);
            if (initialized)
            {
                try { NrNative.Shutdown(); }
                catch (Exception cleanup) when (cleanup is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
            }
            return null;
        }
    }

    public bool Prepare(int width, int height, NeuralOptions options)
    {
        if (_proxy is { } proxy && proxy.Width == width && proxy.Height == height && _builtOptions == options)
            return true;
        _gpu.WaitIdle();
        NrNative.Free();
        _builtOptions = null;
        IsActive = false;
        var native = new NrNative.Options
        {
            Intensity = options.Intensity,
            Style = options.Style,
            Preset = options.Preset,
            LocalStructure = 1f,
            LocalTone = 1f,
            SkinStructure = 1f,
            AutoMask = 1,
        };
        var result = NrNative.Create(_gpu.Cmd.NativePointer, (uint)width, (uint)height, native);
        _gpu.SubmitAndWait();
        _gpu.BeginFrame();
        if (result != 0)
        {
            HasFailed = true;
            Status = NrNative.Error;
            return false;
        }
        _proxy?.Dispose();
        _modelOutput?.Dispose();
        _proxy = new Texture2D(_gpu, new TextureDesc(width, height, Format.R16G16B16A16_Float, Uav: true, Name: "nr.color"));
        _modelOutput = new Texture2D(_gpu, new TextureDesc(width, height, Format.R16G16B16A16_Float, Uav: true, Name: "nr.modelOutput"));
        _builtOptions = options;
        HasFailed = false;
        _reset = true;
        Status = Strings.ModelCreated;
        return true;
    }

    public void ResetHistory() => _reset = true;

    public bool Evaluate(Texture2D color, Texture2D depth, Texture2D motion, Texture2D output)
    {
        if (_proxy is null || _modelOutput is null || _builtOptions is null) return false;
        var cmd = _gpu.Cmd;
        var size = new Vector4(output.Width, output.Height, 0, 0);
        color.Transition(cmd, ResourceStates.NonPixelShaderResource);
        _proxy.Transition(cmd, ResourceStates.UnorderedAccess);
        _prepare.Dispatch(cmd, size, [color.Srv], [_proxy.Uav], output.Width, output.Height);
        _proxy.UavBarrier(cmd);
        depth.Transition(cmd, ResourceStates.UnorderedAccess);
        motion.Transition(cmd, ResourceStates.UnorderedAccess);
        _modelOutput.Transition(cmd, ResourceStates.UnorderedAccess);
        var resources = new NrNative.Resources
        {
            Color = _proxy.Resource.NativePointer,
            Depth = depth.Resource.NativePointer,
            Motion = motion.Resource.NativePointer,
            Output = _modelOutput.Resource.NativePointer,
            Width = (uint)output.Width,
            Height = (uint)output.Height,
            GuideWidth = (uint)depth.Width,
            GuideHeight = (uint)depth.Height,
            MvScaleX = 1f,
            MvScaleY = 1f,
            Reset = _reset ? 1 : 0,
            DepthInverted = 1,
        };
        var result = NrNative.Evaluate(cmd.NativePointer, resources);
        _gpu.BindDescriptorHeap();
        if (result != 0)
        {
            IsActive = false;
            HasFailed = true;
            Status = NrNative.Error;
            return false;
        }
        _modelOutput.UavBarrier(cmd);
        _modelOutput.Transition(cmd, ResourceStates.NonPixelShaderResource);
        color.Transition(cmd, ResourceStates.NonPixelShaderResource);
        output.Transition(cmd, ResourceStates.UnorderedAccess);
        _resolve.Dispatch(cmd, size, [color.Srv, _modelOutput.Srv], [output.Uav], output.Width, output.Height);
        output.UavBarrier(cmd);
        _reset = false;
        IsActive = true;
        HasFailed = false;
        Status = Strings.Active;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gpu.WaitIdle();
        NrNative.Shutdown();
        _proxy?.Dispose();
        _modelOutput?.Dispose();
        _prepare.Dispose();
        _resolve.Dispose();
    }
}
