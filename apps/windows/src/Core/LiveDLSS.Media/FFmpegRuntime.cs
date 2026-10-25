using FFmpeg.AutoGen;
using LiveDLSS.Core.Logging;

namespace LiveDLSS.Media;

public static class FFmpegRuntime
{
    private static readonly Lock Gate = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized) return;

            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            var fromEnv = Environment.GetEnvironmentVariable("FFMPEG_ROOT");
            ffmpeg.RootPath = Directory.Exists(local) ? local : fromEnv ?? string.Empty;
            DynamicallyLoadedBindings.Initialize();

            unsafe
            {
                Log.Info($"FFmpeg {ffmpeg.av_version_info()} (root: '{ffmpeg.RootPath}')");
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
            }
            _initialized = true;
        }
    }

    public static unsafe string Error(int code)
    {
        const int size = 256;
        var buffer = stackalloc byte[size];
        ffmpeg.av_strerror(code, buffer, size);
        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)buffer) ?? code.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
