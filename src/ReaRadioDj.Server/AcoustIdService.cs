using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>
/// Recognises songs by their sound with AcoustID (acoustid.org): fpcalc (Chromaprint, fetched by
/// scripts/fetch-fpcalc.ps1) makes a fingerprint of the first two minutes, AcoustID returns the MusicBrainz recordings
/// it belongs to. Used by the metadata lookup for songs whose tags do not lead to a match. The application key is set
/// in the studio (Instellingen → AcoustID) and kept in the station database; it is never sent back to the studio.
/// </summary>
public sealed partial class AcoustIdService(StationDb db, IHttpClientFactory http, ILogger<AcoustIdService> logger)
{
    public const string SettingKey = "acoustid";
    public const string RegisterUrl = "https://acoustid.org/new-application";
    private const string UserAgent = "REA-Radio-DJ/0.1 ( https://github.com/jerrydeevee/REA-Radio-DJ )";
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(350); // AcoustID allows three requests a second
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastCall = DateTime.MinValue;
    private string? _key;

    private sealed record Stored(string ApiKey);

    public string ApiKey
    {
        get
        {
            if (_key is not null) return _key;
            try { _key = db.GetSetting(SettingKey) is { } json ? JsonSerializer.Deserialize<Stored>(json, JsonSerializerOptions.Web)?.ApiKey ?? "" : ""; }
            catch (JsonException) { _key = ""; }
            return _key;
        }
    }

    public void SetKey(string key)
    {
        _key = key.Trim();
        db.SetSetting(SettingKey, JsonSerializer.Serialize(new Stored(_key), JsonSerializerOptions.Web));
    }

    /// <summary>fpcalc.exe next to the engine (copied there by the build from /native).</summary>
    public static string FpcalcPath => Path.Combine(AppContext.BaseDirectory, "fpcalc.exe");
    public static bool FpcalcFound => File.Exists(FpcalcPath);

    /// <summary>Both the key and fpcalc are there.</summary>
    public bool Usable => ApiKey.Length > 0 && FpcalcFound;

    [GeneratedRegex("^[A-Za-z0-9]{6,40}$")] public static partial Regex KeyShape();

    public AcoustIdSettingsDto Dto() => new(ApiKey.Length > 0, ApiKey.Length > 4 ? "…" + ApiKey[^3..] : "", FpcalcFound, Usable, RegisterUrl);

    /// <summary>Fingerprint of an audio file: its length in seconds and the compressed fingerprint. Null when it failed.</summary>
    public async Task<(double Duration, string Fingerprint)?> FingerprintAsync(string path, CancellationToken ct)
    {
        if (!FpcalcFound || !File.Exists(path)) return null;
        var psi = new ProcessStartInfo(FpcalcPath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-json", "-length", "120", path }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var outTask = p.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            return MetadataMatch.ParseFingerprint(await outTask);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested) throw;
            logger.LogInformation("fpcalc took too long for {File}", path);
            return null;
        }
    }

    /// <summary>The recordings AcoustID knows for a fingerprint.</summary>
    public async Task<IReadOnlyList<MetaCandidate>> LookupAsync(double duration, string fingerprint, CancellationToken ct) =>
        MetadataMatch.ParseAcoustId(await PostAsync(duration, fingerprint, ct));

    private async Task<string> PostAsync(double duration, string fingerprint, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastCall + Pace - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCall = DateTime.UtcNow;
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.acoustid.org/v2/lookup")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client"] = ApiKey, ["format"] = "json", ["meta"] = "recordings releasegroups compress",
                    ["duration"] = ((int)Math.Round(duration)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["fingerprint"] = fingerprint,
                }),
            };
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var res = await http.CreateClient("acoustid").SendAsync(req, ct);
            return await res.Content.ReadAsStringAsync(ct); // errors come back as JSON with status "error"
        }
        finally { _gate.Release(); }
    }

    /// <summary>Checks the key: a lookup with an empty fingerprint is refused for a wrong key, and for a right one only for the fingerprint.</summary>
    public async Task<AcoustIdTestDto> TestAsync(CancellationToken ct)
    {
        if (ApiKey.Length == 0) return new(false, "Vul eerst de API-sleutel van je AcoustID-applicatie in.");
        try
        {
            var json = await PostAsync(1, "", ct);
            var error = MetadataMatch.AcoustIdError(json);
            if (error is { Code: 4 }) return new(false, "AcoustID kent deze sleutel niet: kopieer de API Key van je applicatie opnieuw.");
            string fp = FpcalcFound ? "" : " Let op: fpcalc ontbreekt nog (scripts\\fetch-fpcalc.ps1), zonder kan de engine geen vingerafdruk maken.";
            return new(true, "AcoustID accepteert de sleutel." + fp);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(false, ex is TaskCanceledException ? "AcoustID antwoordt niet (time-out)." : $"Niet verbonden: {ex.Message}");
        }
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/acoustid/settings", (AcoustIdService s) => s.Dto());
        api.MapPut("/acoustid/settings", (AcoustIdSettingsRequest r, AcoustIdService s) =>
        {
            if (r.Clear) { s.SetKey(""); return Results.Ok(s.Dto()); }
            string key = r.ApiKey?.Trim() ?? "";
            if (key.Length == 0) return Results.Ok(s.Dto()); // empty keeps the stored key
            if (!KeyShape().IsMatch(key)) return Results.BadRequest("De API-sleutel bestaat uit letters en cijfers; kopieer hem uit 'Your applications' op acoustid.org.");
            s.SetKey(key);
            return Results.Ok(s.Dto());
        });
        api.MapPost("/acoustid/test", (AcoustIdService s, CancellationToken ct) => s.TestAsync(ct));
    }
}
