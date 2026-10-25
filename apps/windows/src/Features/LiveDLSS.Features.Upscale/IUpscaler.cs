using System.Numerics;
using LiveDLSS.Core.Gpu;
using LiveDLSS.Protocol;

namespace LiveDLSS.Features.Upscale;

public sealed record UpscaleParams(
    QualityMode Mode,
    byte Preset,
    float Sharpness,
    bool Reset,
    CameraIntrinsics Intrinsics,
    Matrix4x4 CameraToWorld,
    Matrix4x4 PreviousCameraToWorld);

public sealed record UpscaleResources(
    Texture2D Color,
    Texture2D Depth,
    Texture2D Motion,
    Texture2D Output);

public interface IUpscaler : IDisposable
{
    string Name { get; }
    bool IsDlss { get; }
    void Evaluate(GpuContext gpu, UpscaleResources resources, UpscaleParams parameters);
}
