using Vortice.Direct3D12;
using Vortice.DXGI;
using ShaderResourceViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension;

namespace LiveDLSS.Core.Gpu;

public readonly record struct TextureDesc(
    int Width,
    int Height,
    Format Format,
    bool Uav = false,
    bool CpuUpload = false,
    bool CpuReadback = false,
    string Name = "texture");

public sealed unsafe class Texture2D : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly ID3D12Resource? _upload;
    private readonly ID3D12Resource? _readback;
    private readonly byte* _uploadPtr;
    private readonly byte* _readbackPtr;
    private readonly PlacedSubresourceFootPrint _footprint;

    public TextureDesc Desc { get; }
    public int Width => Desc.Width;
    public int Height => Desc.Height;
    public ID3D12Resource Resource { get; }
    public ResourceStates State { get; private set; } = ResourceStates.Common;
    public CpuDescriptorHandle Srv { get; }
    public CpuDescriptorHandle Uav { get; }

    public Texture2D(GpuContext gpu, TextureDesc desc)
    {
        _gpu = gpu;
        Desc = desc;
        var device = gpu.Device;

        var rd = ResourceDescription.Texture2D(desc.Format, (uint)desc.Width, (uint)desc.Height, 1, 1,
            flags: desc.Uav ? ResourceFlags.AllowUnorderedAccess : ResourceFlags.None);
        Resource = device.CreateCommittedResource(HeapType.Default, rd, State);
        Resource.Name = desc.Name;

        PlacedSubresourceFootPrint footprint;
        device.GetCopyableFootprints(rd, 0, 1, 0, &footprint, null, null, out var totalBytes);
        _footprint = footprint;

        if (desc.CpuUpload)
        {
            _upload = device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(totalBytes), ResourceStates.GenericRead);
            void* p;
            _upload.Map(0, &p).CheckError();
            _uploadPtr = (byte*)p;
        }
        if (desc.CpuReadback)
        {
            _readback = device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(totalBytes), ResourceStates.CopyDest);
            void* p;
            _readback.Map(0, &p).CheckError();
            _readbackPtr = (byte*)p;
        }

        Srv = gpu.AllocStaging();
        device.CreateShaderResourceView(Resource, new ShaderResourceViewDescription
        {
            Format = desc.Format,
            ViewDimension = ShaderResourceViewDimension.Texture2D,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Texture2D = new Texture2DShaderResourceView { MipLevels = 1 },
        }, Srv);

        if (desc.Uav)
        {
            Uav = gpu.AllocStaging();
            device.CreateUnorderedAccessView(Resource, null, new UnorderedAccessViewDescription
            {
                Format = desc.Format,
                ViewDimension = UnorderedAccessViewDimension.Texture2D,
            }, Uav);
        }
    }

    public static int BytesPerPixel(Format format) => format switch
    {
        Format.R8_UNorm => 1,
        Format.R16_Float or Format.R8G8_UNorm => 2,
        Format.R32_Float or Format.R16G16_Float or Format.R8G8B8A8_UNorm => 4,
        Format.R16G16B16A16_Float => 8,
        _ => throw new NotSupportedException($"Unsupported texture format {format}"),
    };

    public void Transition(ID3D12GraphicsCommandList cmd, ResourceStates state)
    {
        if (state == State) return;
        cmd.ResourceBarrierTransition(Resource, State, state);
        State = state;
    }

    public void AssumeState(ResourceStates state) => State = state;

    public void UavBarrier(ID3D12GraphicsCommandList cmd) => cmd.ResourceBarrierUnorderedAccessView(Resource);

    public void Upload(ID3D12GraphicsCommandList cmd, ReadOnlySpan<byte> src, int srcRowPitch)
    {
        if (_uploadPtr == null) throw new InvalidOperationException("Texture has no upload buffer");
        var rowBytes = System.Math.Min(Width * BytesPerPixel(Desc.Format), srcRowPitch);
        var dstPitch = (int)_footprint.Footprint.RowPitch;
        var dstBase = _uploadPtr + _footprint.Offset;
        for (var y = 0; y < Height; y++)
        {
            src.Slice(y * srcRowPitch, rowBytes).CopyTo(new Span<byte>(dstBase + y * dstPitch, rowBytes));
        }

        Transition(cmd, ResourceStates.CopyDest);
        cmd.CopyTextureRegion(
            new TextureCopyLocation(Resource, 0), 0, 0, 0,
            new TextureCopyLocation(_upload!, _footprint));
    }

    public void CopyToReadback(ID3D12GraphicsCommandList cmd)
    {
        if (_readback is null) throw new InvalidOperationException("Texture has no readback buffer");
        Transition(cmd, ResourceStates.CopySource);
        cmd.CopyTextureRegion(new TextureCopyLocation(_readback, _footprint), 0, 0, 0, new TextureCopyLocation(Resource, 0));
    }

    public ReadOnlySpan<byte> ReadbackData =>
        _readbackPtr == null
            ? throw new InvalidOperationException("Texture has no readback buffer")
            : new ReadOnlySpan<byte>(_readbackPtr + _footprint.Offset, ReadbackRowPitch * Height);

    public int ReadbackRowPitch => (int)_footprint.Footprint.RowPitch;

    public void Dispose()
    {
        _gpu.FreeStaging(Srv);
        if (Desc.Uav) _gpu.FreeStaging(Uav);
        _upload?.Dispose();
        _readback?.Dispose();
        Resource.Dispose();
    }
}
