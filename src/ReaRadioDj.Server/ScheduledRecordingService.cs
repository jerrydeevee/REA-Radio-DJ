using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>
/// Recordings besides the program recording: <b>scheduled recordings</b> (every day at 12:00 the regional news from
/// an input or a stream, straight into the library) and the <b>emergency recorder</b> (one button: record everything
/// that comes in, every input channel apart, until it is pressed again).
/// </summary>
public sealed class ScheduledRecordingService(AudioRouter router, RecordingService recordings, AirRecorder air, MediaLibrary library,
    StationDb db, ReaRadioDjOptions options, MessageCenter messages, IHttpClientFactory http, ILogger<ScheduledRecordingService> logger)
    : BackgroundService
{
    public const string Key = "recordings.scheduled";
    /// <summary>Below this the emergency recording of a channel counts as silence and is dropped (about -46 dBFS).</summary>
    public const float SilentPeak = 0.005f;

    private readonly object _gate = new();
    private List<ScheduledRecording> _list = [];
    private readonly Dictionary<string, Job> _running = [];
    private readonly Dictionary<string, Outcome> _last = [];
    private readonly HashSet<(string, DateTime)> _done = [];
    private EmergencyRun? _emergency;

    private sealed class Job
    {
        public required ScheduledRecording Item { get; init; }
        public required DateTime Edition { get; init; }
        public required DateTime Until { get; set; }
        public DateTime Started { get; } = DateTime.Now;
        public ChannelRecorder? Recorder { get; init; }
        public CancellationTokenSource? Stream { get; init; }
        public Task<string?>? StreamTask { get; init; }
    }

    private sealed record EmergencyRun(DateTime Started, IReadOnlyList<(MixerStrip Strip, ChannelRecorder Recorder)> Channels);

    public sealed record Outcome(DateTime Time, string? File, double Seconds, string? Error);

    public sealed record ItemStatus(ScheduledRecording Item, DateTime? Next, bool Recording, DateTime? Until, double Seconds, Outcome? Last, string? Problem);

    public sealed record EmergencyStatus(bool Recording, DateTime? Started, double Seconds, IReadOnlyList<string> Channels);

    private string WorkFolder => Path.Combine(options.RecordingPath, "Gepland");

    // ------------------------------------------------------------------ settings

    public IReadOnlyList<ScheduledRecording> List() { lock (_gate) return _list.ToList(); }

    public static List<ScheduledRecording> Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return JsonSerializer.Deserialize<List<ScheduledRecording>>(json, JsonSerializerOptions.Web)?.Select(r => r.Checked()).ToList() ?? []; }
        catch (JsonException) { return []; }
    }

    public IReadOnlyList<ScheduledRecording> Save(IEnumerable<ScheduledRecording> items)
    {
        var list = items.Select(i => i.Checked()).ToList();
        db.SetSetting(Key, JsonSerializer.Serialize(list, JsonSerializerOptions.Web));
        lock (_gate) _list = list;
        // A recording that was removed or switched off stops now; a shorter one stops at its new end.
        foreach (var (id, job) in Running())
        {
            var now = list.FirstOrDefault(i => i.Id == id);
            if (now is null || !now.Enabled) Finish(id, "de opname is uitgezet");
            else job.Until = job.Edition + now.Length;
        }
        return list;
    }

    /// <summary>The channels a recording can take: every mixer channel that brings sound in, and the program.</summary>
    public IReadOnlyList<(string Id, string Name)> Sources() =>
        router.Strips.Where(s => s.Source is not null || s.Id is AudioRouter.MicStrip || AudioRouter.IsVoip(s.Id))
            .OrderBy(s => s.Id == AudioRouter.MicStrip ? 0 : s.Source is not null ? 1 : 2)
            .Select(s => ($"strip:{s.Id}", s.Name)).ToList();

    public IReadOnlyList<ItemStatus> Status(DateTime now)
    {
        var running = Running().ToDictionary(r => r.Id, r => r.Job);
        lock (_gate)
            return _list.Select(i => new ItemStatus(i, i.Next(now), running.ContainsKey(i.Id), running.GetValueOrDefault(i.Id)?.Until,
                running.TryGetValue(i.Id, out var j) ? (now - j.Started).TotalSeconds : 0, _last.GetValueOrDefault(i.Id), i.Problem())).ToList();
    }

    private List<(string Id, Job Job)> Running() { lock (_gate) return _running.Select(p => (p.Key, p.Value)).ToList(); }

    // ------------------------------------------------------------------ the clock

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        lock (_gate) _list = Load(db);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { Tick(DateTime.Now); }
                catch (Exception ex) { logger.LogError(ex, "Scheduled recording tick failed"); }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var (id, _) in Running()) Finish(id, "de engine stopt", wait: true);
            if (_emergency is not null) StopEmergency(wait: true);
        }
    }

    internal void Tick(DateTime now)
    {
        foreach (var (id, job) in Running())
            if (now >= job.Until) Finish(id, null);
        foreach (var item in List())
        {
            if (item.Running(now) is not DateTime edition) continue;
            lock (_gate)
            {
                if (_running.ContainsKey(item.Id) || _done.Contains((item.Id, edition))) continue;
                _done.Add((item.Id, edition));
                _done.RemoveWhere(d => d.Item2 < now.AddDays(-2));
            }
            // Started late (the engine came up halfway): record what is left, but not the last few seconds.
            if (edition + item.Length - now < TimeSpan.FromSeconds(15)) continue;
            Begin(item, edition, edition + item.Length);
        }
        if (_emergency is { } e && now - e.Started >= RecordingCutter.MaxLength)
        {
            messages.Warning($"De noodopname liep {RecordingCutter.MaxLength.TotalHours:0} uur en is gestopt.");
            StopEmergency();
        }
    }

    /// <summary>Starts a recording now for its set length, outside its times (to try it, or once extra).</summary>
    public string? StartNow(string id)
    {
        var item = List().FirstOrDefault(i => i.Id == id);
        if (item is null) return "opname niet gevonden";
        if (item.Problem() is { } p) return p;
        lock (_gate) if (_running.ContainsKey(id)) return "deze opname loopt al";
        var now = DateTime.Now;
        return Begin(item, now, now + item.Length);
    }

    public string? StopNow(string id)
    {
        lock (_gate) if (!_running.ContainsKey(id)) return "deze opname loopt niet";
        Finish(id, "met de hand gestopt");
        return null;
    }

    private string? Begin(ScheduledRecording item, DateTime edition, DateTime until)
    {
        string? error = null;
        Job? job = null;
        try
        {
            if (item.IsStream)
            {
                var cts = new CancellationTokenSource();
                var path = Path.Combine(WorkFolder, $"{item.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.part");
                job = new Job { Item = item, Edition = edition, Until = until, Stream = cts, StreamTask = Task.Run(() => CopyStream(item.Source, path, cts.Token)) };
            }
            else if (item.StripId is { } sid && router.HasStrip(sid))
            {
                var rec = new ChannelRecorder(router.Strip(sid).Handle);
                rec.Start(WorkFolder, item.Id);
                job = new Job { Item = item, Edition = edition, Until = until, Recorder = rec };
            }
            else error = $"kanaal {item.StripId} bestaat niet (meer)";
        }
        catch (Exception ex) when (ex is BassException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }
        if (job is null)
        {
            error ??= "niet gestart";
            lock (_gate) _last[item.Id] = new Outcome(DateTime.Now, null, 0, error);
            messages.Add("waarschuwing", $"Geplande opname {item.Name} niet gestart: {error}", "opname");
            return error;
        }
        lock (_gate) _running[item.Id] = job;
        logger.LogInformation("Scheduled recording {Name} started until {Until}", item.Name, until);
        messages.Info($"Geplande opname {item.Name} loopt tot {until:HH:mm}");
        return null;
    }

    /// <summary>Copies an internet stream as it comes in, without metadata in between; returns the content type.</summary>
    private async Task<string?> CopyStream(string url, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var client = http.CreateClient("recording");
        client.Timeout = Timeout.InfiniteTimeSpan;
        for (int attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var type = response.Content.Headers.ContentType?.MediaType;
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                await input.CopyToAsync(output, ct);
                if (type is not null) return type;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                logger.LogWarning("Stream recording {Url}: {Error}", url, ex.Message);
                if (attempt == 0) messages.Warning($"Opname van {url}: {ex.Message}; er wordt opnieuw verbonden.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { break; }
        }
        return null;
    }

    private void Finish(string id, string? why, bool wait = false)
    {
        Job? job;
        lock (_gate) if (!_running.Remove(id, out job)) return;
        var started = DateTime.Now;
        if (why is not null) logger.LogInformation("Scheduled recording {Name} stopped: {Why}", job.Item.Name, why);
        var work = Task.Run(async () =>
        {
            string? raw = null, type = null;
            if (job.Recorder is { } rec) raw = rec.Stop();
            else if (job.Stream is { } cts)
            {
                cts.Cancel();
                try { type = await job.StreamTask!; } catch (OperationCanceledException) { }
                raw = Directory.EnumerateFiles(WorkFolder, $"{job.Item.Id}-*.part").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                cts.Dispose();
            }
            var outcome = Store(job.Item, job.Edition, raw, type);
            lock (_gate) _last[id] = outcome;
        });
        if (wait) work.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>Turns the raw recording into an MP3 in the library; with Replace it takes the place of the previous edition.</summary>
    private Outcome Store(ScheduledRecording item, DateTime edition, string? raw, string? contentType, bool quiet = false)
    {
        string label = quiet ? item.Name : $"Geplande opname {item.Name}";
        if (raw is null || !File.Exists(raw) || new FileInfo(raw).Length < 1024)
        {
            if (raw is not null) TryDelete(raw);
            var error = item.IsStream ? "er kwam niets binnen van de stream" : "er is niets opgenomen";
            if (!quiet) messages.Add("waarschuwing", $"{label}: {error}.", "opname");
            return new Outcome(DateTime.Now, null, 0, error);
        }
        try
        {
            if (recordings.EnsureLibraryFolder() is { } folderError) throw new IOException(folderError);
            var mp3 = Path.ChangeExtension(raw, ".mp3");
            double seconds;
            bool isMp3 = contentType is "audio/mpeg" or "audio/mp3" || (item.IsStream && contentType is null);
            if (item.IsStream && isMp3)
            {
                File.Move(raw, mp3, overwrite: true);
                seconds = RecordingCutter.Measure(mp3)?.Seconds ?? 0;
                if (seconds <= 0) throw new InvalidOperationException($"de stream gaf geen leesbaar MP3 ({contentType ?? "onbekend type"})");
            }
            else
            {
                var measured = RecordingCutter.Measure(raw)
                    ?? throw new InvalidOperationException($"de opname is niet te lezen{(contentType is null ? "" : $" ({contentType})")}");
                seconds = RecordingCutter.Cut([new CutPiece(raw, 0, measured.Seconds)], mp3, air.Settings.Bitrate);
                TryDelete(raw);
            }
            try { RecordingCutter.Tag(mp3, item.Name, item.Artist, edition, $"Opgenomen op {edition:dd-MM-yyyy HH:mm}"); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not tag {File}", mp3); }

            var target = Path.Combine(recordings.LibraryFolder, item.FileName(edition));
            string note = "";
            try { File.Move(mp3, target, overwrite: item.Replace); }
            catch (IOException) when (item.Replace)
            {
                // The previous edition is playing (or open elsewhere): keep the new one beside it.
                target = Path.Combine(recordings.LibraryFolder, $"{edition:yyyy-MM-dd-HHmm} {item.FileName(edition)}");
                File.Move(mp3, target, overwrite: true);
                note = " (de vorige was in gebruik; de nieuwe staat ernaast)";
            }
            catch (IOException)
            {
                target = Path.Combine(recordings.LibraryFolder, $"{Path.GetFileNameWithoutExtension(target)} {DateTime.Now:HHmmss}.mp3");
                File.Move(mp3, target);
            }
            _ = library.ScanAsync();
            if (!quiet) messages.Info($"{label} staat klaar in de bibliotheek (categorie Opnamen), {TimeSpan.FromSeconds(seconds):h\\:mm\\:ss}{note}");
            return new Outcome(DateTime.Now, Path.GetFileName(target), Math.Round(seconds, 1), null);
        }
        catch (Exception ex) when (ex is BassException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Scheduled recording {Name} could not be stored", item.Name);
            messages.Add("waarschuwing", $"{label} niet in de bibliotheek: {ex.Message}" + (raw is not null && File.Exists(raw) ? $" De opname zelf staat in {raw}." : ""), "opname");
            return new Outcome(DateTime.Now, null, 0, ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------ emergency recorder

    public EmergencyStatus Emergency()
    {
        var e = _emergency;
        return e is null
            ? new EmergencyStatus(false, null, 0, [])
            : new EmergencyStatus(true, e.Started, (DateTime.Now - e.Started).TotalSeconds, e.Channels.Select(c => c.Strip.Name).ToList());
    }

    /// <summary>Starts recording every channel that brings sound in (microphone, inputs, VoIP), each to its own file.</summary>
    public string? StartEmergency()
    {
        lock (_gate)
        {
            if (_emergency is not null) return null;
            var strips = router.Strips.Where(s => s.Source is not null || s.Id is AudioRouter.MicStrip || AudioRouter.IsVoip(s.Id)).ToList();
            if (strips.Count == 0) return "er is geen ingang om op te nemen";
            var folder = Path.Combine(options.RecordingPath, "Noodopname");
            var channels = new List<(MixerStrip, ChannelRecorder)>();
            foreach (var s in strips)
            {
                try
                {
                    var rec = new ChannelRecorder(s.Handle);
                    rec.Start(folder, s.Id);
                    channels.Add((s, rec));
                }
                catch (Exception ex) when (ex is BassException or IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Emergency recording of {Strip} did not start", s.Id);
                }
            }
            if (channels.Count == 0) return "opnemen lukte op geen enkel kanaal";
            _emergency = new EmergencyRun(DateTime.Now, channels);
        }
        messages.Add("alarm", $"Noodopname loopt: {string.Join(", ", _emergency.Channels.Select(c => c.Strip.Name))}");
        return null;
    }

    /// <summary>Stops the emergency recording; channels that stayed silent are dropped, the rest go into the library.</summary>
    public void StopEmergency(bool wait = false)
    {
        EmergencyRun? e;
        lock (_gate) { e = _emergency; _emergency = null; }
        if (e is null) return;
        var work = Task.Run(() =>
        {
            var kept = new List<string>();
            int failed = 0;
            foreach (var (strip, rec) in e.Channels)
            {
                var wav = rec.Stop();
                if (wav is null) continue;
                if (rec.Peak < SilentPeak) { TryDelete(wav); continue; }
                var item = new ScheduledRecording("nood", $"Noodopname {strip.Name} {e.Started:dd-MM HH.mm}", true, 0, "00:00", 1, "strip:" + strip.Id, Replace: false);
                var outcome = Store(item, e.Started, wav, null, quiet: true);
                if (outcome.File is not null) kept.Add(outcome.File); else failed++;
            }
            if (kept.Count == 0 && failed > 0) return;   // the reason is already in the messages
            messages.Info(kept.Count == 0
                ? "Noodopname gestopt: op geen enkel kanaal kwam geluid binnen."
                : $"Noodopname gestopt en in de bibliotheek (categorie Opnamen): {string.Join(", ", kept)}");
        });
        if (wait) work.Wait(TimeSpan.FromSeconds(30));
    }

    public string? ToggleEmergency()
    {
        if (_emergency is null) return StartEmergency();
        StopEmergency();
        return null;
    }
}
