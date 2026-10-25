using System.Globalization;
using LiveDLSS.Core.Gpu;
using LiveDLSS.Core.Logging;
using LiveDLSS.Features.Upscale.Dlss;

namespace LiveDLSS.Features.Upscale;

public static class UpscalerFactory
{
    private static bool _streamlineInitialized;

    public static bool InitBeforeDevice(uint nvidiaAppId, out string status)
    {
        try
        {
            var result = DlssNative.Init(nvidiaAppId);
            _streamlineInitialized = result == DlssNative.Ok;
            status = _streamlineInitialized
                ? Strings.StreamlineInitialized
                : string.Format(CultureInfo.CurrentCulture, Strings.StreamlineInitFailed, result);
        }
        catch (DllNotFoundException)
        {
            status = Strings.BridgeMissing;
        }
        return _streamlineInitialized;
    }

    public static IUpscaler Create(GpuContext gpu, out string status)
    {
        status = Strings.StreamlineNotInitialized;
        if (_streamlineInitialized && gpu.IsNvidia)
        {
            if (DlssUpscaler.TryCreate(gpu, out status) is { } dlss) return dlss;
        }
        else if (_streamlineInitialized)
        {
            status = string.Format(CultureInfo.CurrentCulture, Strings.NotNvidiaGpu, gpu.AdapterName);
        }

        Log.Warn($"Bilinear fallback: {status}");
        return new BilinearUpscaler(gpu);
    }

    public static void Shutdown()
    {
        if (_streamlineInitialized) DlssNative.Shutdown();
        _streamlineInitialized = false;
    }
}
