using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>The DJ desk over HTTP: one address per deck action, one for the crossfader and the on-air switch.</summary>
public static class DjApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/dj", (DjMixer dj) => Dto(dj.Info));

        api.MapPost("/dj/mixer", (DjMixerRequest r, DjMixer dj) =>
        {
            if (r.Crossfader is double x) dj.SetCrossfader(x);
            if (r.Curve is { } curve) dj.SetCurve(curve);
            if (r.OnAir is bool on) dj.SetOnAir(on);
            return Results.Ok(Dto(dj.Info));
        });

        api.MapPost("/dj/decks/{id}/{action}", (string id, string action, DjCommand? body, DjMixer dj, MediaLibrary library) =>
        {
            if (dj.Find(id) is not { } deck) return Results.NotFound();
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
            default: return $"onbekende opdracht '{action}' of gegevens ontbreken";
        }
        return null;
    }

    public static DjDto Dto(DjMixerInfo m) => new(m.Crossfader, m.Curve, m.OnAir, m.Decks.Select(Deck).ToList());

    public static DjDeckDto Deck(DjDeckInfo d) => new(d.Id, d.Media is { Id: > 0 } m ? m.Id : null, d.Media?.Title ?? "", d.Media?.Artist ?? "",
        d.Media is not null, d.Playing, d.Position, Math.Round(d.Duration, 3), d.Bpm, d.FirstBeat, d.Analysing, Math.Round(d.Tempo, 3), d.TempoRange,
        d.KeyLock, d.Synced, d.CuePoint, d.HotCues, d.LoopIn, d.LoopOut, d.LoopActive, d.LoopBeats, d.Gain, d.EqLow, d.EqMid, d.EqHigh,
        d.KillLow, d.KillMid, d.KillHigh, d.Filter, d.Fader, d.Pfl, d.LevelLeft, d.LevelRight);
}
