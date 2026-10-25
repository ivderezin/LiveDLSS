using System.Runtime.InteropServices;

namespace LiveDLSS.Features.Upscale.Dlss;

internal static partial class DlssNative
{
    private const string Library = "LiveDLSS.Dlss";

    public const int Ok = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Resources
    {
        public nint Color;
        public nint Depth;
        public nint Motion;
        public nint Output;
        public uint ColorState;
        public uint DepthState;
        public uint MotionState;
        public uint OutputState;
        public uint RenderWidth;
        public uint RenderHeight;
        public uint OutputWidth;
        public uint OutputHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Options
    {
        public int Mode;
        public int Preset;
        public float Sharpness;
        public int Reset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Constants
    {
        public fixed float CameraViewToClip[16];
        public fixed float ClipToCameraView[16];
        public fixed float ClipToPrevClip[16];
        public fixed float PrevClipToClip[16];
        public fixed float CameraPos[3];
        public fixed float CameraUp[3];
        public fixed float CameraRight[3];
        public fixed float CameraFwd[3];
        public float CameraNear;
        public float CameraFar;
        public float CameraFov;
        public float CameraAspect;
        public float MvecScaleX;
        public float MvecScaleY;
    }

    [LibraryImport(Library, EntryPoint = "ldls_dlss_init")]
    public static partial int Init(uint appId);

    [LibraryImport(Library, EntryPoint = "ldls_dlss_set_device")]
    public static partial int SetDevice(nint d3d12Device, ulong adapterLuid);

    [LibraryImport(Library, EntryPoint = "ldls_dlss_evaluate")]
    public static partial int Evaluate(nint commandList, in Resources resources, in Options options, in Constants constants, uint frameIndex);

    [LibraryImport(Library, EntryPoint = "ldls_dlss_free_resources")]
    public static partial void FreeResources();

    [LibraryImport(Library, EntryPoint = "ldls_dlss_shutdown")]
    public static partial void Shutdown();
}
