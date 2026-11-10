using System.Collections.Concurrent;
using System.Net.Sockets;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// An Ember+ provider (a Lawo desk or another device) over TCP, default port 9000. Asks for the nodes on the way to
/// every watched parameter so the provider reports their values and changes, turns those changes into on/off events,
/// and sets parameters for tallies. Keeps the tree it has seen, for browsing.
/// </summary>
public sealed class EmberConnection : IHwConnection
{
    public const int DefaultPort = 9000;

    private readonly TcpClient _client = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<HwEvent> _report;
    private readonly ConcurrentDictionary<string, EmberElement> _tree = new();
    private readonly ConcurrentDictionary<string, bool> _requested = new();
    private readonly Dictionary<EmberAddress, bool> _state = [];
    private readonly object _writeLock = new();
    private volatile EmberAddress[] _watch = [];
    private volatile EmberAddress[] _targets = [];
    private NetworkStream? _stream;
    private volatile bool _alive;
    private DateTime _lastHeard = DateTime.UtcNow;

    public HwDevice Device { get; }
    public bool Alive => _alive;
    public string Status { get; private set; } = "verbinden…";

    public EmberConnection(HwDevice device, Action<HwEvent> report)
    {
        Device = device;
        _report = report;
        _ = Task.Run(RunAsync);
    }

    /// <summary>The buttons and tallies of this device; asks for whatever was not asked for yet.</summary>
    public void Watch(IEnumerable<string> triggers, IEnumerable<string> targets)
    {
        _watch = triggers.Select(EmberAddress.Parse).OfType<EmberAddress>().Distinct().ToArray();
        _targets = targets.Select(EmberAddress.Parse).OfType<EmberAddress>().Distinct().ToArray();
        if (_alive) RequestWatched();
    }

    private async Task RunAsync()
    {
        try
        {
            string host = Device.Port;
            int port = DefaultPort;
            if (host.LastIndexOf(':') is int colon and > 0 && int.TryParse(host[(colon + 1)..], out int p)) { host = host[..colon]; port = p; }
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(5));
                await _client.ConnectAsync(host, port, connect.Token);
            }
            _stream = _client.GetStream();
            _alive = true;
            Status = "verbonden";
            _lastHeard = DateTime.UtcNow;
            Send(S101.EmberPacket(Glow.GetDirectory([])));
            RequestWatched();
            _ = Task.Run(KeepAliveAsync);

            var reader = new S101.Reader();
            var buffer = new byte[16384];
            while (!_cts.IsCancellationRequested)
            {
                int n = await _stream.ReadAsync(buffer, _cts.Token);
                if (n == 0) break;
                _lastHeard = DateTime.UtcNow;
                foreach (var message in reader.Feed(buffer.AsSpan(0, n))) Handle(message);
            }
            if (!_cts.IsCancellationRequested) Status = "verbinding verbroken";
        }
        catch (OperationCanceledException) { Status = _cts.IsCancellationRequested ? Status : $"{Device.Port} reageert niet"; }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { Status = $"{Device.Port}: {ex.Message}"; }
        finally { _alive = false; }
    }

    /// <summary>Answers nothing for 30 seconds: the connection is gone (the service opens it again).</summary>
    private async Task KeepAliveAsync()
    {
        while (_alive && !_cts.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token); } catch (OperationCanceledException) { return; }
            if (DateTime.UtcNow - _lastHeard > TimeSpan.FromSeconds(30))
            {
                Status = "geen antwoord meer";
                _alive = false;
                _client.Dispose();
                return;
            }
            Send(S101.KeepAliveRequest);
        }
    }

    private void Handle(S101Message message)
    {
        switch (message.Command)
        {
            case S101Message.KeepAliveRequest: Send(S101.KeepAliveResponse); return;
            case S101Message.Ember: break;
            default: return;
        }
        List<EmberElement> elements;
        try { elements = Glow.Parse(message.Payload); }
        catch (FormatException) { return; }
        foreach (var e in elements)
        {
            var merged = _tree.AddOrUpdate(e.Path, e, (_, old) => e.Merge(old));
            if (!e.HasValue) continue;
            foreach (var a in _watch)
            {
                if (a.Resolve(_tree.Values, out _) is not { } numbers || string.Join('.', numbers) != merged.Path) continue;
                if (a.IsOn(merged.Value) is not bool on) continue;
                bool known;
                lock (_state)
                {
                    known = _state.TryGetValue(a, out bool was);
                    if (known && was == on) continue;
                    _state[a] = on;
                }
                // The first value after connecting is how things are, not something somebody did.
                if (known) _report(new HwEvent(Device.Id, a.ToString(), on));
            }
        }
        RequestWatched(); // nodes that just became known may lead to watched parameters
    }

    /// <summary>Asks for every node on the way to a watched parameter or tally, once per connection.</summary>
    private void RequestWatched()
    {
        foreach (var a in _watch.Concat(_targets))
        {
            // A named address is asked for as far as its names are known; every answer brings it a level further.
            bool found = a.Resolve(_tree.Values, out int[] path) is not null;
            for (int depth = a.Names is null ? 1 : 0; depth < path.Length + (found ? 0 : 1); depth++)
            {
                string key = string.Join('.', path.Take(depth));
                if (_requested.TryAdd(key, true)) Send(S101.EmberPacket(Glow.GetDirectory(path[..depth])));
            }
        }
    }

    private bool Send(byte[] frame)
    {
        if (!_alive || _stream is null) return false;
        try
        {
            lock (_writeLock) _stream.Write(frame);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _alive = false;
            Status = "schrijven mislukt";
            return false;
        }
    }

    public void Show(HwOutput output, bool on)
    {
        if (EmberAddress.Parse(output.Target) is not { } a || a.Resolve(_tree.Values, out _) is not { } numbers) return;
        var type = _tree.TryGetValue(string.Join('.', numbers), out var known) ? known.EffectiveType : EmberType.Boolean;
        if (EmberAddress.ValueFor(type, on, on ? output.OnText : output.OffText) is { } value)
            Send(S101.EmberPacket(Glow.SetValue(numbers, value)));
    }

    /// <summary>The children of a node (the top when the path is empty), asked fresh from the provider.</summary>
    public async Task<IReadOnlyList<EmberElement>?> BrowseAsync(string path, CancellationToken ct)
    {
        if (EmberAddress.ParsePath(path) is not { } numbers) return null;
        bool IsChild(EmberElement e) => e.Numbers is var n && n.Length == numbers.Length + 1 && n.AsSpan(0, numbers.Length).SequenceEqual(numbers);
        int before = _tree.Values.Count(IsChild);
        if (!Send(S101.EmberPacket(Glow.GetDirectory(numbers)))) return null;
        _requested.TryAdd(path, true);
        // Wait for the answer: the children arrive, or stop growing.
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(100, ct);
            int now = _tree.Values.Count(IsChild);
            if (now > 0 && (now > before || i >= 5)) { await Task.Delay(150, ct); break; }
        }
        return _tree.Values.Where(IsChild).OrderBy(e => e.Numbers[^1]).ToList();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _alive = false;
        _client.Dispose();
    }
}
