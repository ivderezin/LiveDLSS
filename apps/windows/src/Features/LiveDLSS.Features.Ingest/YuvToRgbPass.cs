using System.Runtime.InteropServices;
using LiveDLSS.Core.Gpu;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace LiveDLSS.Features.Ingest;

public sealed class YuvToRgbPass(GpuContext gpu) : IDisposable
{
    private readonly ComputeKernel _kernel = new(gpu, typeof(YuvToRgbPass).Assembly, "YuvToRgb.hlsl", "CSYuvToRgb");
    private Texture2D? _y, _u, _v;

    public void Execute(ID3D12GraphicsCommandList cmd, DecodedPicture picture, Texture2D output)
    {
        EnsurePlanes(picture);
        _y!.Upload(cmd, picture.Plane(0), picture.Stride(0));
        _u!.Upload(cmd, picture.Plane(1), picture.Stride(1));
        _v!.Upload(cmd, picture.Plane(2), picture.Stride(2));

        const ResourceStates srv = ResourceStates.NonPixelShaderResource;
        _y.Transition(cmd, srv);
        _u.Transition(cmd, srv);
        _v.Transition(cmd, srv);
        output.Transition(cmd, ResourceStates.UnorderedAccess);

        var p = new Params
        {
            Width = output.Width,
            Height = output.Height,
            FullRange = picture.FullRange ? 1u : 0u,
            Bt709 = picture.Bt709 ? 1u : 0u,
        };
        _kernel.Dispatch(cmd, p, [_y.Srv, _u.Srv, _v.Srv], [output.Uav], output.Width, output.Height);
        output.UavBarrier(cmd);
    }

    private void EnsurePlanes(DecodedPicture picture)
    {
        if (_y is { } y && y.Width == picture.Width && y.Height == picture.Height) return;
        gpu.WaitIdle();
        DisposePlanes();
        _y = Plane(picture.Width, picture.Height, "yuv.y");
        _u = Plane(picture.ChromaWidth, picture.ChromaHeight, "yuv.u");
        _v = Plane(picture.ChromaWidth, picture.ChromaHeight, "yuv.v");
    }

    private Texture2D Plane(int width, int height, string name) =>
        new(gpu, new TextureDesc(width, height, Format.R8_UNorm, CpuUpload: true, Name: name));

    private void DisposePlanes()
    {
        _y?.Dispose();
        _u?.Dispose();
        _v?.Dispose();
    }

    public void Dispose()
    {
        DisposePlanes();
        _kernel.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Params
    {
        public float Width;
        public float Height;
        public uint FullRange;
        public uint Bt709;
    }
}
