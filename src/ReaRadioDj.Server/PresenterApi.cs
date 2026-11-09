using System.Diagnostics;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>AI presenter settings made in the studio; stored in the database and winning over appsettings.json.</summary>
public sealed record PresenterSettings(bool Enabled, string PresenterName, string Persona, int MaxWords, string LlmBaseUrl,
    string LlmModel, string LlmApiKey, double Temperature, bool RequireApproval, bool PostProcess, long? BedCategoryId = null)
{
    public const string Key = "presenter";
    public const string Mask = "••••••";

    public static PresenterSettings From(ReaRadioDjOptions o)
    {
        var p = o.Presenter;
        return new(p.Enabled, p.PresenterName, p.Persona, p.MaxWords, p.Llm.BaseUrl, p.Llm.Model, p.Llm.ApiKey, p.Llm.Temperature,
            p.RequireApproval, p.PostProcess, p.BedCategoryId);
    }

    public void ApplyTo(ReaRadioDjOptions o)
    {
        var p = o.Presenter;
        p.Enabled = Enabled;
        if (!string.IsNullOrWhiteSpace(PresenterName)) p.PresenterName = PresenterName.Trim();
        if (!string.IsNullOrWhiteSpace(Persona)) p.Persona = Persona.Trim();
        p.MaxWords = Math.Clamp(MaxWords, 10, 150);
        p.Llm.BaseUrl = LlmBaseUrl.Trim();
        p.Llm.Model = LlmModel.Trim();
        p.Llm.ApiKey = LlmApiKey.Trim();
        p.Llm.Temperature = Math.Clamp(Temperature, 0, 1.5);
        p.RequireApproval = RequireApproval;
        p.PostProcess = PostProcess;
        p.BedCategoryId = BedCategoryId is > 0 ? BedCategoryId : null;
    }

    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        if (db.GetSetting(Key) is not { } json) return;
        try { JsonSerializer.Deserialize<PresenterSettings>(json, JsonSerializerOptions.Web)?.ApplyTo(o); }
        catch (JsonException) { }
    }

    public void Save(StationDb db) => db.SetSetting(Key, JsonSerializer.Serialize(this, JsonSerializerOptions.Web));

    public PresenterSettingsDto Dto() => new(Enabled, PresenterName, Persona, MaxWords, LlmBaseUrl, LlmModel,
        LlmApiKey.Length > 0 ? Mask : "", Temperature, RequireApproval, PostProcess, BedCategoryId);
}

/// <summary>Weather, news and outro settings made in the studio.</summary>
public sealed record PresenterExtrasSettings(long? OutroCategoryId, bool WeatherEnabled, string Place, double Latitude, double Longitude,
    int WeatherEveryMinutes, bool NewsEnabled, string NewsFeeds, int NewsEveryMinutes)
{
    public const string Key = "presenter-extras";

    public static PresenterExtrasSettings From(ReaRadioDjOptions o)
    {
        var p = o.Presenter;
        return new(p.OutroCategoryId, p.Weather.Enabled, p.Weather.Place, p.Weather.Latitude, p.Weather.Longitude, p.Weather.EveryMinutes,
            p.News.Enabled, string.Join("\n", p.News.Feeds), p.News.EveryMinutes);
    }

    public void ApplyTo(ReaRadioDjOptions o)
    {
        var p = o.Presenter;
        p.OutroCategoryId = OutroCategoryId is > 0 ? OutroCategoryId : null;
        p.Weather.Enabled = WeatherEnabled;
        p.Weather.Place = Place.Trim();
        p.Weather.Latitude = Math.Clamp(Latitude, -90, 90);
        p.Weather.Longitude = Math.Clamp(Longitude, -180, 180);
        p.Weather.EveryMinutes = Math.Clamp(WeatherEveryMinutes, 5, 24 * 60);
        p.News.Enabled = NewsEnabled;
        p.News.Feeds = NewsFeeds.Split(['\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => Uri.TryCreate(f, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")).Distinct().ToList();
        p.News.EveryMinutes = Math.Clamp(NewsEveryMinutes, 5, 24 * 60);
    }

    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        if (db.GetSetting(Key) is not { } json) return;
        try { JsonSerializer.Deserialize<PresenterExtrasSettings>(json, JsonSerializerOptions.Web)?.ApplyTo(o); }
        catch (JsonException) { }
    }

    public void Save(StationDb db) => db.SetSetting(Key, JsonSerializer.Serialize(this, JsonSerializerOptions.Web));

    public PresenterExtrasDto Dto() => new(OutroCategoryId, WeatherEnabled, Place, Latitude, Longitude, WeatherEveryMinutes, NewsEnabled,
        NewsFeeds, NewsEveryMinutes);
}

/// <summary>Keys of online voices and the speech check, set in the studio (keys are never sent back unmasked).</summary>
public sealed record VoiceServicesSettings(string ElevenLabsKey, string ElevenLabsModel, string AzureKey, string AzureRegion, bool CheckEnabled,
    string CheckUrl, string CheckModel, double CheckMinScore, int CheckRetries, bool CheckReject)
{
    public const string Key = "voice-services";

    public static VoiceServicesSettings From(ReaRadioDjOptions o)
    {
        var t = o.Presenter.Tts;
        var c = o.Presenter.Check;
        return new(t.ElevenLabsKey, t.ElevenLabsModel, t.AzureKey, t.AzureRegion, c.Enabled, c.BaseUrl, c.Model, c.MinScore, c.Retries, c.Reject);
    }

    public void ApplyTo(ReaRadioDjOptions o)
    {
        var t = o.Presenter.Tts;
        var c = o.Presenter.Check;
        t.ElevenLabsKey = ElevenLabsKey.Trim();
        if (!string.IsNullOrWhiteSpace(ElevenLabsModel)) t.ElevenLabsModel = ElevenLabsModel.Trim();
        t.AzureKey = AzureKey.Trim();
        if (!string.IsNullOrWhiteSpace(AzureRegion)) t.AzureRegion = AzureRegion.Trim().ToLowerInvariant();
        c.Enabled = CheckEnabled;
        if (!string.IsNullOrWhiteSpace(CheckUrl)) c.BaseUrl = CheckUrl.Trim();
        if (!string.IsNullOrWhiteSpace(CheckModel)) c.Model = CheckModel.Trim();
        c.MinScore = Math.Clamp(CheckMinScore, 0.3, 1);
        c.Retries = Math.Clamp(CheckRetries, 0, 3);
        c.Reject = CheckReject;
    }

    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        if (db.GetSetting(Key) is not { } json) return;
        try { JsonSerializer.Deserialize<VoiceServicesSettings>(json, JsonSerializerOptions.Web)?.ApplyTo(o); }
        catch (JsonException) { }
    }

    public void Save(StationDb db) => db.SetSetting(Key, JsonSerializer.Serialize(this, JsonSerializerOptions.Web));

    public VoiceServicesDto Dto() => new(ElevenLabsKey.Length > 0 ? PresenterSettings.Mask : "", ElevenLabsModel,
        AzureKey.Length > 0 ? PresenterSettings.Mask : "", AzureRegion, CheckEnabled, CheckUrl, CheckModel, CheckMinScore, CheckRetries, CheckReject);
}

public static class PresenterApi
{
    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        var p = api.MapGroup("/presenter");
        p.MapGet("/settings", () => PresenterSettings.From(options).Dto());
        p.MapPut("/settings", (PresenterSettingsDto d, StationDb db, MessageCenter messages) =>
        {
            var old = PresenterSettings.From(options);
            var s = new PresenterSettings(d.Enabled, d.PresenterName, d.Persona, d.MaxWords, d.LlmBaseUrl, d.LlmModel,
                d.LlmApiKey == PresenterSettings.Mask ? old.LlmApiKey : d.LlmApiKey, d.Temperature, d.RequireApproval, d.PostProcess,
                d.BedCategoryId);
            s.ApplyTo(options);
            PresenterSettings.From(options).Save(db);
            messages.Info("AI-presentator: instellingen opgeslagen");
            return PresenterSettings.From(options).Dto();
        });

        // Models of the language-model server the studio is about to use (the url may not be saved yet).
        p.MapGet("/models", async (string? url, IHttpClientFactory http, CancellationToken ct) =>
        {
            var baseUrl = string.IsNullOrWhiteSpace(url) ? options.Presenter.Llm.BaseUrl : url;
            if (string.IsNullOrWhiteSpace(baseUrl)) return new LlmModelsDto(false, [], "geen adres ingevuld");
            try
            {
                return new LlmModelsDto(true, await OpenAiScriptWriter.ListModelsAsync(http.CreateClient("ai"), baseUrl, options.Presenter.Llm.ApiKey, ct), null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
            {
                return new LlmModelsDto(false, [], $"niet bereikbaar: {ex.Message}");
            }
        });

        // A trial script for the next two songs in the log: shows what the model writes with the current settings.
        p.MapPost("/try", async (ScriptTryRequest? r, PlayoutLog log, BreakRenderer breaks, PersonaStore personas, CancellationToken ct) =>
        {
            var persona = r?.PersonaId is long pid ? personas.All().FirstOrDefault(x => x.Id == pid) : null;
            var songs = log.Upcoming.Where(i => i.Type == LogItemType.Song && i.Media is not null).Select(i => i.Media).Take(2).ToList();
            var sw = Stopwatch.StartNew();
            var (script, source) = await breaks.TryScriptAsync(songs.ElementAtOrDefault(0), songs.ElementAtOrDefault(1), ct, persona);
            return new ScriptTryDto(script, source, Math.Round(sw.Elapsed.TotalSeconds, 1),
                script.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, (persona ?? personas.For(DateTime.Now))?.Name);
        });

        p.MapGet("/extras", () => PresenterExtrasSettings.From(options).Dto());
        p.MapPut("/extras", (PresenterExtrasDto d, StationDb db) =>
        {
            new PresenterExtrasSettings(d.OutroCategoryId, d.WeatherEnabled, d.Place ?? "", d.Latitude, d.Longitude, d.WeatherEveryMinutes,
                d.NewsEnabled, d.NewsFeeds ?? "", d.NewsEveryMinutes).ApplyTo(options);
            var saved = PresenterExtrasSettings.From(options);
            saved.Save(db);
            return saved.Dto();
        });
        // Fetches what the presenter would say about the weather and the news now (ignores the "every N minutes" pause).
        p.MapGet("/extras/preview", async (IHttpClientFactory http, CancellationToken ct) =>
        {
            var probe = new PresenterOptions
            {
                Weather = { Enabled = options.Presenter.Weather.Enabled, Place = options.Presenter.Weather.Place,
                    Latitude = options.Presenter.Weather.Latitude, Longitude = options.Presenter.Weather.Longitude, EveryMinutes = 5 },
                News = { Enabled = false },
            };
            var extras = new BreakExtras(http.CreateClient("extras"), probe, null);
            string? weather = null, headline = null, error = null;
            if (probe.Weather.Enabled) weather = (await extras.PickAsync(DateTime.Now, ct))?.Spoken ?? "weer niet opgehaald (zie log)";
            if (options.Presenter.News.Enabled && options.Presenter.News.Feeds.Count > 0)
            {
                var newsOnly = new PresenterOptions { News = { Enabled = true, Feeds = options.Presenter.News.Feeds, EveryMinutes = 5 } };
                headline = (await new BreakExtras(http.CreateClient("extras"), newsOnly, null).PickAsync(DateTime.Now, ct))?.Spoken
                    ?? "geen kop gevonden (zie log)";
            }
            if (weather is null && headline is null) error = "weer en nieuws staan uit";
            return new ExtrasPreviewDto(weather, headline, error);
        });

        api.MapGet("/voice/services", () => VoiceServicesSettings.From(options).Dto());
        api.MapPut("/voice/services", (VoiceServicesDto d, StationDb db) =>
        {
            var old = VoiceServicesSettings.From(options);
            var s = new VoiceServicesSettings(d.ElevenLabsKey == PresenterSettings.Mask ? old.ElevenLabsKey : d.ElevenLabsKey ?? "",
                d.ElevenLabsModel ?? "", d.AzureKey == PresenterSettings.Mask ? old.AzureKey : d.AzureKey ?? "", d.AzureRegion ?? "",
                d.CheckEnabled, d.CheckUrl ?? "", d.CheckModel ?? "", d.CheckMinScore, d.CheckRetries, d.CheckReject);
            s.ApplyTo(options);
            var saved = VoiceServicesSettings.From(options);
            saved.Save(db);
            return saved.Dto();
        });
        // Speaks a text with the default voice (and its backups) and lets the speech check listen to it.
        api.MapPost("/voice/check-test", async (CheckTestRequest r, ProfileSpeechEngine voice, SpeechCheck check, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(r.Text)) return new CheckTestDto(false, 0, "", "", "lege tekst");
            var wav = Path.Combine(Path.GetTempPath(), $"rea-check-{Guid.NewGuid():N}.wav");
            try
            {
                var used = await voice.SpeakAsync(r.Text, wav, voice.DefaultProfile, ct);
                var result = await check.CheckAsync(wav, r.Text, ct);
                return new CheckTestDto(true, Math.Round(result.Score, 2), result.Heard, used, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                string why = ex is HttpRequestException { StatusCode: null } && ex.Message.Contains(new Uri(options.Presenter.Check.BaseUrl).Authority)
                    ? $"spraakherkenning niet bereikbaar op {options.Presenter.Check.BaseUrl}; start tools/whisper/start.sh in WSL"
                    : ex.Message;
                return new CheckTestDto(false, 0, "", "", why);
            }
            finally { try { File.Delete(wav); } catch (IOException) { } }
        });

        var notes = p.MapGroup("/notes");
        notes.MapGet("", (NoteStore st) => st.All().Select(n => new PresenterNoteDto(n.Id, n.Text, n.Until, n.MaxUses, n.Used)));
        notes.MapPost("", (AddNoteRequest r, NoteStore st) => string.IsNullOrWhiteSpace(r.Text)
            ? Results.BadRequest(new { error = "lege tekst" })
            : Results.Ok(new { id = st.Add(r.Text, r.Until, r.MaxUses <= 0 ? 1 : r.MaxUses) }));
        notes.MapDelete("/{id:long}", (long id, NoteStore st) => st.Delete(id) ? Results.Ok() : Results.NotFound());

        var ps = p.MapGroup("/personas");
        ps.MapGet("", (PersonaStore st) =>
        {
            var now = st.For(DateTime.Now);
            return st.All().Select(x => Dto(x, x.Id == now?.Id));
        });
        ps.MapPost("", (PersonaDto d, PersonaStore st) => Results.Ok(new { id = st.Save(From(d) with { Id = 0 }) }));
        ps.MapPut("/{id:long}", (long id, PersonaDto d, PersonaStore st) => Results.Ok(new { id = st.Save(From(d) with { Id = id }) }));
        ps.MapDelete("/{id:long}", (long id, PersonaStore st) => st.Delete(id) ? Results.Ok() : Results.NotFound());

        api.MapPost("/log/{id:long}/break/approve", async (long id, ApproveBreakRequest? r, PlayoutLog log, BreakRenderer breaks, CancellationToken ct) =>
            log.Find(id) is { Type: LogItemType.AiBreak } item
                ? await breaks.ApproveAsync(item, r?.Script, ct) ? Results.Ok() : Results.BadRequest(new { error = item.Error ?? "niet goedgekeurd" })
                : Results.NotFound());
        api.MapPost("/log/{id:long}/break/rewrite", (long id, PlayoutLog log, BreakRenderer breaks, IHostApplicationLifetime life) =>
        {
            if (log.Find(id) is not { Type: LogItemType.AiBreak } item || item.BreakState == BreakState.Rendering) return Results.NotFound();
            item.BreakState = BreakState.Rendering;
            _ = Task.Run(() => breaks.RenderAsync(item, life.ApplicationStopping));
            return Results.Ok();
        });
    }

    public static PersonaDto Dto(Persona p, bool onAir = false) => new(p.Id, p.Name, p.Enabled, p.Days, p.Hours, p.PresenterName,
        p.Character, p.VoiceId, p.MaxWords, p.BedCategoryId, p.Sort, p.Describe(), onAir);

    public static Persona From(PersonaDto d) => new(d.Id, d.Name ?? "", d.Enabled, d.Days, d.Hours ?? "*", d.PresenterName ?? "",
        d.Character ?? "", d.VoiceId is > 0 ? d.VoiceId : null, d.MaxWords is > 0 ? d.MaxWords : null,
        d.BedCategoryId is > 0 ? d.BedCategoryId : null, d.Sort);
}
