using System.Reflection;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D12;

namespace LiveDLSS.Core.Gpu;

public sealed class ComputeKernel : IDisposable
{
    public const int MaxConstants = 48;
    public const int MaxSrvs = 4;
    public const int MaxUavs = 4;

    private readonly GpuContext _gpu;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pso;

    public ComputeKernel(GpuContext gpu, Assembly shaderAssembly, string shaderFile, string entryPoint)
    {
        _gpu = gpu;
        _rootSignature = CreateSharedRootSignature(gpu.Device);

        var source = LoadShaderSource(shaderAssembly, shaderFile);
#if DEBUG
        const ShaderFlags flags = ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#else
        const ShaderFlags flags = ShaderFlags.OptimizationLevel3;
#endif
        ReadOnlyMemory<byte> code;
        try
        {
            code = Compiler.Compile(source, entryPoint, shaderFile, "cs_5_1", flags, EffectFlags.None);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Shader {shaderFile}:{entryPoint} failed: {e.Message}", e);
        }

        _pso = gpu.Device.CreateComputePipelineState<ID3D12PipelineState>(new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = code,
        });
    }

    public void Dispatch<TConstants>(
        ID3D12GraphicsCommandList cmd,
        in TConstants constants,
        ReadOnlySpan<CpuDescriptorHandle> srvs,
        ReadOnlySpan<CpuDescriptorHandle> uavs,
        int threadsX,
        int threadsY)
        where TConstants : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<TConstants>(in constants));
        if (bytes.Length % 4 != 0 || bytes.Length > MaxConstants * 4)
        {
            throw new ArgumentException($"Constants must be a multiple of 4 bytes and ≤ {MaxConstants * 4}");
        }

        Span<CpuDescriptorHandle> srvTable = stackalloc CpuDescriptorHandle[MaxSrvs];
        Span<CpuDescriptorHandle> uavTable = stackalloc CpuDescriptorHandle[MaxUavs];
        for (var i = 0; i < MaxSrvs; i++) srvTable[i] = i < srvs.Length ? srvs[i] : _gpu.NullSrv;
        for (var i = 0; i < MaxUavs; i++) uavTable[i] = i < uavs.Length ? uavs[i] : _gpu.NullUav;

        cmd.SetComputeRootSignature(_rootSignature);
        cmd.SetPipelineState(_pso);
        unsafe
        {
            fixed (byte* p = bytes)
            {
                cmd.SetComputeRoot32BitConstants(0, (uint)(bytes.Length / 4), p, 0);
            }
        }
        cmd.SetComputeRootDescriptorTable(1, _gpu.CopyToTable(srvTable));
        cmd.SetComputeRootDescriptorTable(2, _gpu.CopyToTable(uavTable));
        cmd.Dispatch((uint)(threadsX + 7) / 8, (uint)(threadsY + 7) / 8, 1);
    }

    private static string LoadShaderSource(Assembly assembly, string shaderFile)
    {
        var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(shaderFile, StringComparison.Ordinal))
            ?? throw new FileNotFoundException($"Embedded shader '{shaderFile}' not found in {assembly.GetName().Name}");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ID3D12RootSignature CreateSharedRootSignature(ID3D12Device device)
    {
        var description = new RootSignatureDescription1(
            RootSignatureFlags.None,
            [
                new RootParameter1(new RootConstants(0, 0, MaxConstants), ShaderVisibility.All),
                new RootParameter1(new RootDescriptorTable1(
                    new DescriptorRange1(DescriptorRangeType.ShaderResourceView, MaxSrvs, 0)), ShaderVisibility.All),
                new RootParameter1(new RootDescriptorTable1(
                    new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, MaxUavs, 0)), ShaderVisibility.All),
            ],
            [
                new StaticSamplerDescription(0, Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
                    TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, StaticBorderColor.TransparentBlack, 0,
                    float.MaxValue, ShaderVisibility.All, 0),
            ]);
        return device.CreateRootSignature(description);
    }

    public void Dispose()
    {
        _pso.Dispose();
        _rootSignature.Dispose();
    }
}
