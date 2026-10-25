using System.Runtime.InteropServices;
using LiveDLSS.Core.Gpu;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace LiveDLSS.Features.Egress;

public sealed class RgbToNv12Pass(GpuContext gpu) : IDisposable
{
    private readonly ComputeKernel _kernel = new(gpu, typeof(RgbToNv12Pass).Assembly, "RgbToNv12.hlsl", "CSRgbToNv12");
    private Texture2D? _y, _uv;

    public int Width => _y?.Width ?? 0;
    public int Height => _y?.Height ?? 0;

    public void Execute(ID3D12GraphicsCommandList cmd, Texture2D source)
    {
        EnsurePlanes(source.Width & ~1, source.Height & ~1);
        source.Transition(cmd, ResourceStates.NonPixelShaderResource);
        _y!.Transition(cmd, ResourceStates.UnorderedAccess);
        _uv!.Transition(cmd, ResourceStates.UnorderedAccess);

        var p = new Params { Width = (uint)_y.Width, Height = (uint)_y.Height };
        _kernel.Dispatch(cmd, p, [source.Srv], [_y.Uav, _uv.Uav], _uv.Width, _uv.Height);
        _y.UavBarrier(cmd);
        _uv.UavBarrier(cmd);
        _y.CopyToReadback(cmd);
        _uv.CopyToReadback(cmd);
    }

    public void CopyTo(Span<byte> nv12)
    {
        CopyRows(_y!, nv12, _y!.Width);
        CopyRows(_uv!, nv12[(_y.Width * _y.Height)..], _uv!.Width * 2);
    }

    public int Nv12Size => Width * Height * 3 / 2;

    private static void CopyRows(Texture2D texture, Span<byte> destination, int rowBytes)
    {
        var source = texture.ReadbackData;
        var pitch = texture.ReadbackRowPitch;
        for (var y = 0; y < texture.Height; y++)
        {
            source.Slice(y * pitch, rowBytes).CopyTo(destination.Slice(y * rowBytes, rowBytes));
        }
    }

    private void EnsurePlanes(int width, int height)
    {
        if (_y is { } y && y.Width == width && y.Height == height) return;
        gpu.WaitIdle();
        _y?.Dispose();
        _uv?.Dispose();
        _y = new Texture2D(gpu, new TextureDesc(width, height, Format.R8_UNorm, Uav: true, CpuReadback: true, Name: "nv12.y"));
        _uv = new Texture2D(gpu, new TextureDesc(width / 2, height / 2, Format.R8G8_UNorm, Uav: true, CpuReadback: true, Name: "nv12.uv"));
    }

    public void Dispose()
    {
        _y?.Dispose();
        _uv?.Dispose();
        _kernel.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Params
    {
        public uint Width;
        public uint Height;
        private readonly uint _pad0;
        private readonly uint _pad1;
    }
}
