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
    string LlmModel, string LlmApiKey, double Temperature, bool RequireApproval, bool PostProcess)
{
    public const string Key = "presenter";
    public const string Mask = "••••••";

    public static PresenterSettings From(ReaRadioDjOptions o)
    {
        var p = o.Presenter;
        return new(p.Enabled, p.PresenterName, p.Persona, p.MaxWords, p.Llm.BaseUrl, p.Llm.Model, p.Llm.ApiKey, p.Llm.Temperature,
            p.RequireApproval, p.PostProcess);
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
    }

    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        if (db.GetSetting(Key) is not { } json) return;
        try { JsonSerializer.Deserialize<PresenterSettings>(json, JsonSerializerOptions.Web)?.ApplyTo(o); }
        catch (JsonException) { }
    }

    public void Save(StationDb db) => db.SetSetting(Key, JsonSerializer.Serialize(this, JsonSerializerOptions.Web));

    public PresenterSettingsDto Dto() => new(Enabled, PresenterName, Persona, MaxWords, LlmBaseUrl, LlmModel,
        LlmApiKey.Length > 0 ? Mask : "", Temperature, RequireApproval, PostProcess);
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
                d.LlmApiKey == PresenterSettings.Mask ? old.LlmApiKey : d.LlmApiKey, d.Temperature, d.RequireApproval, d.PostProcess);
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
        p.MapPost("/try", async (PlayoutLog log, BreakRenderer breaks, CancellationToken ct) =>
        {
            var songs = log.Upcoming.Where(i => i.Type == LogItemType.Song && i.Media is not null).Select(i => i.Media).Take(2).ToList();
            var sw = Stopwatch.StartNew();
            var (script, source) = await breaks.TryScriptAsync(songs.ElementAtOrDefault(0), songs.ElementAtOrDefault(1), ct);
            return new ScriptTryDto(script, source, Math.Round(sw.Elapsed.TotalSeconds, 1),
                script.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        });

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
}
