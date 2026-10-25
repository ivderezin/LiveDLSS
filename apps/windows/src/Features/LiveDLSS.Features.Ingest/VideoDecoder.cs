using FFmpeg.AutoGen;
using LiveDLSS.Core.Logging;
using LiveDLSS.Media;

namespace LiveDLSS.Features.Ingest;

public sealed unsafe class DecodedPicture
{
    private readonly AVFrame* _frame;

    internal DecodedPicture(AVFrame* frame) => _frame = frame;

    public int Width => _frame->width;
    public int Height => _frame->height;
    public int ChromaWidth => (Width + 1) / 2;
    public int ChromaHeight => (Height + 1) / 2;

    public bool FullRange =>
        _frame->color_range == AVColorRange.AVCOL_RANGE_JPEG ||
        (AVPixelFormat)_frame->format == AVPixelFormat.AV_PIX_FMT_YUVJ420P;

    public bool Bt709 => _frame->colorspace == AVColorSpace.AVCOL_SPC_BT709;

    public int Stride(int plane) => _frame->linesize[(uint)plane];

    public ReadOnlySpan<byte> Plane(int plane) =>
        new(_frame->data[(uint)plane], Stride(plane) * (plane == 0 ? Height : ChromaHeight));
}

public sealed unsafe class VideoDecoder : IDisposable
{
    private AVCodecContext* _ctx;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private AVFrame* _received;
    private byte[] _packetBuffer = [];

    public VideoDecoder()
    {
        FFmpegRuntime.EnsureInitialized();
        Open();
    }

    public void Reset()
    {
        Close();
        Open();
    }

    public DecodedPicture? Decode(ReadOnlySpan<byte> annexB)
    {
        var padded = annexB.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE;
        if (_packetBuffer.Length < padded) _packetBuffer = new byte[padded];
        annexB.CopyTo(_packetBuffer);
        _packetBuffer.AsSpan(annexB.Length, ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE).Clear();

        fixed (byte* data = _packetBuffer)
        {
            _packet->data = data;
            _packet->size = annexB.Length;
            var sent = ffmpeg.avcodec_send_packet(_ctx, _packet);
            _packet->data = null;
            _packet->size = 0;
            if (sent < 0)
            {
                Log.Warn($"Decoder rejected packet ({annexB.Length} bytes): {FFmpegRuntime.Error(sent)}");
                return null;
            }
        }

        var produced = false;
        while (ffmpeg.avcodec_receive_frame(_ctx, _received) == 0)
        {
            ffmpeg.av_frame_unref(_frame);
            ffmpeg.av_frame_move_ref(_frame, _received);
            produced = true;
        }
        if (!produced) return null;

        var format = (AVPixelFormat)_frame->format;
        if (format is not (AVPixelFormat.AV_PIX_FMT_YUV420P or AVPixelFormat.AV_PIX_FMT_YUVJ420P))
        {
            Log.Warn($"Unsupported decoded pixel format {format}");
            return null;
        }
        return new DecodedPicture(_frame);
    }

    private void Open()
    {
        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        _ctx = ffmpeg.avcodec_alloc_context3(codec);
        _ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        _ctx->thread_count = 1;
        var opened = ffmpeg.avcodec_open2(_ctx, codec, null);
        if (opened < 0) throw new InvalidOperationException($"avcodec_open2(h264): {FFmpegRuntime.Error(opened)}");
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
        _received = ffmpeg.av_frame_alloc();
    }

    private void Close()
    {
        var frame = _frame;
        ffmpeg.av_frame_free(&frame);
        _frame = null;
        var received = _received;
        ffmpeg.av_frame_free(&received);
        _received = null;
        var packet = _packet;
        ffmpeg.av_packet_free(&packet);
        _packet = null;
        var ctx = _ctx;
        ffmpeg.avcodec_free_context(&ctx);
        _ctx = null;
    }

    public void Dispose() => Close();
}
