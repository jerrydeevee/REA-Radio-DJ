using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Users;

namespace ReaRadioDj.Server;

/// <summary>
/// Records the own voice of a presenter with the studio microphone (or takes a WAV file), so it can be checked and
/// listened to before it is kept. One recording at a time, and never while voicetracking records.
/// </summary>
public sealed class OwnVoiceRecorder(IVoiceCapture capture, VoiceTracker tracker, ReaRadioDjOptions options)
{
    private readonly object _gate = new();
    private Stopwatch? _clock;
    private string? _recordingFor;
    private (string Owner, byte[] Wav, VoiceClips.ClipCheck Check)? _take;

    public bool Recording { get { lock (_gate) return _clock is not null; } }
    public double Seconds { get { lock (_gate) return _clock?.Elapsed.TotalSeconds ?? 0; } }
    public string? RecordingFor { get { lock (_gate) return _recordingFor; } }

    public void Start(string owner, int? device)
    {
        lock (_gate)
        {
            if (_clock is not null) throw new InvalidOperationException($"er loopt al een stemopname ({_recordingFor})");
            if (tracker.Step != VoiceTrackStep.Idle) throw new InvalidOperationException("voicetracking neemt nu op; probeer het straks");
            capture.Start(device ?? options.Routing.MicDevice);
            _clock = Stopwatch.StartNew();
            _recordingFor = owner;
            if (_take?.Owner == owner) _take = null;
        }
    }

    public VoiceClips.ClipCheck Stop(string owner)
    {
        lock (_gate)
        {
            if (_clock is null) throw new InvalidOperationException("er loopt geen opname");
            if (!Same(_recordingFor, owner)) throw new InvalidOperationException($"{_recordingFor} neemt nu op");
            var samples = capture.Stop();
            _clock = null;
            _recordingFor = null;
            return Keep(owner, VoiceClips.FromRecording(samples, capture.SampleRate));
        }
    }

    /// <summary>A recording made elsewhere (a WAV file).</summary>
    public VoiceClips.ClipCheck Use(string owner, byte[] wav)
    {
        lock (_gate) return Keep(owner, wav);
    }

    private VoiceClips.ClipCheck Keep(string owner, byte[] wav)
    {
        var check = VoiceClips.Check(wav);
        _take = (owner, wav, check);
        return check;
    }

    public (byte[] Wav, VoiceClips.ClipCheck Check)? Take(string owner)
    {
        lock (_gate) return _take is { } t && Same(t.Owner, owner) ? (t.Wav, t.Check) : null;
    }

    public void Discard(string owner)
    {
        lock (_gate) if (_take is { } t && Same(t.Owner, owner)) _take = null;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>How the Higgs server is started: with the engine or by hand, and the command (station settings over appsettings.json).</summary>
public sealed record HiggsServerSettings(bool AutoStart, string BaseUrl, string Command, string Arguments, string WorkingDirectory)
{
    public const string Key = "higgs-server";

    public static HiggsServerSettings From(ReaRadioDjOptions o)
    {
        var t = o.Presenter.Tts;
        return new(t.StartServer, t.BaseUrl, t.ServerCommand, t.ServerArguments, t.ServerWorkingDirectory);
    }

    public void ApplyTo(ReaRadioDjOptions o)
    {
        var t = o.Presenter.Tts;
        t.StartServer = AutoStart;
        if (Uri.TryCreate((BaseUrl ?? "").Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https") t.BaseUrl = url.ToString().TrimEnd('/');
        t.ServerCommand = (Command ?? "").Trim();
        t.ServerArguments = (Arguments ?? "").Trim();
        t.ServerWorkingDirectory = (WorkingDirectory ?? "").Trim();
    }

    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        if (db.GetSetting(Key) is not { } json) return;
        try { JsonSerializer.Deserialize<HiggsServerSettings>(json, JsonSerializerOptions.Web)?.ApplyTo(o); }
        catch (JsonException) { }
    }

    public void Save(StationDb db) => db.SetSetting(Key, JsonSerializer.Serialize(this, JsonSerializerOptions.Web));

    public HiggsServerSettingsDto Dto(bool editable) => new(AutoStart, BaseUrl, Command, Arguments, WorkingDirectory, editable);
}

/// <summary>
/// The voice database around the voice profiles: the roles (presenter, news reader, weather), reference recordings of
/// cloned voices, the own voice of a presenter, and how the Higgs server starts.
/// </summary>
public static class VoiceLibraryApi
{
    /// <summary>What a presenter reads for the own voice: everyday radio sentences, so the clone sounds like the presenter on air.</summary>
    public static string ReadText(string station) =>
        $"Goedemorgen, je luistert naar {(string.IsNullOrWhiteSpace(station) ? "de radio" : station.Trim())}. " +
        "Het is een mooie dag om de radio aan te zetten. Straks hoor je het nieuws en het weer, " +
        "maar eerst nog een nummer dat ik zelf heel graag draai. Blijf erbij, want na de muziek heb ik nog een leuke tip voor het weekend.";

    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        var v = api.MapGroup("/voice");

        // ------------------------------------------------------------ roles
        v.MapGet("/roles", (VoiceStore st, VoiceRoleStore roles) => RolesDto(st, roles));
        v.MapPut("/roles", (VoiceRolesDto d, VoiceStore st, VoiceRoleStore roles, MessageCenter m) =>
        {
            if (d.Presenter is long p && st.Default?.Id != p)
            {
                if (st.Get(p) is not { } profile) return Results.BadRequest("die stem bestaat niet (meer)");
                if (!profile.Allows(VoiceRoles.Presenter)) return Results.BadRequest($"'{profile.Name}' mag niet als presentator spreken");
                st.SetDefault(p);
                m.Info($"Presentatorstem: {profile.Name}");
            }
            if (roles.Save(new VoiceRoles(d.News, d.Weather)) is { } error) return Results.BadRequest(error);
            return Results.Ok(RolesDto(st, roles));
        });

        // ------------------------------------------------------------ a recording for any voice profile (technician)
        v.MapPost("/profiles/{id:long}/clip", async (long id, HttpRequest request, VoiceStore st, VoiceClips clips) =>
        {
            if (st.Get(id) is not { } p) return Results.NotFound();
            var wav = await ReadBody(request);
            if (wav is null) return Results.BadRequest("het bestand is te groot (maximaal 25 MB)");
            var check = VoiceClips.Check(wav);
            if (check.Problem is not null) return Results.BadRequest(check.Problem);
            var name = clips.Store(id, wav);
            st.Save(p with { ReferenceAudio = name, Engine = "higgs" });
            if (p.ReferenceAudio != name) clips.Delete(p.ReferenceAudio);
            return Results.Ok(new VoiceClipDto(check.Seconds, check.PeakDb, null));
        });
        v.MapGet("/profiles/{id:long}/clip", (long id, VoiceStore st, VoiceClips clips) =>
            st.Get(id) is { } p && clips.PathOf(p.ReferenceAudio) is { } path ? Results.File(path, "audio/wav") : Results.NotFound());

        // ------------------------------------------------------------ the own voice of a presenter
        v.MapGet("/mine", (HttpContext http, string? owner, Own own) =>
        {
            var (who, refusal) = own.Who(http, owner);
            return Results.Ok(own.Dto(who, refusal is not null && own.UsersActive));
        });
        v.MapPost("/mine/record/start", (HttpContext http, OwnVoiceRecordRequest r, Own own) => own.Act(http, r.Owner, who => own.Recorder.Start(who, r.Device)));
        v.MapPost("/mine/record/stop", (HttpContext http, OwnVoiceRecordRequest r, Own own) => own.Act(http, r.Owner, who => own.Recorder.Stop(who)));
        v.MapPost("/mine/upload", async (HttpContext http, string? owner, Own own) =>
        {
            var wav = await ReadBody(http.Request);
            if (wav is null) return Results.BadRequest("het bestand is te groot (maximaal 25 MB)");
            return own.Act(http, owner, who => own.Recorder.Use(who, wav));
        });
        v.MapPost("/mine/take/play", (HttpContext http, string? owner, Own own, VoiceService voices) => own.Act(http, owner, who =>
        {
            if (own.Recorder.Take(who) is not { } take) throw new InvalidOperationException("er is nog geen opname");
            var path = voices.NewTestPath();
            File.WriteAllBytes(path, take.Wav);
            voices.PlayTest(path, $"Stemopname {who}", true);
        }));
        v.MapDelete("/mine/take", (HttpContext http, string? owner, Own own) => own.Act(http, owner, who => own.Recorder.Discard(who)));
        v.MapPost("/mine", (HttpContext http, OwnVoiceSaveRequest r, Own own) => own.Act(http, r.Owner, who => own.Save(who, r)));
        v.MapDelete("/mine", (HttpContext http, string? owner, Own own) => own.Act(http, owner, own.Remove));

        // ------------------------------------------------------------ starting the Higgs server
        v.MapGet("/server/settings", (HttpContext http) => HiggsServerSettings.From(options).Dto(IsLocal(http)));
        v.MapPut("/server/settings", (HttpContext http, HiggsServerSettingsDto d, StationDb db, MessageCenter m) =>
        {
            var old = HiggsServerSettings.From(options);
            bool local = IsLocal(http);
            // The start command runs a program on the studio computer: only changed there, never over the network.
            if (!local && (d.Command != old.Command || d.Arguments != old.Arguments || d.WorkingDirectory != old.WorkingDirectory))
                return Results.Json("Het startcommando kan alleen op de studio-pc zelf worden gewijzigd.", statusCode: StatusCodes.Status403Forbidden);
            new HiggsServerSettings(d.AutoStart, d.BaseUrl, d.Command, d.Arguments, d.WorkingDirectory).ApplyTo(options);
            var saved = HiggsServerSettings.From(options);
            saved.Save(db);
            m.Info(saved.AutoStart ? "Higgs-server start voortaan mee met de engine" : "Higgs-server start voortaan alleen met de hand");
            return Results.Ok(saved.Dto(local));
        });
    }

    private static VoiceRolesDto RolesDto(VoiceStore st, VoiceRoleStore roles) =>
        new(st.Default?.Id, roles.Current.News, roles.Current.Weather);

    private static bool IsLocal(HttpContext http) => http.Connection.RemoteIpAddress is not { } ip || IPAddress.IsLoopback(ip);

    private static async Task<byte[]?> ReadBody(HttpRequest request)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await request.Body.ReadAsync(buffer)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > VoiceClips.MaxBytes) return null;
        }
        return ms.ToArray();
    }
}

/// <summary>The own voice of whoever asks: the signed-in user, or (only when the station has no users) the name given.</summary>
public sealed class Own(OwnVoiceRecorder recorder, VoiceStore store, VoiceClips clips, VoiceRoleStore roles, SessionStore sessions,
    UserStore users, MessageCenter messages, ReaRadioDjOptions options)
{
    public OwnVoiceRecorder Recorder => recorder;
    public bool UsersActive => users.Active;

    public (string? Owner, IResult? Refusal) Who(HttpContext http, string? given)
    {
        if (sessions.Get(http.Request.Headers[UserRoles.SessionHeader].ToString()) is { } s) return (s.Name, null);
        if (users.Active)
            return (null, Results.Json("Meld je eerst aan; je eigen stem wordt aan jouw naam gekoppeld.", statusCode: StatusCodes.Status401Unauthorized));
        var name = (given ?? "").Trim();
        if (name.Length is 0 or > 60) return (null, Results.BadRequest("Vul je naam in (zoals je op de radio heet)."));
        return (name, null);
    }

    public IResult Act(HttpContext http, string? given, Action<string> action)
    {
        var (who, refusal) = Who(http, given);
        if (refusal is not null) return refusal;
        try { action(who!); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or BassException) { return Results.BadRequest(ex.Message); }
        return Results.Ok(Dto(who, false));
    }

    public IResult Act<T>(HttpContext http, string? given, Func<string, T> action) => Act(http, given, who => { action(who); });

    public OwnVoiceDto Dto(string? who, bool signIn)
    {
        var profile = who is null ? null : store.OwnedBy(who);
        var take = who is null ? null : recorder.Take(who);
        bool mine = who is not null && string.Equals(recorder.RecordingFor, who, StringComparison.OrdinalIgnoreCase);
        return new OwnVoiceDto(who, signIn, profile is null ? null : VoiceService.Dto(profile), VoiceLibraryApi.ReadText(options.StationName),
            mine, mine ? Math.Round(recorder.Seconds, 1) : 0,
            take is { } t ? new VoiceClipDto(t.Check.Seconds, t.Check.PeakDb, t.Check.Problem) : null);
    }

    public void Save(string who, OwnVoiceSaveRequest r)
    {
        if (!r.Consent) throw new InvalidOperationException("Zonder jouw toestemming wordt je stem niet vastgelegd.");
        var uses = VoiceRoles.Parse(r.Uses);
        if (uses.Count == 0) throw new InvalidOperationException("Kies waarvoor je stem gebruikt mag worden (presentator, nieuws en/of weer).");
        var text = (r.Text ?? "").Trim();
        if (text.Length < 10) throw new InvalidOperationException("Vul de tekst in die je hebt voorgelezen.");
        var existing = store.OwnedBy(who);
        var take = recorder.Take(who);
        if (take is null && existing is null) throw new InvalidOperationException("Neem eerst je stem op (of kies een WAV-bestand).");
        if (take is { Check.Problem: { } problem }) throw new InvalidOperationException(problem);

        string labels = string.Join(", ", uses.Select(VoiceRoles.Label));
        var note = $"Eigen stem: toestemming gegeven door {who} op {DateTime.Now:d-M-yyyy HH:mm} in de studio (voor: {labels}). " +
                   "In te trekken met 'Mijn stem verwijderen'.";
        long? backup = store.Profiles.FirstOrDefault(p => p.Name == VoiceStore.WindowsPresetName)?.Id;
        var profile = new VoiceProfile(existing?.Id ?? 0, existing?.Name ?? $"{who} (eigen stem)", "higgs", existing?.ReferenceAudio ?? "",
            text, existing?.Tags ?? "", existing?.Temperature ?? 0.7, "", true, note, existing?.IsDefault ?? false,
            existing?.FallbackId ?? backup, who, VoiceRoles.Format(uses));
        long id = store.Save(profile);
        if (take is { } t)
        {
            var name = clips.Store(id, t.Wav);
            store.Save(store.Get(id)! with { ReferenceAudio = name });
            if (existing?.ReferenceAudio is { } old && old != name) clips.Delete(old);
            recorder.Discard(who);
        }
        if (store.Get(id) is { IsDefault: true } saved && !saved.Allows(VoiceRoles.Presenter)) MoveDefaultAway(id);
        messages.Info($"Eigen stem van {who} vastgelegd (voor: {labels})");
    }

    public void Remove(string who)
    {
        if (store.OwnedBy(who) is not { } p) throw new InvalidOperationException("Je hebt geen eigen stem vastgelegd.");
        if (p.IsDefault) MoveDefaultAway(p.Id);
        store.Delete(p.Id);
        clips.Delete(p.ReferenceAudio);
        recorder.Discard(who);
        var r = roles.Current;
        if (r.News == p.Id || r.Weather == p.Id)
            roles.Save(new VoiceRoles(r.News == p.Id ? null : r.News, r.Weather == p.Id ? null : r.Weather));
        messages.Info($"Eigen stem van {who} verwijderd, met de opname");
    }

    /// <summary>The presenter voice goes to a station voice (no owner), so a withdrawn own voice is never left speaking.</summary>
    private void MoveDefaultAway(long id)
    {
        if (store.Profiles.FirstOrDefault(p => p.Id != id && p.Owner.Length == 0) is { } other) store.SetDefault(other.Id);
    }
}
