using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using LiveDLSS.Core.Logging;
using LiveDLSS.Protocol;

namespace LiveDLSS.Features.Egress;

public sealed record EgressFrame(
    long SessionId, ulong FrameId, int Width, int Height, uint BitrateKbps, byte[] Nv12, uint DecodeUs, uint UpscaleUs);

public sealed class EgressWorker : IDisposable
{
    private readonly Channel<EgressFrame> _queue = Channel.CreateBounded<EgressFrame>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
        dropped => ArrayPool<byte>.Shared.Return(dropped.Nv12));
    private readonly Func<ProcessedFrame, long, bool> _send;
    private readonly Thread _thread;
    private VideoEncoder? _encoder;
    private int _forceKeyframe = 1;
    private long _lastEncodeTicks;
    private string _encoderName = "";

    public EgressWorker(Func<ProcessedFrame, long, bool> send)
    {
        _send = send;
        _thread = new Thread(Run) { IsBackground = true, Name = "LiveDLSS egress", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public string EncoderName => Volatile.Read(ref _encoderName);

    public double LastEncodeMs => TimeSpan.FromTicks(Interlocked.Read(ref _lastEncodeTicks)).TotalMilliseconds;

    public static byte[] RentBuffer(int size) => ArrayPool<byte>.Shared.Rent(size);

    public void Post(EgressFrame frame)
    {
        if (!_queue.Writer.TryWrite(frame)) ArrayPool<byte>.Shared.Return(frame.Nv12);
    }

    public void RequestKeyframe() => Interlocked.Exchange(ref _forceKeyframe, 1);

    private void Run()
    {
        var reader = _queue.Reader;
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var frame))
            {
                try
                {
                    Encode(frame);
                }
                catch (InvalidOperationException e)
                {
                    Log.Error($"Encoder failed: {e.Message}");
                    _encoder?.Dispose();
                    _encoder = null;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(frame.Nv12);
                }
            }
        }
    }

    private void Encode(EgressFrame frame)
    {
        if (_encoder is null || _encoder.Width != frame.Width || _encoder.Height != frame.Height ||
            _encoder.BitrateKbps != frame.BitrateKbps)
        {
            _encoder?.Dispose();
            _encoder = new VideoEncoder(frame.Width, frame.Height, frame.BitrateKbps);
            Volatile.Write(ref _encoderName, _encoder.EncoderName);
            RequestKeyframe();
        }

        var forceKeyframe = Interlocked.Exchange(ref _forceKeyframe, 0) == 1;
        var started = Stopwatch.GetTimestamp();
        var encoded = _encoder.Encode(frame.Nv12, forceKeyframe);
        var elapsed = Stopwatch.GetElapsedTime(started);
        Interlocked.Exchange(ref _lastEncodeTicks, elapsed.Ticks);
        if (encoded.AnnexB.Length == 0)
        {
            if (forceKeyframe) RequestKeyframe();
            return;
        }

        var sent = _send(new ProcessedFrame(
            frame.FrameId, VideoCodec.H264, encoded.IsKeyframe, (ushort)frame.Width, (ushort)frame.Height,
            frame.DecodeUs, frame.UpscaleUs, (uint)elapsed.TotalMicroseconds, encoded.AnnexB), frame.SessionId);
        if (!sent) RequestKeyframe();
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _thread.Join();
        _encoder?.Dispose();
    }
}
