using System.Collections.Concurrent;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server;

/// <summary>Transition editor: read, change, remove and pre-listen the transition into a log item.</summary>
public static class TransitionApi
{
    private static readonly ConcurrentDictionary<string, ushort[]> PeakCache = new();

    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        var g = api.MapGroup("/transition");

        g.MapGet("/{id:long}", (long id, PlayoutLog log, MediaLibrary library) =>
            Pair(log, id) is not { } p ? Results.NotFound(new { error = "geen overgang: het item of het item ervoor heeft geen audio" })
            : Results.Ok(Dto(p.Out, p.In, options, library)));

        g.MapPut("/{id:long}", (long id, TransitionSettingsDto s, PlayoutLog log, MediaLibrary library) =>
        {
            if (Pair(log, id) is not { } p) return Results.NotFound();
            p.In.MixIn = Build(s, p.Out.Media!, p.In.Media!, library);
            log.Touch();
            return Results.Ok(Dto(p.Out, p.In, options, library));
        });

        g.MapDelete("/{id:long}", (long id, PlayoutLog log, MediaLibrary library) =>
        {
            if (Pair(log, id) is not { } p) return Results.NotFound();
            p.In.MixIn = null;
            log.Touch();
            return Results.Ok(Dto(p.Out, p.In, options, library));
        });

        g.MapPost("/{id:long}/preview", (long id, TransitionSettingsDto s, PlayoutLog log, MediaLibrary library, StudioPlayers players) =>
        {
            if (Pair(log, id) is not { } p) return Results.NotFound();
            var (a, b) = (p.Out.Media!, p.In.Media!);
            var t = Build(s, a, b, library);
            var target = options.Mix.TargetLufs;
            var (samples, start) = TransitionPreview.Render(a, b, t, AudioRouter.LoudnessGain(a, target), AudioRouter.LoudnessGain(b, target),
                t.Insert is { } ins ? AudioRouter.LoudnessGain(ins, target) : 1f);
            var folder = Path.Combine(options.DataFolder, "preview");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "overgang.wav");
            File.WriteAllBytes(path, VoiceProcessor.Write(BassRuntime.SampleRate, 2, samples));
            double length = samples.Length / 2.0 / BassRuntime.SampleRate;
            var media = new MediaItem { Path = path, Kind = MediaKind.Voice, Title = "Overgang (voorbeeld)", Duration = length,
                Cues = new CueSet().Normalize(length) };
            players.Pfl.Load(media);
            players.Pfl.PlayFrom(0);
            // Start in the outgoing item's time, so the editor can follow the playhead.
            return Results.Ok(new { start = Math.Round(start, 3), length = Math.Round(length, 3) });
        });

        g.MapPost("/preview/stop", (StudioPlayers players) => { players.Pfl.Stop(); return Results.Ok(); });
    }

    /// <summary>The item and the item before it (the one on air when it is next), both with audio.</summary>
    private static (LogItem Out, LogItem In)? Pair(PlayoutLog log, long id)
    {
        var up = log.Upcoming;
        int i = up.ToList().FindIndex(x => x.Id == id);
        if (i < 0) return null;
        var before = i > 0 ? up[i - 1] : log.History.FirstOrDefault();
        if (before?.Media is null || up[i].Media is null) return null;
        return (before, up[i]);
    }

    private static Transition Build(TransitionSettingsDto s, MediaItem a, MediaItem b, MediaLibrary library) => new Transition
    {
        AfterPath = a.Path,
        StartAt = s.StartAt,
        From = s.From,
        Out = s.Out.Select(p => new EnvelopePoint(p.Seconds, p.Gain)).ToList(),
        In = s.In.Select(p => new EnvelopePoint(p.Seconds, p.Gain)).ToList(),
        Insert = s.InsertMediaId is long mid ? library.Get(mid) : null,
        InsertAt = s.InsertAt,
    }.Checked(a, b);

    private static TransitionSettingsDto Settings(Transition t) => new(t.StartAt, t.From,
        t.Out.Select(p => new EnvelopePointDto(p.Seconds, p.Gain)).ToList(), t.In.Select(p => new EnvelopePointDto(p.Seconds, p.Gain)).ToList(),
        t.Insert is { Id: > 0 } m ? m.Id : null, t.InsertAt);

    private static TransitionDto Dto(LogItem a, LogItem b, ReaRadioDjOptions options, MediaLibrary library)
    {
        var auto = Transition.Default(a.Media!, b.Media!, options.Mix.FadeOut);
        var own = b.MixIn is { } t && t.AfterPath == a.Media!.Path ? t : null;
        var ins = own?.Insert;
        return new TransitionDto(b.Id, Side(a, options, library), Side(b, options, library), Settings(own ?? auto), Settings(auto),
            own is not null, AnalysisResult.BlockMilliseconds, ins?.DisplayName, ins is null ? 0 : ins.CueOut - ins.CueIn);
    }

    private static TransitionSideDto Side(LogItem i, ReaRadioDjOptions options, MediaLibrary library)
    {
        var m = i.Media!;
        var peaks = m.Id > 0 ? library.GetPeaks(m.Id)
            : PeakCache.GetOrAdd(m.Path, p => File.Exists(p) ? AudioAnalyzer.Analyze(p, options.Mix).Peaks : []);
        var bytes = new byte[peaks.Length * 2];
        Buffer.BlockCopy(peaks, 0, bytes, 0, bytes.Length);
        var label = i.Type switch { LogItemType.AiBreak => "AI-break", LogItemType.VoiceTrack => "Voicetrack", _ => m.DisplayName };
        return new TransitionSideDto(i.Id, label, m.Id, m.Duration > 0 ? m.Duration : m.CueOut, m.CueIn, m.CueOut, m.MixPoint,
            Convert.ToBase64String(bytes));
    }
}
