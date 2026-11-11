using Microsoft.Extensions.Logging;

namespace ReaRadioDj.Core.Streaming;

/// <summary>An extra stream server: Icecast (host:port/mount) or SHOUTcast (host:port, v2 as host:port,sid), each with its own MP3 quality.</summary>
public sealed record StreamTarget(string Name, string Server, string Password, int Bitrate = 128, bool Enabled = true, string Genre = "", string Url = "");

/// <summary>The state of one stream server.</summary>
public sealed record StreamTargetStatus(string Name, string Server, int Bitrate, bool Enabled, bool Connected, long BytesSent, string? Error);

/// <summary>
/// The station's streams: the main stream from the settings plus any number of extra servers. Start, stop and title
/// updates go to all of them; each has its own encoder, so one server going down does not touch the others.
/// </summary>
public sealed class StreamGroup : IDisposable
{
    public static readonly int[] Bitrates = [32, 48, 64, 96, 128, 160, 192, 256, 320];

    private readonly IcecastStreamer _main;
    private readonly string _stationName;
    private readonly ILoggerFactory? _loggers;
    private readonly object _gate = new();
    private List<(StreamTarget Target, IcecastStreamer Streamer)> _extra = [];
    private int _handle;   // the mixer the streams encode, once started
    private bool _mainWanted;
    private DateTime _lastTry = DateTime.MinValue;

    /// <summary>A stream that dropped is tried again this often.</summary>
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(15);

    public StreamGroup(IcecastStreamer main, string stationName, ILoggerFactory? loggers = null)
    {
        _main = main;
        _stationName = stationName;
        _loggers = loggers;
    }

    public IcecastStreamer Main => _main;

    public IReadOnlyList<StreamTarget> Targets { get { lock (_gate) return [.. _extra.Select(e => e.Target)]; } }

    /// <summary>Drops targets without a server and keeps the quality to a known MP3 bitrate.</summary>
    public static IReadOnlyList<StreamTarget> Clean(IEnumerable<StreamTarget> targets) =>
        [.. targets.Where(t => !string.IsNullOrWhiteSpace(t.Server))
                   .Select((t, i) => t with
                   {
                       Name = string.IsNullOrWhiteSpace(t.Name) ? $"Server {i + 2}" : t.Name.Trim(),
                       Server = t.Server.Trim(), Password = t.Password ?? "", Genre = (t.Genre ?? "").Trim(), Url = (t.Url ?? "").Trim(),
                       Bitrate = Bitrates.Contains(t.Bitrate) ? t.Bitrate : 128,
                   })];

    /// <summary>Replaces the extra servers; when the streams run, the new ones start right away.</summary>
    public void SetTargets(IEnumerable<StreamTarget> targets)
    {
        List<(StreamTarget, IcecastStreamer)> old;
        lock (_gate)
        {
            old = _extra;
            _extra = [.. Clean(targets).Select(t => (t, new IcecastStreamer(Options(t), _stationName,
                _loggers?.CreateLogger<IcecastStreamer>())))];
            if (_handle != 0) foreach (var (t, s) in _extra) if (t.Enabled) s.Start(_handle);
        }
        foreach (var (_, s) in old) s.Dispose();
    }

    private static StreamOptions Options(StreamTarget t) => new()
    {
        Enabled = t.Enabled, Server = t.Server, Password = t.Password, Bitrate = t.Bitrate,
        Genre = t.Genre.Length > 0 ? t.Genre : "Various", Url = t.Url,
    };

    /// <summary>Starts the main stream (when <paramref name="main"/>) and every enabled extra server.</summary>
    public void Start(int mixerHandle, bool main = true)
    {
        if (main) _main.Start(mixerHandle);
        lock (_gate)
        {
            _handle = mixerHandle;
            if (main) _mainWanted = true;
            foreach (var (t, s) in _extra) if (t.Enabled) s.Start(mixerHandle);
        }
    }

    public void Stop()
    {
        _main.Stop();
        lock (_gate)
        {
            _handle = 0;
            _mainWanted = false;
            foreach (var (_, s) in _extra) s.Stop();
        }
    }

    /// <summary>
    /// Reconnects streams that should run but dropped (server restarted, network gone), at most every
    /// <see cref="RetryEvery"/>. Returns the number of streams tried.
    /// </summary>
    public int Keepalive(DateTime now)
    {
        int handle; bool mainWanted; List<IcecastStreamer> extras;
        lock (_gate)
        {
            if (_handle == 0 || now - _lastTry < RetryEvery) return 0;
            handle = _handle; mainWanted = _mainWanted;
            extras = [.. _extra.Where(e => e.Target.Enabled && !e.Streamer.GetStatus().Connected).Select(e => e.Streamer)];
        }
        var tries = extras;
        if (mainWanted && !_main.GetStatus().Connected) tries = [_main, .. extras];
        if (tries.Count == 0) return 0;
        lock (_gate) _lastTry = now;
        foreach (var s in tries) s.Start(handle);   // connecting can take a moment; this runs off the audio thread
        return tries.Count;
    }

    public void SetTitle(string title)
    {
        _main.SetTitle(title);
        lock (_gate) foreach (var (t, s) in _extra) if (t.Enabled) s.SetTitle(title);
    }

    /// <summary>The main stream, as before; <see cref="AllStatus"/> has every server.</summary>
    public StreamStatus GetStatus() => _main.GetStatus();

    /// <summary>True when any stream (main or extra) is connected.</summary>
    public bool AnyConnected => _main.GetStatus().Connected || AllStatus().Any(s => s.Connected);

    /// <summary>The extra servers and their state.</summary>
    public IReadOnlyList<StreamTargetStatus> AllStatus()
    {
        lock (_gate)
            return [.. _extra.Select(e =>
            {
                var st = e.Streamer.GetStatus();
                return new StreamTargetStatus(e.Target.Name, e.Target.Server, e.Target.Bitrate, e.Target.Enabled, st.Connected, st.BytesSent, st.Error);
            })];
    }

    public void Dispose()
    {
        Stop();
        lock (_gate) foreach (var (_, s) in _extra) s.Dispose();
    }
}
