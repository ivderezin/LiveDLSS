using LiveDLSS.Core.Logging;
using LiveDLSS.Protocol;

namespace LiveDLSS.Features.Ingest;

public sealed record FramePacket(CameraFrame Camera, DepthFrame? Depth, long SessionId);

public sealed record ClientInfo(bool Connected, string Peer, Hello? Hello);

public sealed class IngestService
{
    public const int MaxQueuedFrames = 8;

    private readonly Lock _gate = new();
    private readonly Queue<FramePacket> _frames = new();
    private readonly SortedDictionary<ulong, DepthFrame> _pendingDepth = [];
    private Settings _settings = new();
    private ClientInfo _client = new(false, "", null);
    private bool _sessionReset;
    private long _dropped;

    public Settings Settings { get { lock (_gate) return _settings; } }
    public ClientInfo Client { get { lock (_gate) return _client; } }
    public long DroppedFrames => Interlocked.Read(ref _dropped);

    public void OnMessage(Message message, long sessionId)
    {
        lock (_gate)
        {
            switch (message)
            {
                case DepthFrame depth:
                    _pendingDepth[depth.FrameId] = depth;
                    while (_pendingDepth.Count > 4) _pendingDepth.Remove(_pendingDepth.Keys.First());
                    break;

                case CameraFrame camera:
                    DepthFrame? matched = null;
                    if (_pendingDepth.Remove(camera.FrameId, out var d)) matched = d;
                    foreach (var stale in _pendingDepth.Keys.Where(k => k < camera.FrameId).ToList()) _pendingDepth.Remove(stale);

                    if (_frames.Count >= MaxQueuedFrames)
                    {
                        Interlocked.Add(ref _dropped, _frames.Count);
                        _frames.Clear();
                        _sessionReset = true;
                    }
                    _frames.Enqueue(new FramePacket(camera, matched, sessionId));
                    break;

                case Settings settings:
                    _settings = settings;
                    Log.Info($"Settings: {settings}");
                    break;

                case Hello hello:
                    _frames.Clear();
                    _pendingDepth.Clear();
                    Log.Info($"Hello from '{hello.ClientName}', depth={hello.HasDepth}, {hello.CaptureWidth}x{hello.CaptureHeight}");
                    _client = _client with { Hello = hello };
                    _sessionReset = true;
                    break;
            }
        }
    }

    public void OnConnection(bool connected, string peer)
    {
        lock (_gate)
        {
            _client = new ClientInfo(connected, peer, null);
            _frames.Clear();
            _pendingDepth.Clear();
            _sessionReset = true;
        }
    }

    public List<FramePacket> DrainFrames()
    {
        lock (_gate)
        {
            var list = _frames.ToList();
            _frames.Clear();
            return list;
        }
    }

    public bool ConsumeSessionReset()
    {
        lock (_gate)
        {
            var reset = _sessionReset;
            _sessionReset = false;
            return reset;
        }
    }
}
