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
    ILogger<BreakRenderer>? logger = null)
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

    public async Task RenderAsync(LogItem item, CancellationToken ct)
    {
        item.BreakState = BreakState.Rendering;
        try
        {
            var (previous, next) = log.NeighbourSongs(item.Id);
            var context = new BreakContext(options.StationName, options.Presenter.PresenterName, EstimateAirTime(item), previous, next);

            string raw;
            string source;
            try
            {
                if (llmWriter is null) throw new InvalidOperationException("No language model configured");
                raw = await llmWriter.WriteAsync(context, ct);
                source = llmWriter.Name;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (llmWriter is not null) _log.LogWarning("Language model failed ({Message}); using template", ex.Message);
                raw = await _template.WriteAsync(context, ct);
                source = _template.Name;
            }
            var script = ScriptSanitizer.Clean(raw, options.Presenter.MaxWords);
            if (script.Length == 0) script = ScriptSanitizer.Clean(await _template.WriteAsync(context, ct), options.Presenter.MaxWords);

            Directory.CreateDirectory(_folder);
            var wav = Path.Combine(_folder, $"break-{DateTime.Now:yyyyMMdd-HHmmss}-{item.Id}.wav");
            string voice;
            try
            {
                await primaryVoice.SynthesizeAsync(script, wav, ct);
                voice = primaryVoice.Name;
            }
            catch (Exception ex) when (fallbackVoice is not null && !ct.IsCancellationRequested)
            {
                _log.LogWarning("Voice {Voice} failed ({Message}); using {Fallback}", primaryVoice.Name, ex.Message, fallbackVoice.Name);
                await fallbackVoice.SynthesizeAsync(script, wav, ct);
                voice = fallbackVoice.Name;
            }

            var media = library.Load(wav, MediaKind.Voice) with { Title = script, Artist = options.Presenter.PresenterName };
            item.Script = script;
            item.ScriptSource = source;
            item.Voice = voice;
            item.Media = media;
            item.BreakState = BreakState.Ready;
            _log.LogInformation("AI break {Id} ready ({Source} + {Voice}, {Duration:0.0}s): {Script}",
                item.Id, source, voice, media.CueOut - media.CueIn, script);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            item.BreakState = BreakState.Failed;
            item.Error = ex.Message;
            _log.LogError(ex, "AI break {Id} failed", item.Id);
        }
        finally
        {
            if (item.BreakState == BreakState.Rendering) item.BreakState = BreakState.Pending; // cancelled: retry later
        }
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
