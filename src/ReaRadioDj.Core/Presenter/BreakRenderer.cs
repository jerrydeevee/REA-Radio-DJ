using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// Renders AI breaks ahead of air time: writes a script (language model, or template as fallback), speaks it
/// (Higgs, or Windows speech as fallback), analyses the WAV and marks the log item Ready. If anything fails the
/// item is marked Failed and the engine plays a jingle instead; air is never held up.
/// </summary>
public sealed class BreakRenderer(
    ReaRadioDjOptions options,
    PlayoutLog log,
    MediaLibrary library,
    IScriptWriter? llmWriter,
    ISpeechEngine primaryVoice,
    ISpeechEngine? fallbackVoice,
    ILogger<BreakRenderer>? logger = null,
    PersonaStore? personas = null,
    BreakExtras? extras = null,
    SpeechCheck? check = null,
    CreativePicker? creative = null)
{
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;
    private readonly TemplateScriptWriter _template = new();
    private readonly string _folder = Path.Combine(options.DataFolder, "breaks");

    /// <summary>Renders every pending break in the log, oldest first.</summary>
    public async Task RenderPendingAsync(CancellationToken ct)
    {
        foreach (var item in log.Upcoming.Where(i => i.Type == LogItemType.AiBreak && i.BreakState == BreakState.Pending))
        {
            ct.ThrowIfCancellationRequested();
            await RenderAsync(item, ct);
        }
    }

    /// <summary>Writes the script and, unless a person must approve it first, speaks it.</summary>
    public async Task RenderAsync(LogItem item, CancellationToken ct)
    {
        item.BreakState = BreakState.Rendering;
        item.Error = null;
        try
        {
            var (previous, next) = log.NeighbourSongs(item.Id);
            var airTime = EstimateAirTime(item);
            var persona = personas?.For(airTime);
            item.Persona = persona?.Name;
            var extra = extras is null ? null : await extras.PickAsync(airTime, ct);
            // A creative theme instead of weather or news (a note to read out always goes first); needs a language model.
            var idea = creative is not null && extra?.Kind != "note" && llmWriter is { Configured: true }
                ? await creative.PickAsync(airTime, ct) : null;
            if (idea is not null) extra = null;
            var (script, source) = await WriteScriptAsync(Context(airTime, previous, next, persona) with { Extra = extra, Creative = idea }, ct);
            if (extra is not null)
            {
                extras!.Used(extra, airTime);
                item.Extra = extra.Kind;
            }
            if (idea is not null && source != _template.Name)
            {
                creative!.Used(idea, airTime);
                item.Extra = idea.Extra;
            }
            item.Script = script;
            item.ScriptSource = source;
            if (options.Presenter.RequireApproval)
            {
                item.BreakState = BreakState.AwaitingApproval;
                _log.LogInformation("AI break {Id} waits for approval ({Source}): {Script}", item.Id, source, script);
                return;
            }
            await SpeakAsync(item, script, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Fail(item, ex);
        }
        finally
        {
            if (item.BreakState == BreakState.Rendering) item.BreakState = BreakState.Pending; // cancelled: retry later
        }
    }

    /// <summary>
    /// Speaks a given text (a news bulletin read from headlines): no script writer, so nothing is added or changed.
    /// Waits for approval first when that is switched on, like any break.
    /// </summary>
    public async Task RenderTextAsync(LogItem item, string text, string source, CancellationToken ct)
    {
        item.BreakState = BreakState.Rendering;
        item.Error = null;
        item.Script = text;
        item.ScriptSource = source;
        try
        {
            if (options.Presenter.RequireApproval) { item.BreakState = BreakState.AwaitingApproval; return; }
            await SpeakAsync(item, text, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Fail(item, ex); }
        finally { if (item.BreakState == BreakState.Rendering) item.BreakState = BreakState.Failed; }
    }

    /// <summary>A person approved the script (possibly edited): speak it now.</summary>
    public async Task<bool> ApproveAsync(LogItem item, string? editedScript, CancellationToken ct)
    {
        if (item.Type != LogItemType.AiBreak || item.BreakState is BreakState.Rendering or BreakState.Ready) return false;
        var script = ScriptSanitizer.Clean(string.IsNullOrWhiteSpace(editedScript) ? item.Script ?? "" : editedScript, options.Presenter.MaxWords * 2);
        if (script.Length == 0) return false;
        if (!string.Equals(script, item.Script, StringComparison.Ordinal)) item.ScriptSource = "bewerkt";
        item.Script = script;
        item.BreakState = BreakState.Rendering;
        item.Error = null;
        try { await SpeakAsync(item, script, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Fail(item, ex); }
        finally { if (item.BreakState == BreakState.Rendering) item.BreakState = BreakState.AwaitingApproval; }
        return item.BreakState == BreakState.Ready;
    }

    /// <summary>A trial script for the given neighbours, without speaking it (to tune the persona and the model).</summary>
    public Task<(string Script, string Source)> TryScriptAsync(MediaItem? previous, MediaItem? next, CancellationToken ct,
        Persona? persona = null) =>
        WriteScriptAsync(Context(DateTime.Now, previous, next, persona ?? personas?.For(DateTime.Now)), ct);

    /// <summary>A trial of the creative presenter: script for a theme and tone (picked when not given), optionally spoken.</summary>
    public async Task<CreativeTrial> TryCreativeAsync(MediaItem? previous, MediaItem? next, string? tone, string? theme,
        string? wav, CancellationToken ct)
    {
        if (creative is null) throw new InvalidOperationException("Creatieve presentator niet beschikbaar");
        var persona = personas?.For(DateTime.Now);
        var idea = await creative.PickAsync(DateTime.Now, ct, force: true, tone: tone, theme: theme)
                   ?? throw new InvalidOperationException("Geen thema gevonden");
        var (script, source) = await WriteScriptAsync(Context(DateTime.Now, previous, next, persona) with { Creative = idea }, ct);
        string? voice = null;
        if (wav is not null) voice = await RenderVoiceAsync(script, wav, persona, ct, idea.Tone);
        return new CreativeTrial(idea, script, source, voice);
    }

    private BreakContext Context(DateTime time, MediaItem? previous, MediaItem? next, Persona? persona) => new(
        options.StationName,
        string.IsNullOrWhiteSpace(persona?.PresenterName) ? options.Presenter.PresenterName : persona.PresenterName,
        time, previous, next,
        string.IsNullOrWhiteSpace(persona?.Character) ? null : persona.Character,
        persona?.MaxWords);

    private Persona? PersonaOf(LogItem item) =>
        item.Persona is { } name ? personas?.All().FirstOrDefault(p => p.Name == name) : null;

    /// <summary>Why the language model failed the last time (shown with a trial).</summary>
    public string? LastLlmError { get; private set; }

    private const int MinWords = 4;
    private static int Words(string text) => ScriptSanitizer.Clean(text, 1000).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private async Task<(string Script, string Source)> WriteScriptAsync(BreakContext context, CancellationToken ct)
    {
        string raw, source;
        try
        {
            if (llmWriter is not { Configured: true }) throw new InvalidOperationException("No language model configured");
            raw = await llmWriter.WriteAsync(context, ct);
            // a model now and then answers with one stray word ("REACTIE"): ask once more, then give up
            if (Words(raw) < MinWords) raw = await llmWriter.WriteAsync(context, ct);
            if (Words(raw) < MinWords) throw new InvalidOperationException($"te korte tekst van het taalmodel: \"{raw.Trim()}\"");
            source = llmWriter.Name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (llmWriter is { Configured: true })
            {
                _log.LogWarning("Language model failed ({Message}); using template", ex.Message);
                LastLlmError = ex.Message;
            }
            raw = await _template.WriteAsync(context, ct);
            source = _template.Name;
        }
        int maxWords = context.Creative is not null
            ? Math.Max(context.MaxWords ?? options.Presenter.MaxWords, options.Presenter.Creative.MaxWords)
            : (context.MaxWords ?? options.Presenter.MaxWords) + (context.Extra is null ? 0 : 25);
        var script = ScriptSanitizer.Clean(raw, maxWords);
        if (script.Length == 0)
        {
            script = ScriptSanitizer.Clean(await _template.WriteAsync(context, ct), maxWords);
            source = _template.Name;
        }
        return (script, source);
    }

    private async Task SpeakAsync(LogItem item, string script, CancellationToken ct)
    {
        Directory.CreateDirectory(_folder);
        var wav = Path.Combine(_folder, $"break-{DateTime.Now:yyyyMMdd-HHmmss}-{item.Id}.wav");
        var persona = PersonaOf(item);
        string? tone = item.Extra is { } x && x.StartsWith("theme:", StringComparison.Ordinal) ? x[6..] : null;
        string voice = await RenderVoiceAsync(script, wav, persona, ct, tone);
        item.CheckScore = null;
        if (check is { Enabled: true })
        {
            var c = options.Presenter.Check;
            for (int attempt = 0; ; attempt++)
            {
                SpeechCheck.Result r;
                try { r = await check.CheckAsync(wav, script, ct); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
                {
                    _log.LogWarning("Speech check not available: {Message}", ex.Message);   // no check is no reason to skip a break
                    break;
                }
                item.CheckScore = Math.Round(r.Score, 2);
                if (r.Score >= c.MinScore) break;
                _log.LogWarning("AI break {Id}: speech check {Score:0.00} below {Min:0.00}; heard: {Heard}", item.Id, r.Score, c.MinScore, r.Heard);
                if (attempt >= c.Retries)
                {
                    if (c.Reject) throw new InvalidOperationException($"gesproken tekst wijkt af (score {r.Score:0.00}): \"{r.Heard}\"");
                    item.Error = $"controle: score {r.Score:0.00}";
                    break;
                }
                voice = await RenderVoiceAsync(script, wav, persona, ct, tone);
            }
        }

        if ((persona?.BedCategoryId ?? options.Presenter.BedCategoryId) is long bedCategory)
        {
            var beds = library.InCategory(bedCategory);
            if (beds.Count > 0)
            {
                var bed = beds[Random.Shared.Next(beds.Count)];
                try
                {
                    BreakMixer.Mix(wav, bed.Path);
                    voice += $" + bed {bed.Title}";
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
                {
                    _log.LogWarning("Music bed {Bed} skipped: {Message}", bed.Path, ex.Message);
                }
            }
        }

        string presenter = string.IsNullOrWhiteSpace(persona?.PresenterName) ? options.Presenter.PresenterName : persona.PresenterName;
        var media = library.Load(wav, MediaKind.Voice) with { Title = script, Artist = presenter };
        item.Voice = persona is null ? voice : $"{persona.Name}: {voice}";
        item.Media = media;
        item.BreakState = BreakState.Ready;
        AddOutro(item);
        _log.LogInformation("AI break {Id} ready ({Source} + {Voice}, {Duration:0.0}s): {Script}",
            item.Id, item.ScriptSource, voice, media.CueOut - media.CueIn, script);
    }

    /// <summary>Speaks the script with the persona's voice (or the default) and its backups, then post-processes it.</summary>
    /// <param name="tone">A creative break's tone: its own voice (when chosen) and, with Higgs, tone tags.</param>
    private async Task<string> RenderVoiceAsync(string script, string wav, Persona? persona, CancellationToken ct, string? tone = null)
    {
        string voice;
        try
        {
            if (primaryVoice is ProfileSpeechEngine profiles)
            {
                var c = options.Presenter.Creative;
                var profile = tone is not null && c.VoiceId is long creativeId ? profiles.Profile(creativeId) : null;
                profile ??= persona?.VoiceId is long voiceId ? profiles.Profile(voiceId) : null;
                profile ??= profiles.Profile(0) ?? DefaultProfile(profiles);
                if (tone is not null && c.ToneVoice && profile is { Engine: "higgs" })
                    profile = profile with { Tags = CreativeTones.HiggsTags(tone) };
                voice = profile is null ? await profiles.SpeakAsync(script, wav, null, ct)
                                        : await profiles.SpeakAsync(script, wav, profile, ct);
                if (tone is not null) voice += $" ({tone})";
                if (profiles.LastFallback is { } why) _log.LogWarning("Backup voice: {Why}", why);
            }
            else
            {
                await primaryVoice.SynthesizeAsync(script, wav, ct);
                voice = primaryVoice.Name;
            }
        }
        catch (Exception ex) when (fallbackVoice is not null && !ct.IsCancellationRequested)
        {
            _log.LogWarning("Voice {Voice} failed ({Message}); using {Fallback}", primaryVoice.Name, ex.Message, fallbackVoice.Name);
            await fallbackVoice.SynthesizeAsync(script, wav, ct);
            voice = fallbackVoice.Name;
        }

        if (options.Presenter.PostProcess)
        {
            try
            {
                var r = VoiceProcessor.Process(wav);
                _log.LogDebug("Voice processed: {In:0.00}s → {Out:0.00}s, {Gain:+0.0;-0.0} dB", r.SecondsIn, r.SecondsOut, r.GainDb);
            }
            catch (InvalidDataException ex) { _log.LogWarning("Post-processing skipped: {Message}", ex.Message); }
        }
        return voice;
    }

    private static VoiceProfile? DefaultProfile(ProfileSpeechEngine profiles) => profiles.DefaultProfile;

    /// <summary>Puts one jingle of the outro category straight after the break (once; not when a jingle already follows).</summary>
    private void AddOutro(LogItem item)
    {
        if (options.Presenter.OutroCategoryId is not long category || item.OutroAdded) return;
        var jingles = library.InCategory(category);
        if (jingles.Count == 0) return;
        var upcoming = log.Upcoming;
        int index = upcoming.ToList().FindIndex(i => i.Id == item.Id);
        if (index < 0) return;
        if (index + 1 < upcoming.Count && upcoming[index + 1].Type == LogItemType.Jingle) return;
        log.Insert(index + 1, new LogItem
        {
            Type = LogItemType.Jingle,
            Media = jingles[Random.Shared.Next(jingles.Count)],
            Origin = "AI-break outro",
        });
        item.OutroAdded = true;
    }

    private void Fail(LogItem item, Exception ex)
    {
        item.BreakState = BreakState.Failed;
        item.Error = ex.Message;
        _log.LogError(ex, "AI break {Id} failed", item.Id);
    }

    /// <summary>Rough air time of an item: now + the remaining length of everything before it.</summary>
    private DateTime EstimateAirTime(LogItem item)
    {
        double seconds = 0;
        foreach (var i in log.Upcoming)
        {
            if (i.Id == item.Id) break;
            if (i.Media is { } m) seconds += Math.Max(0, m.MixPoint - m.CueIn);
            else seconds += 10;
        }
        return DateTime.Now.AddSeconds(seconds);
    }

    /// <summary>Deletes rendered breaks older than a day.</summary>
    public void CleanUp()
    {
        if (!Directory.Exists(_folder)) return;
        foreach (var f in Directory.EnumerateFiles(_folder, "break-*.wav"))
            if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1))
                try { File.Delete(f); } catch (IOException) { }
    }
}
