using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using LiveDLSS.Core.Logging;
using LiveDLSS.Protocol;

namespace LiveDLSS.Core.Net;

public sealed class TcpServer(int port) : IAsyncDisposable
{
    private const int MaxQueuedVideo = 2;

    private readonly CancellationTokenSource _cts = new();
    private OutboundSession? _outbox;
    private Task? _acceptLoop;
    private long _nextSessionId;
    private volatile bool _hasClient;

    public int Port => port;
    public bool HasClient => _hasClient;
    public long VideoSessionId => Volatile.Read(ref _outbox)?.SessionId ?? 0;

    public event Action<Message>? MessageReceived;
    public event Action<bool, string>? ConnectionChanged;

    public void Start()
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start(1);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
        Log.Info($"Listening on TCP {port}");
    }

    public bool Send(Message message) => Volatile.Read(ref _outbox)?.TryWrite(message, false, 0) ?? false;

    public bool SendVideo(Message message, long sessionId) =>
        Volatile.Read(ref _outbox)?.TryWrite(message, true, sessionId) ?? false;

    public void BeginVideoSession() =>
        Volatile.Read(ref _outbox)?.BeginSession(Interlocked.Increment(ref _nextSessionId));

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                await ServeClientAsync(client, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task ServeClientAsync(TcpClient client, CancellationToken serverCt)
    {
        client.NoDelay = true;
        var socket = client.Client;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        var peer = client.Client.RemoteEndPoint?.ToString() ?? "?";
        var stream = client.GetStream();

        var outbox = new OutboundSession(Interlocked.Increment(ref _nextSessionId));
        Volatile.Write(ref _outbox, outbox);
        _hasClient = true;
        Log.Info($"Client connected: {peer}");
        ConnectionChanged?.Invoke(true, peer);

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        var writer = WriteLoopAsync(stream, outbox, sessionCts);
        try
        {
            while (await MessageFraming.ReadAsync(stream, sessionCts.Token).ConfigureAwait(false) is { } message)
            {
                if (message is Bye) break;
                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception e) when (e is IOException or ProtocolException or SocketException)
        {
            Log.Warn($"Client {peer} dropped: {e.Message}");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Volatile.Write(ref _outbox, null);
            _hasClient = false;
            outbox.Messages.Writer.TryComplete();
            await sessionCts.CancelAsync().ConfigureAwait(false);
            try { await writer.ConfigureAwait(false); } catch (OperationCanceledException) { }
            Log.Info($"Client disconnected: {peer}");
            ConnectionChanged?.Invoke(false, peer);
        }
    }

    private static async Task WriteLoopAsync(NetworkStream stream, OutboundSession outbox, CancellationTokenSource session)
    {
        try
        {
            await foreach (var item in outbox.Messages.Reader.ReadAllAsync(session.Token).ConfigureAwait(false))
            {
                try
                {
                    if (!item.IsVideo || item.SessionId == outbox.SessionId)
                        await MessageFraming.WriteAsync(stream, item.Message, session.Token).ConfigureAwait(false);
                }
                finally { if (item.IsVideo) outbox.CompleteVideo(); }
            }
        }
        catch (IOException e)
        {
            Log.Warn($"Client write failed: {e.Message}");
        }
        finally
        {
            await session.CancelAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }

    private sealed class OutboundSession(long sessionId)
    {
        private int _queuedVideo;
        private long _sessionId = sessionId;
        public long SessionId => Volatile.Read(ref _sessionId);
        public void BeginSession(long id) => Volatile.Write(ref _sessionId, id);
        public Channel<Outgoing> Messages { get; } = Channel.CreateUnbounded<Outgoing>(
            new UnboundedChannelOptions { SingleReader = true });

        public bool TryWrite(Message message, bool isVideo, long expectedSessionId)
        {
            if (isVideo && expectedSessionId != SessionId) return false;
            if (isVideo && Interlocked.Increment(ref _queuedVideo) > MaxQueuedVideo)
            {
                Interlocked.Decrement(ref _queuedVideo);
                return false;
            }
            if (Messages.Writer.TryWrite(new Outgoing(message, isVideo, expectedSessionId))) return true;
            if (isVideo) Interlocked.Decrement(ref _queuedVideo);
            return false;
        }

        public void CompleteVideo() => Interlocked.Decrement(ref _queuedVideo);
    }

    private readonly record struct Outgoing(Message Message, bool IsVideo, long SessionId);
}
