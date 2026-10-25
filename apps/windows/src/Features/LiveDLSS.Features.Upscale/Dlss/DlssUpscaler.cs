using System.Globalization;
using System.Numerics;
using LiveDLSS.Core.Gpu;
using LiveDLSS.Core.Logging;
using LiveDLSS.Core.Math;
using Vortice.Direct3D12;

namespace LiveDLSS.Features.Upscale.Dlss;

public sealed class DlssUpscaler : IUpscaler
{
    private const ResourceStates SrvState = ResourceStates.NonPixelShaderResource;
    private uint _frameIndex;

    private DlssUpscaler() { }

    public string Name => "NVIDIA DLSS (Streamline)";
    public bool IsDlss => true;

    public static DlssUpscaler? TryCreate(GpuContext gpu, out string reason)
    {
        try
        {
            var luid = gpu.AdapterLuid;
            var luidValue = (ulong)luid.HighPart << 32 | luid.LowPart;
            var result = DlssNative.SetDevice(gpu.Device.NativePointer, luidValue);
            if (result != DlssNative.Ok)
            {
                reason = string.Format(CultureInfo.CurrentCulture, Strings.DlssUnsupported, result);
                return null;
            }
            reason = Strings.DlssReady;
            return new DlssUpscaler();
        }
        catch (DllNotFoundException e)
        {
            reason = string.Format(CultureInfo.CurrentCulture, Strings.BridgeLoadFailed, e.Message);
            return null;
        }
    }

    public unsafe void Evaluate(GpuContext gpu, UpscaleResources resources, UpscaleParams parameters)
    {
        var cmd = gpu.Cmd;
        resources.Color.Transition(cmd, SrvState);
        resources.Depth.Transition(cmd, SrvState);
        resources.Motion.Transition(cmd, SrvState);
        resources.Output.Transition(cmd, ResourceStates.UnorderedAccess);

        var native = new DlssNative.Resources
        {
            Color = resources.Color.Resource.NativePointer,
            Depth = resources.Depth.Resource.NativePointer,
            Motion = resources.Motion.Resource.NativePointer,
            Output = resources.Output.Resource.NativePointer,
            ColorState = (uint)SrvState,
            DepthState = (uint)SrvState,
            MotionState = (uint)SrvState,
            OutputState = (uint)ResourceStates.UnorderedAccess,
            RenderWidth = (uint)resources.Color.Width,
            RenderHeight = (uint)resources.Color.Height,
            OutputWidth = (uint)resources.Output.Width,
            OutputHeight = (uint)resources.Output.Height,
        };

        var options = new DlssNative.Options
        {
            Mode = (int)parameters.Mode,
            Preset = parameters.Preset,
            Sharpness = parameters.Sharpness,
            Reset = parameters.Reset ? 1 : 0,
        };

        float w = resources.Color.Width, h = resources.Color.Height;
        var proj = CameraMath.ProjectionFromIntrinsics(parameters.Intrinsics, w, h);
        var viewProj = CameraMath.Inverse(parameters.CameraToWorld) * proj;
        var prevViewProj = CameraMath.Inverse(parameters.PreviousCameraToWorld) * proj;
        var clipToPrevClip = CameraMath.Inverse(viewProj) * prevViewProj;

        var c = new DlssNative.Constants
        {
            CameraNear = CameraMath.NearPlane,
            CameraFar = 1000f,
            CameraFov = CameraMath.VerticalFov(parameters.Intrinsics, h),
            CameraAspect = w / h,
            MvecScaleX = 1f / w,
            MvecScaleY = 1f / h,
        };
        Write(c.CameraViewToClip, proj);
        Write(c.ClipToCameraView, CameraMath.Inverse(proj));
        Write(c.ClipToPrevClip, clipToPrevClip);
        Write(c.PrevClipToClip, CameraMath.Inverse(clipToPrevClip));
        Write(c.CameraPos, CameraMath.Position(parameters.CameraToWorld));
        Write(c.CameraUp, CameraMath.Up(parameters.CameraToWorld));
        Write(c.CameraRight, CameraMath.Right(parameters.CameraToWorld));
        Write(c.CameraFwd, CameraMath.Forward(parameters.CameraToWorld));

        var result = DlssNative.Evaluate(cmd.NativePointer, native, options, c, ++_frameIndex);
        if (result != DlssNative.Ok) Log.Warn($"DLSS evaluate failed: {result}");

        gpu.BindDescriptorHeap();
    }

    private static unsafe void Write(float* dst, Matrix4x4 m) =>
        new ReadOnlySpan<float>(&m.M11, 16).CopyTo(new Span<float>(dst, 16));

    private static unsafe void Write(float* dst, Vector3 v)
    {
        dst[0] = v.X;
        dst[1] = v.Y;
        dst[2] = v.Z;
    }

    public void Dispose() => DlssNative.FreeResources();
}
