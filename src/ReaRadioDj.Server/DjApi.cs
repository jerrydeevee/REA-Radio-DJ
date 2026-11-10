using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>The DJ desk over HTTP: one address per deck action, one for the crossfader and the on-air switch.</summary>
public static class DjApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/dj", (DjMixer dj, DjHandover handover) => Dto(dj.Info, handover.Following));

        // Deck A starts copying what the automation plays (see DjHandover).
        api.MapPost("/dj/follow", (DjMixer dj, DjHandover handover) =>
            handover.Follow() is { } problem ? Results.BadRequest(problem) : Results.Ok(Dto(dj.Info, handover.Following)));

        api.MapPost("/dj/mixer", (DjMixerRequest r, DjMixer dj, DjHandover handover, MessageCenter messages, RecordingService recordings,
            ReaRadioDj.Core.ReaRadioDjOptions options) =>
        {
            if (r.Record is true && dj.Recording.File is null)
            {
                try
                {
                    var file = dj.StartRecording(Path.Combine(options.RecordingPath, "DJ"));
                    messages.Info($"Mix wordt opgenomen: {Path.GetFileName(file)}");
                }
                catch (Exception ex) when (ex is BassException or IOException or UnauthorizedAccessException)
                {
                    return Results.BadRequest($"Opnemen lukte niet: {ex.Message}");
                }
            }
            else if (r.Record is false && dj.StopRecording() is { } done)
                recordings.AddToLibrary(done, "DJ-mix");
            if (r.Crossfader is double x) dj.SetCrossfader(x);
            if (r.Curve is { } curve) dj.SetCurve(curve);
            if (r.Quantize is bool q) dj.SetQuantize(q);
            if (r.CueMix is double cm) dj.SetCueMix(cm);
            if (r.Limiter is not null || r.LimiterDb is not null)
            {
                var now = dj.Info;
                try { dj.SetLimiter(r.Limiter ?? now.Limiter, r.LimiterDb ?? now.LimiterDb); }
                catch (BassException ex) { return Results.BadRequest($"Begrenzer niet beschikbaar: {ex.Message}"); }
            }
            if (r.OnAir is bool on)
            {
                // On air while deck A follows: the automation stops at this moment and deck A carries on.
                if (on && !dj.OnAir && handover.TakeOver()) messages.Info("DJ-tafel heeft de uitzending overgenomen; de automatisering is gestopt");
                dj.SetOnAir(on);
            }
            return Results.Ok(Dto(dj.Info, handover.Following));
        });

        api.MapPost("/dj/decks/{id}/{action}", (string id, string action, DjCommand? body, DjMixer dj, MediaLibrary library, DjHandover handover) =>
        {
            if (dj.Find(id) is not { } deck) return Results.NotFound();
            // Sound shaping leaves deck A following; anything that moves the track makes it the DJ's own.
            if (deck.Id == "A" && !Shaping.Contains(action.ToLowerInvariant())) handover.Release();
            try
            {
                return Run(dj, deck, library, action.ToLowerInvariant(), body ?? new DjCommand()) is { } problem
                    ? Results.BadRequest(problem)
                    : Results.Ok(Deck(deck.Info));
            }
            catch (BassException ex) { return Results.BadRequest($"bestand niet te openen: {ex.Message}"); }
            catch (DllNotFoundException) { return Results.BadRequest("bass_fx.dll ontbreekt: voer scripts\fetch-bass.ps1 uit"); }
        });
    }

    private static readonly HashSet<string> Shaping =
        ["fx", "fxtype", "fxmix", "fxbeats", "gain", "fader", "eqlow", "eqmid", "eqhigh", "killlow", "killmid", "killhigh", "filter", "pfl", "keylock", "range"];

    /// <summary>Runs one deck action; returns what is wrong, or null when it is done.</summary>
    public static string? Run(DjMixer dj, DjDeck deck, MediaLibrary library, string action, DjCommand c)
    {
        double? v = c.Value;
        switch (action)
        {
            case "load" when c.MediaId is long id && library.Get(id) is { } media: deck.Load(media); break;
            case "eject": deck.Eject(); break;
            case "play": deck.Play(); break;
            case "pause": deck.Pause(); break;
            case "toggle": deck.Toggle(); break;
            case "cue": deck.Cue(); break;
            case "seek" when v is double s: deck.Seek(s); break;
            case "tempo" when v is double t: deck.SetTempo(t); break;
            case "range" when v is double r: deck.SetTempoRange(r); break;
            case "keylock": deck.SetKeyLock(c.On ?? !deck.KeyLock); break;
            case "bend" when v is double b: deck.Bend(b); break;
            case "sync": if (!dj.Sync(deck)) return "geen ander spelend deck met een tempo om te volgen"; break;
            case "gain" when v is double g: deck.SetGain(g); break;
            case "fader" when v is double f: deck.SetFader(f); break;
            case "eqlow" or "eqmid" or "eqhigh" when v is double e: deck.SetEq(action[2..], e); break;
            case "killlow": deck.SetKill("low", c.On ?? !deck.KillLow); break;
            case "killmid": deck.SetKill("mid", c.On ?? !deck.KillMid); break;
            case "killhigh": deck.SetKill("high", c.On ?? !deck.KillHigh); break;
            case "filter" when v is double x: deck.SetFilter(x); break;
            case "pfl": deck.SetPfl(c.On ?? !deck.Pfl); break;
            case "hotcue" when c.Index is int i: deck.HotCue(i); break;
            case "hotcueclear" when c.Index is int i: deck.ClearHotCue(i); break;
            case "loopin": deck.SetLoopIn(); break;
            case "loopout": deck.SetLoopOut(); break;
            case "loop": deck.Loop(v ?? deck.LoopBeats); break;
            case "loophalf": deck.ScaleLoop(0.5); break;
            case "loopdouble": deck.ScaleLoop(2); break;
            case "loopexit": deck.ExitLoop(); break;
            // Loop roll: On = true while the button is held (Value = beats), false on release.
            case "roll": deck.Roll(v ?? 1, c.On ?? false); break;
            // Beat effect: type by Name (echo, flanger, reverb), on/off, mix 0..1, length in beats.
            case "fx": deck.SetFx(c.Name, c.On ?? !deck.FxOn); break;
            case "fxtype" when c.Name is { } fx: deck.SetFx(type: fx); break;
            case "fxmix" when v is double fm: deck.SetFx(mix: fm); break;
            case "fxbeats" when v is double fb: deck.SetFx(beats: fb); break;
            default: return $"onbekende opdracht '{action}' of gegevens ontbreken";
        }
        return null;
    }

    public static DjDto Dto(DjMixerInfo m, bool following = false) =>
        new(m.Crossfader, m.Curve, m.OnAir, m.Decks.Select(Deck).ToList(), following, m.Recording is { } f ? Path.GetFileName(f) : null, m.RecordingSeconds,
            m.Quantize, m.CueMix, m.Limiter, m.LimiterDb);

    public static DjDeckDto Deck(DjDeckInfo d) => new(d.Id, d.Media is { Id: > 0 } m ? m.Id : null, d.Media?.Title ?? "", d.Media?.Artist ?? "",
        d.Media is not null, d.Playing, d.Position, Math.Round(d.Duration, 3), d.Bpm, d.FirstBeat, d.Analysing, Math.Round(d.Tempo, 3), d.TempoRange,
        d.KeyLock, d.Synced, d.CuePoint, d.HotCues, d.LoopIn, d.LoopOut, d.LoopActive, d.LoopBeats, d.Gain, d.EqLow, d.EqMid, d.EqHigh,
        d.KillLow, d.KillMid, d.KillHigh, d.Filter, d.Fader, d.Pfl, d.LevelLeft, d.LevelRight, d.Rolling, d.FxType, d.FxOn, d.FxMix, d.FxBeats);
}
