using System.Runtime.InteropServices;

namespace LiveDLSS.Features.NeuralRendering;

internal static partial class NrNative
{
    private const string Library = "nvngx.dll_LiveDLSS.NeuralRendering";

    [StructLayout(LayoutKind.Sequential)]
    internal struct Resources
    {
        public nint Color, Depth, Motion, Output;
        public uint Width, Height, GuideWidth, GuideHeight;
        public float MvScaleX, MvScaleY;
        public int Reset, DepthInverted;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Options
    {
        public float Intensity;
        public int Style, Preset;
        public float LocalStructure, LocalTone, SkinStructure;
        public int AutoMask;
    }

    [LibraryImport(Library, EntryPoint = "ldls_nr_init", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int Init(nint device, string runtimeDirectory, string cacheDirectory, uint appId);

    [LibraryImport(Library, EntryPoint = "ldls_nr_create")]
    internal static partial int Create(nint commandList, uint width, uint height, in Options options);

    [LibraryImport(Library, EntryPoint = "ldls_nr_evaluate")]
    internal static partial int Evaluate(nint commandList, in Resources resources);

    [LibraryImport(Library, EntryPoint = "ldls_nr_free")]
    internal static partial void Free();

    [LibraryImport(Library, EntryPoint = "ldls_nr_shutdown")]
    internal static partial void Shutdown();

    [LibraryImport(Library, EntryPoint = "ldls_nr_last_error")]
    private static partial nint LastError();

    internal static string Error => Marshal.PtrToStringUTF8(LastError()) ?? "Unknown NR error";
}
