using FFmpeg.AutoGen;
using LiveDLSS.Core.Logging;
using LiveDLSS.Media;

namespace LiveDLSS.Features.Egress;

public sealed record EncodedFrame(byte[] AnnexB, bool IsKeyframe);

public sealed unsafe class VideoEncoder : IDisposable
{
    private static readonly string[] Candidates = ["h264_nvenc", "h264_mf", "libx264"];

    private AVCodecContext* _ctx;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private long _pts;

    public int Width { get; }
    public int Height { get; }
    public uint BitrateKbps { get; }
    public string EncoderName { get; } = "";

    public VideoEncoder(int width, int height, uint bitrateKbps)
    {
        FFmpegRuntime.EnsureInitialized();
        Width = width;
        Height = height;
        BitrateKbps = bitrateKbps;

        foreach (var name in Candidates)
        {
            if (TryOpen(name))
            {
                EncoderName = name;
                break;
            }
        }
        if (_ctx == null) throw new InvalidOperationException("No H.264 encoder available (NVENC / MF / x264)");
        Log.Info($"Encoder: {EncoderName} {width}x{height} @ {bitrateKbps} kbps");

        _frame = ffmpeg.av_frame_alloc();
        _frame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        _frame->width = width;
        _frame->height = height;
        _frame->color_range = AVColorRange.AVCOL_RANGE_MPEG;
        _frame->colorspace = AVColorSpace.AVCOL_SPC_BT709;
        _frame->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
        _frame->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
        _packet = ffmpeg.av_packet_alloc();
    }

    public EncodedFrame Encode(ReadOnlySpan<byte> nv12, bool forceKeyframe)
    {
        if (nv12.Length < Width * Height * 3 / 2) throw new ArgumentException("NV12 buffer is too small", nameof(nv12));

        _frame->pts = _pts++;
        _frame->pict_type = forceKeyframe ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;

        int sent;
        fixed (byte* data = nv12)
        {
            _frame->data[0] = data;
            _frame->data[1] = data + Width * Height;
            _frame->linesize[0] = Width;
            _frame->linesize[1] = Width;
            sent = ffmpeg.avcodec_send_frame(_ctx, _frame);
            _frame->data[0] = null;
            _frame->data[1] = null;
        }
        if (sent < 0) return new EncodedFrame([], false);

        using var output = new MemoryStream();
        var isKey = false;
        while (ffmpeg.avcodec_receive_packet(_ctx, _packet) == 0)
        {
            output.Write(new ReadOnlySpan<byte>(_packet->data, _packet->size));
            isKey |= (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
            ffmpeg.av_packet_unref(_packet);
        }
        return new EncodedFrame(output.ToArray(), isKey);
    }

    private bool TryOpen(string name)
    {
        var codec = ffmpeg.avcodec_find_encoder_by_name(name);
        if (codec == null) return false;

        var c = ffmpeg.avcodec_alloc_context3(codec);
        c->width = Width;
        c->height = Height;
        c->pix_fmt = AVPixelFormat.AV_PIX_FMT_NV12;
        c->color_range = AVColorRange.AVCOL_RANGE_MPEG;
        c->colorspace = AVColorSpace.AVCOL_SPC_BT709;
        c->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
        c->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
        c->time_base = new AVRational { num = 1, den = 60 };
        c->framerate = new AVRational { num = 60, den = 1 };
        c->gop_size = 120;
        c->max_b_frames = 0;
        c->bit_rate = BitrateKbps * 1000L;
        c->rc_max_rate = c->bit_rate;
        c->rc_buffer_size = (int)(c->bit_rate / 30);
        c->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

        switch (name)
        {
            case "h264_nvenc":
                ffmpeg.av_opt_set(c->priv_data, "preset", "p1", 0);
                ffmpeg.av_opt_set(c->priv_data, "tune", "ull", 0);
                ffmpeg.av_opt_set(c->priv_data, "rc", "cbr", 0);
                ffmpeg.av_opt_set_int(c->priv_data, "zerolatency", 1, 0);
                ffmpeg.av_opt_set_int(c->priv_data, "forced-idr", 1, 0);
                ffmpeg.av_opt_set_int(c->priv_data, "delay", 0, 0);
                break;
            case "libx264":
                ffmpeg.av_opt_set(c->priv_data, "preset", "ultrafast", 0);
                ffmpeg.av_opt_set(c->priv_data, "tune", "zerolatency", 0);
                break;
        }

        if (ffmpeg.avcodec_open2(c, codec, null) < 0)
        {
            ffmpeg.avcodec_free_context(&c);
            return false;
        }
        _ctx = c;
        return true;
    }

    public void Dispose()
    {
        var packet = _packet;
        ffmpeg.av_packet_free(&packet);
        _packet = null;
        var frame = _frame;
        ffmpeg.av_frame_free(&frame);
        _frame = null;
        var ctx = _ctx;
        ffmpeg.avcodec_free_context(&ctx);
        _ctx = null;
    }
}
