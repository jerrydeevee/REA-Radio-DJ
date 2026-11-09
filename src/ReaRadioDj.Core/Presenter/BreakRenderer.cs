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
    PersonaStore? personas = null)
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
            var (script, source) = await WriteScriptAsync(Context(airTime, previous, next, persona), ct);
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

    private BreakContext Context(DateTime time, MediaItem? previous, MediaItem? next, Persona? persona) => new(
        options.StationName,
        string.IsNullOrWhiteSpace(persona?.PresenterName) ? options.Presenter.PresenterName : persona.PresenterName,
        time, previous, next,
        string.IsNullOrWhiteSpace(persona?.Character) ? null : persona.Character,
        persona?.MaxWords);

    private Persona? PersonaOf(LogItem item) =>
        item.Persona is { } name ? personas?.All().FirstOrDefault(p => p.Name == name) : null;

    private async Task<(string Script, string Source)> WriteScriptAsync(BreakContext context, CancellationToken ct)
    {
        string raw, source;
        try
        {
            if (llmWriter is not { Configured: true }) throw new InvalidOperationException("No language model configured");
            raw = await llmWriter.WriteAsync(context, ct);
            source = llmWriter.Name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (llmWriter is { Configured: true }) _log.LogWarning("Language model failed ({Message}); using template", ex.Message);
            raw = await _template.WriteAsync(context, ct);
            source = _template.Name;
        }
        int maxWords = context.MaxWords ?? options.Presenter.MaxWords;
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
        string voice;
        try
        {
            if (persona?.VoiceId is long voiceId && primaryVoice is ProfileSpeechEngine profiles && profiles.Profile(voiceId) is { } profile)
            {
                await profiles.SynthesizeAsync(script, wav, profile, ct);
                voice = profile.Name;
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
        _log.LogInformation("AI break {Id} ready ({Source} + {Voice}, {Duration:0.0}s): {Script}",
            item.Id, item.ScriptSource, voice, media.CueOut - media.CueIn, script);
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
