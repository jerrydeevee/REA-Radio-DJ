using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A track waiting in the Soloist queue.</summary>
public sealed record SoloistQueueRow(string Number, string Title, string Artist, string Source);

/// <summary>
/// Developer test of Spotify Soloist: controls a Soloist that the user runs themselves (soloist.exe with their own API key
/// and --ws 127.0.0.1:port) over its local WebSocket API. The sound goes from Soloist straight to Windows, never through
/// the engine, the DJ desk, the stream or a recording: Spotify does not allow broadcasting. Only loopback addresses are
/// accepted, because the Soloist WebSocket has no authentication of its own.
/// </summary>
public sealed partial class SoloistTool : StudioTool
{
    public const string DownloadsUrl = "https://developer.spotify.com/documentation/soloist/reference/downloads-and-updates";
    private static readonly HttpClient Images = new() { Timeout = TimeSpan.FromSeconds(10) };

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _send = new(1, 1);
    private string _coverUrl = "";
    private double _positionMs, _speed;
    private long _timestampMs;
    private DispatcherTimer? _clock;
    private DateTime _volumeSentAt;
    private bool _showingVolume;

    static SoloistTool() => Images.DefaultRequestHeaders.UserAgent.ParseAdd("REA-Radio-DJ/0.1 (+https://github.com/jerrydeevee/REA-Radio-DJ)");

    public SoloistTool() : base("soloist", "Soloist (ontwikkeltest)") { }

    public ObservableCollection<SoloistQueueRow> Queue { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    public string StartCommand => "soloist.exe --device-name \"REA ontwikkeltest\" --api-key %SOLOIST_API_KEY% --ws " + Address.Trim();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StartCommand))] private string _address = "127.0.0.1:9090";
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private bool _connecting;
    [ObservableProperty] private string _connectText = "Verbinden";
    [ObservableProperty] private string _state = "Niet verbonden.";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateNeutral))] private bool _stateOk;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateNeutral))] private bool _stateBad;
    public bool StateNeutral => !StateOk && !StateBad;
    [ObservableProperty] private bool _loggedIn;
    [ObservableProperty] private string _title = "Niets aan het spelen";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private string _context = "";
    [ObservableProperty] private string _status = "idle";
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private double _durationMs;
    [ObservableProperty] private double _volume = 50;
    [ObservableProperty] private bool _shuffle;
    [ObservableProperty] private string _link = "";
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _cover;
    [ObservableProperty] private bool _queueEmpty = true;

    public bool Playing => Status is "playing" or "buffering";
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(Playing));

    partial void OnVolumeChanged(double value)
    {
        if (_showingVolume || !LoggedIn) return;
        // A slider sends many values: at most one command per 150 ms, the last one always arrives through the state.
        if (DateTime.UtcNow - _volumeSentAt < TimeSpan.FromMilliseconds(150)) return;
        _volumeSentAt = DateTime.UtcNow;
        _ = Command(new JsonObject { ["command"] = "set_volume", ["volume"] = (int)Math.Round(Math.Clamp(value, 0, 100)) });
    }

    private void Note(string text)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (Log.Count > 60) Log.RemoveAt(Log.Count - 1);
    }

    private void SetState(string text, bool ok = false, bool bad = false) { State = text; StateOk = ok; StateBad = bad; }

    /// <summary>"127.0.0.1:9090", "localhost:9090" or "ws://127.0.0.1:9090" → a WebSocket address on this computer, or null.</summary>
    public static Uri? ParseAddress(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "ws://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var u) || u.Scheme != "ws" || u.IsDefaultPort) return null; // a port is required: Soloist picks its own
        return u.IsLoopback ? u : null;
    }

    /// <summary>A Spotify link (open.spotify.com/track/…) or URI (spotify:album:…) → its URI and kind, or null.</summary>
    public static (string Uri, string Kind)? ParseSpotify(string text)
    {
        var m = Regex.Match(text.Trim(), @"(track|album|playlist|artist|episode|show)[/:]([A-Za-z0-9]{10,})");
        return m.Success ? ($"spotify:{m.Groups[1].Value}:{m.Groups[2].Value}", m.Groups[1].Value) : null;
    }

    [RelayCommand]
    private async Task Connect()
    {
        if (Connected || Connecting) { Disconnect(); return; }
        if (ParseAddress(Address) is not { } uri)
        {
            SetState("Gebruik een adres op deze computer, bijv. 127.0.0.1:9090 (de Soloist-WebSocket heeft geen beveiliging).", bad: true);
            return;
        }
        Connecting = true;
        ConnectText = "Stoppen";
        SetState($"verbinden met {uri}…");
        var cts = new CancellationTokenSource();
        var socket = new ClientWebSocket();
        _cts = cts;
        _socket = socket;
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await socket.ConnectAsync(uri, timeout.Token);
            }
            Connected = true;
            Connecting = false;
            ConnectText = "Verbreken";
            SetState("Verbonden met Soloist; wachten op de aanmelding…");
            Note($"verbonden met {uri}");
            _clock ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
            _clock.Start();
            _ = ReceiveLoop(socket, cts.Token);
            await Command(new JsonObject { ["command"] = "get_auth_state" });
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or HttpRequestException)
        {
            Disconnect();
            SetState($"Geen Soloist op {uri}: draait soloist.exe met --ws {uri.Authority}?", bad: true);
            Note("verbinden: " + ex.Message);
        }
    }

    private void Disconnect()
    {
        _cts?.Cancel();
        try { _socket?.Abort(); } catch (ObjectDisposedException) { }
        _socket?.Dispose();
        _socket = null;
        _cts = null;
        _clock?.Stop();
        Connected = Connecting = LoggedIn = false;
        ConnectText = "Verbinden";
        if (!StateBad) SetState("Niet verbonden.");
    }

    private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var r = await socket.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;
                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                if (r.MessageType == WebSocketMessageType.Text)
                    Dispatcher.UIThread.Post(() => Handle(text));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        Dispatcher.UIThread.Post(() =>
        {
            if (_socket != socket) return;
            Disconnect();
            SetState("Soloist heeft de verbinding verbroken.", bad: true);
            Note("verbinding verbroken");
        });
    }

    private async Task Command(JsonObject body)
    {
        if (_socket is not { State: WebSocketState.Open } socket) return;
        body["type"] = "command";
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        await _send.WaitAsync();
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? default); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { Note("versturen: " + ex.Message); }
        finally { _send.Release(); }
    }

    private static string Name(JsonNode? entity) => entity?["decorations"]?["identity"]?["name"]?.GetValue<string>() ?? "";

    /// <summary>One event from Soloist (see the WebSocket API reference).</summary>
    public void Handle(string text)
    {
        JsonNode? e;
        try { e = JsonNode.Parse(text); }
        catch (JsonException) { Note("onleesbaar bericht"); return; }
        string type = e?["type"]?.GetValue<string>() ?? "";
        switch (type)
        {
            case "auth_state":
                LoggedIn = e!["logged_in"]?.GetValue<bool>() == true;
                string device = e["device_name"]?.GetValue<string>() ?? "";
                if (LoggedIn)
                {
                    SetState($"Aangemeld: apparaat \"{device}\".", ok: true);
                    _ = Command(new JsonObject { ["command"] = "get_state" });
                    _ = Command(new JsonObject { ["command"] = "get_queue", ["limit"] = 30 });
                }
                else SetState($"Soloist draait (\"{device}\") maar is nog niet gekoppeld: kies dit apparaat in de Spotify-app op hetzelfde netwerk.", bad: true);
                Note(LoggedIn ? "aangemeld" : "nog niet gekoppeld");
                break;
            case "playback_state":
                ShowPlayback(e!);
                break;
            case "track_changed":
                ShowItem(e!["item"]);
                break;
            case "queue_changed":
                ShowQueue(e!);
                break;
            case "command_result":
                break;
            case "error":
                Note("Soloist: " + (e!["message"]?.GetValue<string>() ?? "fout"));
                break;
            case "playback_changed" or "volume_changed" or "options_changed" or "position_sync" or "context_changed" or "device_changed":
                // These carry part of the state; asking for the whole state keeps one place that reads it.
                _ = Command(new JsonObject { ["command"] = "get_state" });
                break;
            default:
                Note("bericht: " + type);
                break;
        }
    }

    private void ShowPlayback(JsonNode s)
    {
        Status = s["status"]?.GetValue<string>() ?? "idle";
        ShowItem(s["item"]);
        Context = Name(s["context"]);
        if (s["position"] is { } p)
        {
            _positionMs = p["position_ms"]?.GetValue<double>() ?? 0;
            _timestampMs = p["timestamp_ms"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _speed = p["speed"]?.GetValue<double>() ?? 0;
        }
        if (s["volume"] is { } v)
        {
            _showingVolume = true;
            Volume = v.GetValue<double>();
            _showingVolume = false;
        }
        Shuffle = s["options"]?["shuffle"]?.GetValue<bool>() == true;
        Tick();
    }

    private void ShowItem(JsonNode? item)
    {
        if (item is null) { Title = "Niets aan het spelen"; Artist = Album = ""; DurationMs = 0; Cover = null; _coverUrl = ""; return; }
        Title = Name(item);
        var d = item["decorations"];
        Artist = d?["creators"] is JsonArray creators
            ? string.Join(", ", creators.Select(c => Name(c?["entity"])).Where(n => n.Length > 0))
            : "";
        Album = Name(d?["parent"]?["entity"]);
        DurationMs = d?["playback"]?["duration_ms"]?.GetValue<double>() ?? 0;
        if (d?["visual_identity"]?["cover"] is JsonArray covers && covers.Count > 0)
        {
            var pick = covers.FirstOrDefault(c => c?["size"]?.GetValue<string>() == "default")
                ?? covers.FirstOrDefault(c => c?["size"]?.GetValue<string>() == "large") ?? covers[0];
            string url = pick?["url"]?.GetValue<string>() ?? "";
            if (url != _coverUrl) { _coverUrl = url; _ = LoadCover(url); }
        }
    }

    private async Task LoadCover(string url)
    {
        // Only Spotify's own image host: the address comes from Soloist, not from the user.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.Host.EndsWith(".scdn.co", StringComparison.OrdinalIgnoreCase))
        { Cover = null; return; }
        try
        {
            var bytes = await Images.GetByteArrayAsync(u);
            if (url != _coverUrl) return;
            Cover = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(new MemoryStream(bytes), 160);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException) { Cover = null; }
    }

    private void ShowQueue(JsonNode q)
    {
        Queue.Clear();
        if (q["upcoming"] is JsonArray up)
        {
            int n = 0;
            foreach (var entry in up)
            {
                var item = entry?["item"];
                string source = entry?["source"]?.GetValue<string>() switch { "queue" => "wachtrij", "autoplay" => "autoplay", _ => "" };
                string artist = item?["decorations"]?["creators"] is JsonArray c ? string.Join(", ", c.Select(x => Name(x?["entity"])).Where(x => x.Length > 0)) : "";
                string title = Name(item);
                Queue.Add(new SoloistQueueRow($"{++n}.", title.Length > 0 ? title : item?["uri"]?.GetValue<string>() ?? "?", artist, source));
            }
        }
        QueueEmpty = Queue.Count == 0;
    }

    private void Tick()
    {
        if (DurationMs <= 0) { TimeText = ""; Progress = 0; return; }
        double pos = _positionMs;
        if (_speed > 0 && _timestampMs > 0) pos += (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _timestampMs) * _speed;
        pos = Math.Clamp(pos, 0, DurationMs);
        static string T(double ms) => TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss");
        string state = Status switch { "paused" => " · gepauzeerd", "buffering" => " · laden…", _ => "" };
        TimeText = $"{T(pos)} / {T(DurationMs)}{state}";
        Progress = pos / DurationMs * 100;
    }

    [RelayCommand] private Task PlayPause() => Command(new JsonObject { ["command"] = Playing ? "pause" : "play" });
    [RelayCommand] private Task Next() => Command(new JsonObject { ["command"] = "skip_next" });
    [RelayCommand] private Task Previous() => Command(new JsonObject { ["command"] = "skip_prev" });
    [RelayCommand] private Task ToggleShuffle() => Command(new JsonObject { ["command"] = "set_shuffle", ["enabled"] = !Shuffle });
    [RelayCommand] private Task Refresh() => Task.WhenAll(Command(new JsonObject { ["command"] = "get_state" }), Command(new JsonObject { ["command"] = "get_queue", ["limit"] = 30 }));

    [RelayCommand]
    private Task Jump(string? seconds)
    {
        if (!double.TryParse(seconds, System.Globalization.CultureInfo.InvariantCulture, out var s) || DurationMs <= 0) return Task.CompletedTask;
        double pos = _positionMs + (_speed > 0 ? (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _timestampMs) * _speed : 0);
        return Command(new JsonObject { ["command"] = "seek", ["position_ms"] = (long)Math.Clamp(pos + s * 1000, 0, DurationMs) });
    }

    [RelayCommand]
    private Task PlayUri()
    {
        if (ParseSpotify(Link) is not { } t) { Note("geen Spotify-link of URI herkend"); return Task.CompletedTask; }
        Note("afspelen: " + t.Uri);
        return Command(new JsonObject { ["command"] = "play", ["uri"] = t.Uri });
    }

    [RelayCommand]
    private async Task QueueUri()
    {
        if (ParseSpotify(Link) is not { } t) { Note("geen Spotify-link of URI herkend"); return; }
        if (t.Kind != "track") { Note("alleen losse nummers kunnen in de wachtrij"); return; }
        Note("in de wachtrij: " + t.Uri);
        await Command(new JsonObject { ["command"] = "add_to_queue", ["uri"] = t.Uri });
        await Command(new JsonObject { ["command"] = "get_queue", ["limit"] = 30 });
    }

    [RelayCommand]
    private static void OpenDownloads()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DownloadsUrl) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
