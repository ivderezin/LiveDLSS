using LiveDLSS.Core.Gpu;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LiveDLSS.Server.Platform;

internal sealed class SwapChain : IDisposable
{
    public const int BufferCount = 2;
    public const Format BackBufferFormat = Format.R8G8B8A8_UNorm;

    private readonly GpuContext _gpu;
    private readonly IDXGISwapChain3 _swapChain;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly uint _rtvSize;
    private readonly ID3D12Resource[] _buffers = new ID3D12Resource[BufferCount];

    public SwapChain(GpuContext gpu, nint hwnd)
    {
        _gpu = gpu;
        var desc = new SwapChainDescription1
        {
            BufferCount = BufferCount,
            Format = BackBufferFormat,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
        };
        using var swapChain1 = gpu.Factory.CreateSwapChainForHwnd(gpu.Queue, hwnd, desc);
        _swapChain = swapChain1.QueryInterface<IDXGISwapChain3>();
        gpu.Factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);

        _rtvHeap = gpu.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, BufferCount));
        _rtvSize = gpu.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        CreateRenderTargets();
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _gpu.WaitIdle();
        foreach (var b in _buffers) b.Dispose();
        _swapChain.ResizeBuffers(BufferCount, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
        CreateRenderTargets();
    }

    public void Begin(ID3D12GraphicsCommandList cmd)
    {
        var index = (int)_swapChain.CurrentBackBufferIndex;
        cmd.ResourceBarrierTransition(_buffers[index], ResourceStates.Present, ResourceStates.RenderTarget);
        var rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart().Offset(index, _rtvSize);
        cmd.ClearRenderTargetView(rtv, new Color4(0.08f, 0.08f, 0.09f, 1f));
        cmd.OMSetRenderTargets(rtv);
    }

    public void End(ID3D12GraphicsCommandList cmd)
    {
        var index = (int)_swapChain.CurrentBackBufferIndex;
        cmd.ResourceBarrierTransition(_buffers[index], ResourceStates.RenderTarget, ResourceStates.Present);
    }

    public void Present() => _swapChain.Present(0, PresentFlags.None);

    private void CreateRenderTargets()
    {
        for (var i = 0; i < BufferCount; i++)
        {
            _buffers[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
            _gpu.Device.CreateRenderTargetView(_buffers[i], null, _rtvHeap.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize));
        }
    }

    public void Dispose()
    {
        _gpu.WaitIdle();
        foreach (var b in _buffers) b.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
    }
}
