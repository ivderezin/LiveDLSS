using System.Numerics;
using LiveDLSS.Core.Gpu;
using Vortice.Direct3D12;

namespace LiveDLSS.Features.Upscale;

public sealed class BilinearUpscaler(GpuContext gpu) : IUpscaler
{
    private readonly ComputeKernel _kernel = new(gpu, typeof(BilinearUpscaler).Assembly, "Bilinear.hlsl", "CSBilinear");

    public string Name => "Bilinear (fallback)";
    public bool IsDlss => false;

    public void Evaluate(GpuContext gpu, UpscaleResources resources, UpscaleParams parameters)
    {
        var cmd = gpu.Cmd;
        resources.Color.Transition(cmd, ResourceStates.NonPixelShaderResource);
        resources.Output.Transition(cmd, ResourceStates.UnorderedAccess);
        var constants = new Vector4(resources.Output.Width, resources.Output.Height, 0, 0);
        _kernel.Dispatch(cmd, constants, [resources.Color.Srv], [resources.Output.Uav], resources.Output.Width, resources.Output.Height);
        resources.Output.UavBarrier(cmd);
    }

    public void Dispose() => _kernel.Dispose();
}
