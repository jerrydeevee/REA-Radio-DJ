using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>Records from an input device into memory (mono float).</summary>
public interface IVoiceCapture
{
    int SampleRate { get; }
    void Start(int device);
    float[] Stop();
}

/// <summary>Plays something for the presenter only (the PFL output), never on air.</summary>
public interface IVoicePreview
{
    void Play(MediaItem media, double from);
    void Stop();
}

/// <summary>One recorded voice track, waiting to be placed in the log.</summary>
/// <param name="StartEarly">Seconds the voice starts before the previous item reaches its mix point (talk over the outro).</param>
/// <param name="NextAt">Position in the voice where the next item starts (talk over the intro), or null for "after the voice".</param>
public sealed record VoiceTake(string Id, string Path, double Length, double StartEarly, double? NextAt, long BeforeItemId,
    string Previous, string Next, double PeakDb);

public enum VoiceTrackStep { Idle, Outro, Intro }

/// <summary>
/// Voicetracking between two log items in three steps. Start: the end of the item before the slot plays on PFL and
/// recording begins. Next: the item after the slot starts on PFL (talk over its intro). Stop: recording ends; leading
/// and trailing silence are cut and the timing is kept, so on air the voice starts where it started over the outro and
/// the next item starts where it was started during the recording. Records its own input, never through the on-air mic.
/// </summary>
public sealed class VoiceTracker(PlayoutLog log, IVoiceCapture capture, IVoicePreview preview, Func<string, MediaItem> load,
    string folder, int device, Func<double>? clock = null)
{
    public const double SilenceDb = -42;

    private readonly object _gate = new();
    private readonly Func<double> _clock = clock ?? StopwatchClock();
    private double _startedAt;
    private double _outro;
    private double? _nextPressed;
    private LogItem? _before;
    private MediaItem? _prev, _next;

    public VoiceTrackStep Step { get; private set; }
    public VoiceTake? Take { get; private set; }
    public double Elapsed { get { lock (_gate) return Step == VoiceTrackStep.Idle ? 0 : _clock() - _startedAt; } }
    public double OutroLength { get { lock (_gate) return _outro; } }
    public long? SlotBefore { get { lock (_gate) return _before?.Id; } }

    private static Func<double> StopwatchClock()
    {
        var sw = Stopwatch.StartNew();
        return () => sw.Elapsed.TotalSeconds;
    }

    /// <summary>Step 1: plays the last <paramref name="preroll"/> seconds before the previous item's mix point and starts recording.</summary>
    public void Start(long beforeItemId, double preroll, int? inputDevice = null)
    {
        lock (_gate)
        {
            if (Step != VoiceTrackStep.Idle) throw new InvalidOperationException("er loopt al een opname");
            var upcoming = log.Upcoming;
            int index = upcoming.ToList().FindIndex(i => i.Id == beforeItemId);
            if (index < 0) throw new InvalidOperationException("dat logitem staat niet (meer) in de log");
            _before = upcoming[index];
            _prev = index > 0 ? upcoming[index - 1].Media : log.History.FirstOrDefault()?.Media;
            _next = _before.Media;
            DiscardTake();

            _outro = _prev is { } p ? Math.Clamp(preroll, 0, Math.Max(0, p.MixPoint - p.CueIn)) : 0;
            try { capture.Start(inputDevice ?? device); }
            catch { _before = null; _outro = 0; throw; }
            _startedAt = _clock();
            _nextPressed = null;
            if (_prev is not null && _outro > 0) preview.Play(_prev, _prev.MixPoint - _outro);
            Step = VoiceTrackStep.Outro;
        }
    }

    /// <summary>Step 2: starts the next item on PFL; on air it starts at this moment in the voice.</summary>
    public void Next()
    {
        lock (_gate)
        {
            if (Step != VoiceTrackStep.Outro) throw new InvalidOperationException("eerst Start");
            _nextPressed = _clock() - _startedAt;
            if (_next is not null) preview.Play(_next, _next.CueIn);
            else preview.Stop();
            Step = VoiceTrackStep.Intro;
        }
    }

    /// <summary>Step 3: stops recording and makes the take.</summary>
    public VoiceTake Stop()
    {
        float[] samples;
        double? next;
        double outro;
        LogItem before;
        lock (_gate)
        {
            if (Step == VoiceTrackStep.Idle) throw new InvalidOperationException("er loopt geen opname");
            samples = capture.Stop();
            preview.Stop();
            Step = VoiceTrackStep.Idle;
            next = _nextPressed;
            outro = _outro;
            before = _before!;
        }

        var take = Make(samples, capture.SampleRate, outro, next);
        var id = Guid.NewGuid().ToString("N")[..12];
        Directory.CreateDirectory(folder);
        var path = System.IO.Path.Combine(folder, $"vt-{DateTime.Now:yyyyMMdd-HHmmss}-{id}.wav");
        File.WriteAllBytes(path, VoiceProcessor.Write(capture.SampleRate, 1, take.Samples));
        lock (_gate)
        {
            Take = new VoiceTake(id, path, take.Length, take.StartEarly, take.NextAt, before.Id,
                _prev?.DisplayName ?? "", _next?.DisplayName ?? before.Label, take.PeakDb);
            return Take;
        }
    }

    /// <summary>Stops without keeping anything.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (Step != VoiceTrackStep.Idle) capture.Stop();
            preview.Stop();
            Step = VoiceTrackStep.Idle;
        }
    }

    public void PlayTake()
    {
        if (Take is { } t) preview.Play(load(t.Path), 0);
    }

    /// <summary>Puts the take in the log just before the item it was recorded for.</summary>
    public LogItem Place()
    {
        lock (_gate)
        {
            if (Take is not { } t) throw new InvalidOperationException("geen opname om te plaatsen");
            int index = log.Upcoming.ToList().FindIndex(i => i.Id == t.BeforeItemId);
            if (index < 0) throw new InvalidOperationException("het logitem na deze opname is al gespeeld of verwijderd");
            var media = load(t.Path) with
            {
                Title = "Voicetrack",
                Artist = "",
                Cues = new CueSet { CueIn = 0, Mix = t.NextAt ?? t.Length, CueOut = t.Length }.Normalize(t.Length),
            };
            var item = new LogItem { Type = LogItemType.VoiceTrack, Media = media, Origin = "voicetrack", StartEarly = t.StartEarly };
            // The voice talks about the items around it: keep those when the log is replanned.
            var upcoming = log.Upcoming;
            foreach (var neighbour in new[] { index > 0 ? upcoming[index - 1] : null, upcoming[index] })
                if (neighbour is not null && neighbour.Origin.StartsWith("klok", StringComparison.Ordinal))
                    neighbour.Origin = "vast voor voicetrack · " + neighbour.Origin;
            log.Insert(index, item);
            Take = null;
            return item;
        }
    }

    public void DiscardTake()
    {
        if (Take is { } t) { try { File.Delete(t.Path); } catch (IOException) { } }
        Take = null;
    }

    /// <summary>Deletes voice track files older than two weeks.</summary>
    public static void CleanUp(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var f in Directory.EnumerateFiles(folder, "vt-*.wav"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14))
                try { File.Delete(f); } catch (IOException) { }
    }

    internal sealed record Processed(float[] Samples, double Length, double StartEarly, double? NextAt, double PeakDb);

    /// <summary>
    /// Cuts silence before and after the speech, keeps the timing: the voice starts <c>outro - lead</c> seconds before
    /// the previous item's mix point (never after it: no dead air) and the next item at <c>next - lead</c>.
    /// </summary>
    internal static Processed Make(float[] x, int rate, double outro, double? next)
    {
        int window = Math.Max(1, rate / 50); // 20 ms
        double threshold = Math.Pow(10, SilenceDb / 20);
        int first = -1, last = -1;
        for (int w = 0; w + window <= x.Length; w += window)
        {
            double sum = 0;
            for (int i = w; i < w + window; i++) sum += x[i] * x[i];
            if (Math.Sqrt(sum / window) < threshold) continue;
            if (first < 0) first = w;
            last = w + window;
        }
        if (first < 0) throw new InvalidOperationException("geen spraak gehoord: staat de goede microfoon ingesteld?");

        int from = Math.Max(0, first - rate / 20);              // keep 50 ms before the first word
        int to = Math.Min(x.Length, last + rate / 4);           // and 250 ms after the last
        var y = x[from..to];
        VoiceProcessor.HighPass(y, rate, 1, 80);
        VoiceProcessor.Compress(y, rate, 1, new VoiceProcessor.Settings());
        double gain = VoiceProcessor.Normalize(y, -1);
        VoiceProcessor.Fade(y, rate, 1, 8);

        double lead = from / (double)rate;
        double length = y.Length / (double)rate;
        double early = Math.Max(0, outro - lead);
        double? nextAt = next is double n ? Math.Clamp(n - lead, 0, length) : null;
        double peak = 0;
        foreach (var v in x.AsSpan(from, to - from)) peak = Math.Max(peak, Math.Abs(v));
        return new Processed(y, Math.Round(length, 3), Math.Round(early, 3), nextAt is double a ? Math.Round(a, 3) : null,
            Math.Round(20 * Math.Log10(Math.Max(peak, 1e-6)), 1));
    }
}

/// <summary>Records from a BASS input device into memory, separate from the on-air microphone.</summary>
public sealed unsafe class BassVoiceCapture : IVoiceCapture
{
    private static readonly object Gate = new();
    private static List<float>? _buffer;
    private int _handle;

    public int SampleRate => BassRuntime.SampleRate;

    public void Start(int device)
    {
        if (!BASS_RecordInit(device) && BASS_ErrorGetCode() != BASS_ERROR_ALREADY)
            throw new InvalidOperationException($"microfoon (opnameapparaat {device}) niet beschikbaar (BASS-fout {BASS_ErrorGetCode()})");
        lock (Gate) _buffer = new List<float>(SampleRate * 60);
        delegate* unmanaged[Stdcall]<int, IntPtr, uint, IntPtr, int> proc = &Proc;
        _handle = BASS_RecordStart((uint)SampleRate, 1, BASS_SAMPLE_FLOAT, (IntPtr)proc, IntPtr.Zero);
        if (_handle == 0) throw new InvalidOperationException($"opname kon niet starten (BASS-fout {BASS_ErrorGetCode()})");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Proc(int handle, IntPtr buffer, uint length, IntPtr user)
    {
        var data = new ReadOnlySpan<float>((void*)buffer, (int)(length / 4));
        lock (Gate) _buffer?.AddRange(data);
        return 1;
    }

    public float[] Stop()
    {
        if (_handle != 0) BASS_ChannelStop(_handle);
        _handle = 0;
        lock (Gate)
        {
            var result = _buffer?.ToArray() ?? [];
            _buffer = null;
            return result;
        }
    }
}

/// <summary>Plays voicetrack audio on the PFL player.</summary>
public sealed class PflVoicePreview(StudioPlayers players) : IVoicePreview
{
    public void Play(MediaItem media, double from)
    {
        players.Pfl.Load(media);
        players.Pfl.PlayFrom(from);
    }

    public void Stop() => players.Pfl.Stop();
}
