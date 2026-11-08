using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// Talks to the engine service: commands over REST, live state over the WebSocket (/api/ws, five times a second).
/// Reconnects by itself when the engine restarts.
/// </summary>
public sealed class EngineClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public EngineClient(Uri baseUri)
    {
        BaseUri = baseUri;
        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
    }

    public Uri BaseUri { get; }

    /// <summary>Raised on a background thread for every state frame.</summary>
    public event Action<StateDto>? StateReceived;
    /// <summary>Raised on a background thread: connected yes/no and a short text.</summary>
    public event Action<bool, string>? ConnectionChanged;
    /// <summary>Raised when a command fails (text for the status bar).</summary>
    public event Action<string>? Error;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        var wsUri = new UriBuilder(BaseUri) { Scheme = BaseUri.Scheme == "https" ? "wss" : "ws", Path = "/api/ws" }.Uri;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(wsUri, ct);
                ConnectionChanged?.Invoke(true, $"verbonden met {BaseUri.Authority}");
                var buffer = new byte[64 * 1024];
                using var message = new MemoryStream();
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    var state = JsonSerializer.Deserialize<StateDto>(message.GetBuffer().AsSpan(0, (int)message.Length), Wire.Json);
                    message.SetLength(0);
                    if (state is not null) StateReceived?.Invoke(state);
                }
                ConnectionChanged?.Invoke(false, "verbinding verbroken");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or JsonException or IOException)
            {
                ConnectionChanged?.Invoke(false, $"geen engine op {BaseUri.Authority}");
            }
            try { await Task.Delay(1500, ct); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        try { return await _http.GetFromJsonAsync<T>(path, Wire.Json); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Error?.Invoke($"{path}: {ex.Message}"); return default; }
    }

    public Task<bool> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body);
    public Task<bool> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body);
    public Task<bool> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    public async Task<T?> SendForAsync<T>(HttpMethod method, string path, object? body)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Wire.Json) };
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) { Error?.Invoke($"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}"); return default; }
            return await response.Content.ReadFromJsonAsync<T>(Wire.Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Error?.Invoke($"{path}: {ex.Message}"); return default; }
    }

    private async Task<bool> SendAsync(HttpMethod method, string path, object? body)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Wire.Json) };
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return true;
            Error?.Invoke($"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Error?.Invoke($"{path}: {ex.Message}"); return false; }
    }

    public async Task<byte[]?> GetBytesAsync(string path)
    {
        try { return await _http.GetByteArrayAsync(path); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Error?.Invoke($"{path}: {ex.Message}"); return null; }
    }

    /// <summary>Starts the engine executable (when it runs on this computer and is not running yet).</summary>
    public static string? StartLocalEngine(StudioSettings settings)
    {
        var exe = settings.EnginePath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) exe = FindEngine();
        if (exe is null) return "REA-Radio-DJ.exe niet gevonden; stel het pad in bij Instellingen.";
        var dir = string.IsNullOrWhiteSpace(settings.EngineWorkingDirectory) ? Path.GetDirectoryName(exe)! : settings.EngineWorkingDirectory;
        Process.Start(new ProcessStartInfo(exe, $"--urls {settings.EffectiveEngineUrl}") { WorkingDirectory = dir, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        return null;
    }

    private static string? FindEngine()
    {
        var here = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var candidates = new List<string>
        {
            Path.Combine(here.FullName, "REA-Radio-DJ.exe"),
            Path.Combine(here.FullName, "..", "engine", "REA-Radio-DJ.exe"),
        };
        // Development layout: src/ReaRadioDj.Studio/bin/<cfg>/<tfm>/ -> src/ReaRadioDj.Server/bin/<cfg>/<tfm>/
        if (here.Parent?.Parent?.Parent?.Parent is { } src)
            candidates.Add(Path.Combine(src.FullName, "ReaRadioDj.Server", "bin", here.Parent.Name, here.Name, "REA-Radio-DJ.exe"));
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}
