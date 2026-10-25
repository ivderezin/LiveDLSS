using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D12;
using Hexa.NET.ImGui.Backends.Win32;
using LiveDLSS.Core.Gpu;
using Vortice.DXGI;
using HexaCpuHandle = Hexa.NET.ImGui.Backends.D3D12.D3D12CpuDescriptorHandle;
using HexaGpuHandle = Hexa.NET.ImGui.Backends.D3D12.D3D12GpuDescriptorHandle;

namespace LiveDLSS.Features.Inspector;

public sealed unsafe class ImGuiHost : IDisposable
{
    private static GpuContext? _gpu;

    public ImGuiHost(GpuContext gpu, nint hwnd, Format renderTargetFormat, int framesInFlight)
    {
        _gpu = gpu;
        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        ImGuiImplWin32.SetCurrentContext(context);
        ImGuiImplD3D12.SetCurrentContext(context);
        ImGui.StyleColorsDark();

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (File.Exists(font)) io.Fonts.AddFontFromFileTTF(font, 18f);

        if (!ImGuiImplWin32.Init((void*)hwnd)) throw new InvalidOperationException("ImGui Win32 backend init failed");

        var info = new ImGuiImplDX12InitInfo
        {
            Device = (ID3D12Device*)gpu.Device.NativePointer,
            CommandQueue = (ID3D12CommandQueue*)gpu.Queue.NativePointer,
            NumFramesInFlight = framesInFlight,
            RTVFormat = (int)renderTargetFormat,
            DSVFormat = (int)Format.Unknown,
            SrvDescriptorHeap = (ID3D12DescriptorHeap*)gpu.ShaderVisibleHeap.NativePointer,
            SrvDescriptorAllocFn = (delegate* unmanaged[Cdecl]<ImGuiImplDX12InitInfo*, HexaCpuHandle*, HexaGpuHandle*, void>)&AllocSrv,
            SrvDescriptorFreeFn = (delegate* unmanaged[Cdecl]<ImGuiImplDX12InitInfo*, HexaCpuHandle, HexaGpuHandle, void>)&FreeSrv,
        };
        if (!ImGuiImplD3D12.Init(ref info)) throw new InvalidOperationException("ImGui DX12 backend init failed");
    }

    public static bool HandleWindowMessage(nint hwnd, uint msg, nuint wParam, nint lParam) =>
        ImGuiImplWin32.WndProcHandler(hwnd, msg, wParam, lParam) != 0;

    public void NewFrame()
    {
        ImGuiImplD3D12.NewFrame();
        ImGuiImplWin32.NewFrame();
        ImGui.NewFrame();
    }

    public void Render(Vortice.Direct3D12.ID3D12GraphicsCommandList cmd)
    {
        ImGui.Render();
        ImGuiImplD3D12.RenderDrawData(ImGui.GetDrawData(), (ID3D12GraphicsCommandList*)cmd.NativePointer);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void AllocSrv(ImGuiImplDX12InitInfo* info, HexaCpuHandle* cpu, HexaGpuHandle* gpu)
    {
        var (c, g) = _gpu!.AllocPersistent();
        *cpu = new HexaCpuHandle(c.Ptr);
        *gpu = new HexaGpuHandle(g.Ptr);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeSrv(ImGuiImplDX12InitInfo* info, HexaCpuHandle cpu, HexaGpuHandle gpu) =>
        _gpu!.FreePersistent(new Vortice.Direct3D12.CpuDescriptorHandle { Ptr = cpu.Ptr });

    public void Dispose()
    {
        ImGuiImplD3D12.Shutdown();
        ImGuiImplWin32.Shutdown();
        ImGui.DestroyContext();
        _gpu = null;
    }
}
