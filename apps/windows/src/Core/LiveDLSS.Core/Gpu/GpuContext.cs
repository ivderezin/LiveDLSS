using LiveDLSS.Core.Logging;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using ShaderResourceViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension;

namespace LiveDLSS.Core.Gpu;

public sealed class GpuContext : IDisposable
{
    public const int ShaderVisibleDescriptors = 4096;
    public const int PersistentDescriptors = 64;
    public const int StagingDescriptors = 512;
    public const int TimestampCount = 16;
    private const uint NvidiaVendorId = 0x10DE;

    private readonly ID3D12CommandAllocator _allocator;
    private readonly ID3D12Fence _fence;
    private readonly AutoResetEvent _fenceEvent = new(false);
    private ulong _fenceValue;

    private readonly ID3D12DescriptorHeap _stagingHeap;
    private readonly uint _descriptorSize;
    private readonly Stack<int> _freeStaging = new();
    private readonly Stack<int> _freePersistent = new();
    private int _ringCursor;

    private readonly ID3D12QueryHeap _queryHeap;
    private readonly ID3D12Resource _queryReadback;
    private readonly ulong[] _timestamps = new ulong[TimestampCount];
    private readonly ulong _timestampFrequency;
    private int _usedTimestamps;

    public IDXGIFactory6 Factory { get; }
    public ID3D12Device Device { get; }
    public ID3D12CommandQueue Queue { get; }
    public ID3D12GraphicsCommandList Cmd { get; }
    public ID3D12DescriptorHeap ShaderVisibleHeap { get; }
    public CpuDescriptorHandle NullSrv { get; }
    public CpuDescriptorHandle NullUav { get; }
    public Vortice.Luid AdapterLuid { get; }
    public string AdapterName { get; }
    public bool IsNvidia { get; }

    public GpuContext(bool enableDebugLayer)
    {
        if (enableDebugLayer && D3D12.D3D12GetDebugInterface(out ID3D12Debug? debug).Success)
        {
            debug!.EnableDebugLayer();
            debug.Dispose();
        }

        Factory = DXGI.CreateDXGIFactory2<IDXGIFactory6>(enableDebugLayer);

        IDXGIAdapter1? chosen = null;
        for (uint i = 0; Factory.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1? adapter).Success; i++)
        {
            var desc = adapter!.Description1;
            if ((desc.Flags & AdapterFlags.Software) != 0)
            {
                adapter.Dispose();
                continue;
            }
            if (chosen is null || desc.VendorId == NvidiaVendorId)
            {
                chosen?.Dispose();
                chosen = adapter;
                if (desc.VendorId == NvidiaVendorId) break;
            }
            else
            {
                adapter.Dispose();
            }
        }
        if (chosen is null) throw new InvalidOperationException("No hardware D3D12 adapter found");

        var chosenDesc = chosen.Description1;
        AdapterLuid = chosenDesc.Luid;
        AdapterName = chosenDesc.Description;
        IsNvidia = chosenDesc.VendorId == NvidiaVendorId;
        Device = D3D12.D3D12CreateDevice<ID3D12Device>(chosen, FeatureLevel.Level_12_0);
        chosen.Dispose();
        Log.Info($"GPU: {AdapterName}");

        Queue = Device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
        _allocator = Device.CreateCommandAllocator(CommandListType.Direct);
        Cmd = Device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _allocator, null);
        Cmd.Close();
        _fence = Device.CreateFence(0, FenceFlags.None);

        _descriptorSize = Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        ShaderVisibleHeap = Device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, ShaderVisibleDescriptors,
            DescriptorHeapFlags.ShaderVisible));
        _stagingHeap = Device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, StagingDescriptors));
        for (var i = StagingDescriptors - 1; i >= 0; i--) _freeStaging.Push(i);
        for (var i = PersistentDescriptors - 1; i >= 0; i--) _freePersistent.Push(i);

        NullSrv = AllocStaging();
        Device.CreateShaderResourceView(null, new ShaderResourceViewDescription
        {
            Format = Format.R8G8B8A8_UNorm,
            ViewDimension = ShaderResourceViewDimension.Texture2D,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Texture2D = new Texture2DShaderResourceView { MipLevels = 1 },
        }, NullSrv);
        NullUav = AllocStaging();
        Device.CreateUnorderedAccessView(null, null, new UnorderedAccessViewDescription
        {
            Format = Format.R8G8B8A8_UNorm,
            ViewDimension = UnorderedAccessViewDimension.Texture2D,
        }, NullUav);

        _queryHeap = Device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, TimestampCount));
        _queryReadback = Device.CreateCommittedResource(
            HeapType.Readback, ResourceDescription.Buffer(sizeof(ulong) * TimestampCount), ResourceStates.CopyDest);
        Queue.GetTimestampFrequency(out _timestampFrequency).CheckError();
    }

    public void BeginFrame()
    {
        _allocator.Reset();
        Cmd.Reset(_allocator, null);
        _ringCursor = 0;
        _usedTimestamps = 0;
        BindDescriptorHeap();
    }

    public void BindDescriptorHeap() => Cmd.SetDescriptorHeaps(ShaderVisibleHeap);

    public void SubmitAndWait()
    {
        if (_usedTimestamps > 0)
        {
            Cmd.ResolveQueryData(_queryHeap, QueryType.Timestamp, 0, (uint)_usedTimestamps, _queryReadback, 0);
        }
        Cmd.Close();
        Queue.ExecuteCommandList(Cmd);
        WaitIdle();

        if (_usedTimestamps > 0)
        {
            unsafe
            {
                void* mapped;
                _queryReadback.Map(0, &mapped).CheckError();
                new ReadOnlySpan<ulong>(mapped, _usedTimestamps).CopyTo(_timestamps);
                _queryReadback.Unmap(0);
            }
        }
    }

    public void WaitIdle()
    {
        var value = ++_fenceValue;
        Queue.Signal(_fence, value);
        if (_fence.CompletedValue < value)
        {
            _fence.SetEventOnCompletion(value, _fenceEvent);
            _fenceEvent.WaitOne();
        }
    }

    public CpuDescriptorHandle AllocStaging()
    {
        if (!_freeStaging.TryPop(out var index)) throw new InvalidOperationException("Out of staging descriptors");
        return _stagingHeap.GetCPUDescriptorHandleForHeapStart().Offset(index, _descriptorSize);
    }

    public void FreeStaging(CpuDescriptorHandle handle) =>
        _freeStaging.Push(IndexOf(handle, _stagingHeap.GetCPUDescriptorHandleForHeapStart()));

    public (CpuDescriptorHandle Cpu, GpuDescriptorHandle Gpu) AllocPersistent()
    {
        if (!_freePersistent.TryPop(out var index)) throw new InvalidOperationException("Out of persistent descriptors");
        return (ShaderVisibleHeap.GetCPUDescriptorHandleForHeapStart().Offset(index, _descriptorSize),
                ShaderVisibleHeap.GetGPUDescriptorHandleForHeapStart().Offset(index, _descriptorSize));
    }

    public void FreePersistent(CpuDescriptorHandle handle) =>
        _freePersistent.Push(IndexOf(handle, ShaderVisibleHeap.GetCPUDescriptorHandleForHeapStart()));

    public GpuDescriptorHandle CopyToTable(ReadOnlySpan<CpuDescriptorHandle> handles)
    {
        var first = PersistentDescriptors + _ringCursor;
        if (first + handles.Length > ShaderVisibleDescriptors) throw new InvalidOperationException("Descriptor ring overflow");
        _ringCursor += handles.Length;

        var dst = ShaderVisibleHeap.GetCPUDescriptorHandleForHeapStart().Offset(first, _descriptorSize);
        foreach (var src in handles)
        {
            Device.CopyDescriptorsSimple(1, dst, src, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            dst = dst.Offset(1, _descriptorSize);
        }
        return ShaderVisibleHeap.GetGPUDescriptorHandleForHeapStart().Offset(first, _descriptorSize);
    }

    public void Timestamp(int index)
    {
        if (index >= TimestampCount) return;
        Cmd.EndQuery(_queryHeap, QueryType.Timestamp, (uint)index);
        _usedTimestamps = System.Math.Max(_usedTimestamps, index + 1);
    }

    public double TimestampDeltaMs(int begin, int end) =>
        _timestamps[end] < _timestamps[begin] ? 0 : (_timestamps[end] - _timestamps[begin]) * 1000.0 / _timestampFrequency;

    private int IndexOf(CpuDescriptorHandle handle, CpuDescriptorHandle start) =>
        (int)((handle.Ptr - start.Ptr) / _descriptorSize);

    public void Dispose()
    {
        WaitIdle();
        _queryReadback.Dispose();
        _queryHeap.Dispose();
        _stagingHeap.Dispose();
        ShaderVisibleHeap.Dispose();
        _fence.Dispose();
        Cmd.Dispose();
        _allocator.Dispose();
        Queue.Dispose();
        Device.Dispose();
        Factory.Dispose();
        _fenceEvent.Dispose();
    }
}
