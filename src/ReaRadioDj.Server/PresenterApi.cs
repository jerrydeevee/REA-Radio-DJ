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
