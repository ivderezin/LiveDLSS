namespace LiveDLSS.Protocol;

public static class MessageFraming
{
    public static async ValueTask<Message?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var headerBytes = new byte[MessageHeader.Size];
        if (!await ReadExactlyOrEofAsync(stream, headerBytes, ct).ConfigureAwait(false)) return null;

        var header = MessageHeader.Decode(headerBytes);
        var payload = header.PayloadSize == 0 ? [] : new byte[header.PayloadSize];
        if (payload.Length > 0 && !await ReadExactlyOrEofAsync(stream, payload, ct).ConfigureAwait(false))
        {
            throw new ProtocolException("connection closed mid-message");
        }
        return MessageCodec.Decode(header, payload);
    }

    public static ValueTask WriteAsync(Stream stream, Message message, CancellationToken ct = default) =>
        stream.WriteAsync(MessageCodec.Encode(message), ct);

    private static async ValueTask<bool> ReadExactlyOrEofAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (read == 0) return false;
        if (read < buffer.Length) throw new ProtocolException("short read");
        return true;
    }
}
