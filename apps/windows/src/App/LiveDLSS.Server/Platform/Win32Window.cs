using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LiveDLSS.Features.Inspector;

namespace LiveDLSS.Server.Platform;

internal sealed unsafe partial class Win32Window : IDisposable
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const int CwUseDefault = unchecked((int)0x80000000);
    private const uint WmSize = 0x0005;
    private const uint WmClose = 0x0010;
    private const uint WmTimer = 0x0113;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const nuint ModalTimerId = 1;
    private const uint PmRemove = 0x0001;
    private const int SwShowDefault = 10;
    private const int SizeMinimized = 1;
    private const nint IdcArrow = 32512;

    private static bool _closeRequested;
    private static (int W, int H)? _pendingResize;
    private static Action? _modalTick;

    private readonly nint _className;

    public nint Handle { get; }

    public Action? ModalTick
    {
        get => _modalTick;
        set => _modalTick = value;
    }

    public Win32Window(string title, int width, int height)
    {
        var instance = GetModuleHandleW(null);
        _className = Marshal.StringToHGlobalUni("LiveDLSSWindow");

        var wc = new WndClassExW
        {
            cbSize = (uint)sizeof(WndClassExW),
            style = 0x0040,
            lpfnWndProc = &WndProc,
            hInstance = instance,
            hCursor = LoadCursorW(0, IdcArrow),
            lpszClassName = _className,
        };
        if (RegisterClassExW(&wc) == 0) throw new InvalidOperationException("RegisterClassEx failed");

        Handle = CreateWindowExW(0, "LiveDLSSWindow", title, WsOverlappedWindow, CwUseDefault, CwUseDefault,
            width, height, 0, 0, instance, 0);
        if (Handle == 0) throw new InvalidOperationException("CreateWindowEx failed");
        ShowWindow(Handle, SwShowDefault);
    }

    public bool PumpMessages()
    {
        Msg msg;
        while (PeekMessageW(&msg, 0, 0, 0, PmRemove))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return !_closeRequested;
    }

    public (int W, int H)? ConsumeResize()
    {
        var size = _pendingResize;
        _pendingResize = null;
        return size;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (ImGuiHost.HandleWindowMessage(hwnd, msg, wParam, lParam)) return 1;

        switch (msg)
        {
            case WmSize when (int)wParam != SizeMinimized:
                _pendingResize = ((int)(lParam & 0xFFFF), (int)((lParam >> 16) & 0xFFFF));
                return 0;
            case WmClose:
                _closeRequested = true;
                return 0;
            case WmEnterSizeMove:
                SetTimer(hwnd, ModalTimerId, 1, 0);
                return 0;
            case WmExitSizeMove:
                KillTimer(hwnd, ModalTimerId);
                return 0;
            case WmTimer when wParam == ModalTimerId:
                _modalTick?.Invoke();
                return 0;
            default:
                return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        DestroyWindow(Handle);
        Marshal.FreeHGlobal(_className);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassExW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WndClassExW* wc);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int cmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(Msg* msg, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint hwnd, nuint id, uint elapseMs, nint timerProc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);
}
