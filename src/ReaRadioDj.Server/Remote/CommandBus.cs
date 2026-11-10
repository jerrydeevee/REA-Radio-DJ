using System.Globalization;
using System.Net.Sockets;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Remote;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server.Remote;

public sealed record CommandResult(bool Ok, string Text)
{
    public static CommandResult Done(string text = "ok") => new(true, text);
    public static CommandResult Fail(string text) => new(false, text);
}

public sealed record CommandInfo(string Syntax, string Description, string Group);

/// <summary>
/// One list of plain-text commands for everything that can operate the station from outside the studio: TCP, OSC,
/// HTTP, studio hotkeys, the planner and (later) hardware such as MIDI and GPIO. Commands are case-insensitive;
/// numbers for players, cartwalls and slots count from 1.
/// </summary>
public sealed class CommandBus(
    ReaRadioDjOptions options,
    PlayoutEngine engine,
    PlayoutLog log,
    MediaLibrary library,
    StudioPlayers players,
    Cartwall cartwall,
    AudioRouter router,
    StreamGroup streamer,
    MessageCenter messages,
    IServiceProvider services,
    ILogger<CommandBus> logger)
{
    public static readonly IReadOnlyList<CommandInfo> Commands =
    [
        new("NEXT", "Start het volgende item in de log", "Uitzending"),
        new("SKIP", "Sla het huidige item over (snel uitfaden)", "Uitzending"),
        new("PLAY", "Automatisch aan en spelen", "Uitzending"),
        new("STOP", "Automatisering stoppen (uitfaden)", "Uitzending"),
        new("AUTO ON|OFF|TOGGLE", "Automatisch of live-assist", "Uitzending"),
        new("BREAK", "AI-break als volgende item", "Log"),
        new("REPLAN", "Log opnieuw vullen vanuit de klokken", "Log"),
        new("LOG ADD <media-id> [NEXT|END]", "Item uit de bibliotheek in de log zetten", "Log"),
        new("MIC ON|OFF|TOGGLE", "Microfoon (talkover)", "Mixer"),
        new("VOLUME <kanaal> <0-125>", "Fader van een kanaal (log, standby, cart, overlay, mic, pfl) in procenten", "Mixer"),
        new("MUTE <kanaal> ON|OFF|TOGGLE", "Kanaal dempen", "Mixer"),
        new("CART <wand> <knop>", "Cartknop indrukken (wand 1-2, knop 1-64)", "Cartwall"),
        new("CARTS STOP", "Alle carts stoppen", "Cartwall"),
        new("PLAYER <1|2|PFL> PLAY|PAUSE|TOGGLE|STOP|EJECT", "Standby-speler of PFL bedienen", "Spelers"),
        new("PLAYER <1|2|PFL> LOAD <media-id>", "Item in een speler laden", "Spelers"),
        new("OVERLAY PLAY <media-id>", "Item als overlay over het programma", "Spelers"),
        new("OVERLAY STOP", "Overlays stoppen", "Spelers"),
        new("DJ <A-D> <actie> [waarde]", "DJ-deck: TOGGLE, PLAY, PAUSE, CUE, SYNC, PFL, HOTCUE <1-8>, LOOPTOGGLE [tellen], FX, LOAD <media-id>, EJECT, "
            + "TEMPO <%>, BEND <%>, GAIN <dB>, FADER <0-1>, EQHIGH|EQMID|EQLOW <-1..1>, FILTER <-1..1>, SEEK <s>", "DJ"),
        new("DJ XFADER <0-1>", "Crossfader (0 = deck A en C, 1 = B en D)", "DJ"),
        new("DJ CUEMIX <0-1>", "Koptelefoon van cue (0) naar master (1)", "DJ"),
        new("DJ ONAIR ON|OFF|TOGGLE", "DJ-tafel op de lucht, of teruggeven aan de automatisering", "DJ"),
        new("DJ AUTOMIX ON|OFF|TOGGLE|MIX", "Automix aan of uit, of nu naar het volgende nummer mixen", "DJ"),
        new("STREAM START|STOP", "Icecast-uitzending starten of stoppen", "Stream"),
        new("SCAN", "Bibliotheek opnieuw inlezen", "Bibliotheek"),
        new("OSC <host:poort> </adres> [argumenten]", "OSC-bericht uitsturen, bijv. OSC 192.168.1.20:8000 /licht/onair 1 (tekst met spaties tussen \"aanhalingstekens\")", "Uitsturen"),
        new("HTTP <webadres>", "Webadres aanroepen (GET), bijv. voor een lamp, RDS-encoder of website", "Uitsturen"),
        new("MESSAGE <tekst>", "Melding in de studio", "Meldingen"),
        new("ALERT <tekst>", "Alarm in de studio", "Meldingen"),
        new("STATUS", "Wat er nu speelt", "Info"),
        new("HELP", "Lijst met commando's", "Info"),
    ];

    /// <summary>Runs one command. <paramref name="source"/> names the interface (tcp, osc, http, studio, planner).</summary>
    public CommandResult Run(string? text, string source)
    {
        var words = Split(text);
        if (words.Count == 0) return CommandResult.Fail("leeg commando");
        try
        {
            var result = Execute(words, text!.Trim());
            if (!result.Ok) logger.LogInformation("Command '{Text}' from {Source} failed: {Error}", text, source, result.Text);
            return result;
        }
        catch (Exception ex) when (ex is BassException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Command '{Text}' from {Source}", text, source);
            return CommandResult.Fail(ex.Message);
        }
    }

    private CommandResult Execute(List<string> w, string raw)
    {
        string verb = w[0].ToUpperInvariant();
        string Arg(int i) => i < w.Count ? w[i].ToUpperInvariant() : "";
        string Rest(int i) => string.Join(' ', w.Skip(i));

        switch (verb)
        {
            case "NEXT": engine.Next(); return CommandResult.Done();
            case "SKIP": engine.Skip(); return CommandResult.Done();
            case "PLAY": engine.Play(); return CommandResult.Done();
            case "STOP": engine.Stop(); return CommandResult.Done();
            case "AUTO":
                return Switch(Arg(1), engine.AutoPlay, on => engine.SetAutoPlay(on), "automatisch");
            case "BREAK":
                log.Insert(0, new LogItem { Type = LogItemType.AiBreak, Origin = "commando" });
                return CommandResult.Done("AI-break als volgende");
            case "REPLAN":
                services.GetRequiredService<AutomationService>().Replan();
                return CommandResult.Done();
            case "LOG" when Arg(1) == "ADD" && Long(w, 2) is long mediaId:
                if (library.Get(mediaId) is not { } media) return CommandResult.Fail($"media {mediaId} niet gevonden");
                var item = new LogItem { Type = media.Kind == MediaKind.Song ? LogItemType.Song : LogItemType.Jingle, Media = media, Origin = "commando" };
                log.Insert(Arg(3) == "END" ? log.Count : 0, item);
                return CommandResult.Done(media.DisplayName);

            case "MIC":
                return Switch(Arg(1), router.MicOpen, on => router.SetMic(on), "microfoon");
            case "VOLUME" when Strip(Arg(1)) is { } strip && Double(w, 2) is double pct:
                strip.SetVolume((float)Math.Clamp(pct / 100, 0, 1.25));
                return CommandResult.Done($"{strip.Name} {pct:0}%");
            case "MUTE" when Strip(Arg(1)) is { } strip:
                return Switch(Arg(2), strip.Muted, on => strip.SetVolume(strip.Volume, on), $"{strip.Name} gedempt");

            case "CART" when Long(w, 1) is long wall && Long(w, 2) is long slot:
                if (wall is < 1 or > Cartwall.SamplerWall + 1 || slot is < 1 or > Cartwall.SlotsPerWall) return CommandResult.Fail("wand 1-2 (3 = DJ-sampler), knop 1-64");
                return cartwall.Press((int)wall - 1, (int)slot - 1) ? CommandResult.Done() : CommandResult.Fail("knop is leeg");
            case "CARTS" when Arg(1) == "STOP":
                cartwall.StopAll();
                return CommandResult.Done();

            case "PLAYER":
                return Player(Arg(1), Arg(2), Long(w, 3));
            case "OVERLAY" when Arg(1) == "STOP":
                players.Overlays.StopAll(0.5);
                return CommandResult.Done();
            case "OVERLAY" when Arg(1) == "PLAY" && Long(w, 2) is long id:
                if (library.Get(id) is not { } m) return CommandResult.Fail($"media {id} niet gevonden");
                players.Overlays.Play($"cmd{id}", m.DisplayName, m, false, true, options.Routing.OverlayDuckLevel);
                return CommandResult.Done(m.DisplayName);

            case "DJ":
                return Dj(w);

            case "STREAM" when Arg(1) == "START": streamer.Start(engine.MixerHandle); return CommandResult.Done();
            case "STREAM" when Arg(1) == "STOP": streamer.Stop(); return CommandResult.Done();
            case "SCAN": _ = library.ScanAsync(); return CommandResult.Done("inlezen gestart");

            case "OSC" when w.Count >= 3:
                return SendOsc(Words(raw).Skip(1).ToList());
            case "HTTP" when w.Count >= 2:
                return CallHttp(RawRest(raw));

            case "MESSAGE" when w.Count > 1: messages.Info(RawRest(raw)); return CommandResult.Done();
            case "ALERT" when w.Count > 1: messages.Add("alarm", RawRest(raw)); return CommandResult.Done();

            case "STATUS":
                var s = engine.GetState();
                return CommandResult.Done(s.Current is { } c
                    ? $"{(s.AutoPlay ? "AUTO" : "ASSIST")} {c.Label} -{Math.Max(0, c.To - c.Position):0}s"
                    : $"{(s.AutoPlay ? "AUTO" : "ASSIST")} stil");
            case "HELP":
                return CommandResult.Done(string.Join("; ", Commands.Select(c => c.Syntax)));
        }
        return CommandResult.Fail($"onbekend commando: {Rest(0)}");
    }

    private static readonly UdpClient OscOut = new();

    /// <summary>OSC to host:port with an address and arguments (numbers as numbers, the rest as text).</summary>
    private static CommandResult SendOsc(List<string> words)
    {
        var target = words[0];
        int colon = target.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(target[(colon + 1)..], out int port) || port is < 1 or > 65535)
            return CommandResult.Fail("doel als host:poort, bijv. 192.168.1.20:8000");
        if (!words[1].StartsWith('/')) return CommandResult.Fail("het OSC-adres begint met /");
        var packet = Osc.Build(words[1], Osc.Arguments(words.Skip(2)));
        try
        {
            lock (OscOut) OscOut.Send(packet, packet.Length, target[..colon], port);
            return CommandResult.Done($"OSC {words[1]} naar {target}");
        }
        catch (SocketException ex) { return CommandResult.Fail(ex.Message); }
    }

    /// <summary>Calls a web address in the background; the answer is logged when it is an error.</summary>
    private CommandResult CallHttp(string url)
    {
        if (!Uri.TryCreate(url.Trim('"'), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return CommandResult.Fail("een webadres met http:// of https://");
        var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("commands");
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.UserAgent.ParseAdd("REA-Radio-DJ/0.1 ( https://github.com/jerrydeevee/REA-Radio-DJ )");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var res = await http.SendAsync(req, cts.Token);
                if (!res.IsSuccessStatusCode) logger.LogWarning("HTTP command to {Host} answered {Status}", uri.Host, (int)res.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { logger.LogWarning("HTTP command to {Host}: {Error}", uri.Host, ex.Message); }
        });
        return CommandResult.Done($"aangeroepen: {uri.Host}");
    }

    /// <summary>Words of a command; "text with spaces" between quotes stays one word.</summary>
    public static List<string> Words(string raw)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in raw.Trim())
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (sb.Length > 0 || any) { list.Add(sb.ToString()); sb.Clear(); any = false; }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0 || any) list.Add(sb.ToString());
        return list;
    }

    /// <summary>The DJ desk: a deck action, the crossfader, the cue mix, on air and automix.</summary>
    private CommandResult Dj(List<string> w)
    {
        var dj = services.GetRequiredService<DjMixer>();
        var handover = services.GetRequiredService<DjHandover>();
        string what = w.Count > 1 ? w[1].ToUpperInvariant() : "";
        string arg = w.Count > 2 ? w[2].ToUpperInvariant() : "";
        switch (what)
        {
            case "XFADER" when Double(w, 2) is double x: dj.SetCrossfader(x); return CommandResult.Done();
            case "CUEMIX" when Double(w, 2) is double c: dj.SetCueMix(c); return CommandResult.Done();
            case "ONAIR":
                return Switch(arg, dj.OnAir, on => { if (on) handover.GoOnAir(); else handover.GiveBack(); }, "DJ-tafel op de lucht");
            case "AUTOMIX" when arg == "MIX":
                return dj.Automix.MixNow() ? CommandResult.Done("mixen") : CommandResult.Fail("geen volgend nummer om naartoe te mixen");
            case "AUTOMIX":
                return Switch(arg, dj.Automix.On, on => { if (on) handover.Release(); dj.Automix.SetOn(on); }, "automix");
        }
        if (dj.Find(what) is not { } deck)
            return CommandResult.Fail("DJ <A-D> <actie> [waarde], DJ XFADER <0-1>, DJ CUEMIX <0-1>, DJ ONAIR, DJ AUTOMIX");
        string action = arg.ToLowerInvariant();
        double? v = Double(w, 3);
        var command = action switch
        {
            "hotcue" or "hotcueclear" => new Contracts.DjCommand(Index: v is double n ? (int)n - 1 : null),
            "load" => new Contracts.DjCommand(MediaId: v is double id ? (long)id : null),
            "fxtype" => new Contracts.DjCommand(Name: w.Count > 3 ? w[3].ToLowerInvariant() : null),
            "fx" or "pfl" or "keylock" or "killlow" or "killmid" or "killhigh" =>
                new Contracts.DjCommand(On: w.Count > 3 ? w[3].ToUpperInvariant() is "ON" or "1" or "AAN" : null),
            _ => new Contracts.DjCommand(Value: v),
        };
        // Sound shaping leaves deck A following the automation; anything that moves the track makes it the DJ's own.
        if (deck.Id == "A" && !DjApi.Shaping.Contains(action)) handover.Release();
        return DjApi.Run(dj, deck, library, action, command) is { } problem ? CommandResult.Fail(problem) : CommandResult.Done();
    }

    private CommandResult Player(string which, string action, long? mediaId)
    {
        var id = which switch { "1" => "standby1", "2" => "standby2", "3" => "standby3", "PFL" => "pfl", _ => which.ToLowerInvariant() };
        if (players.Find(id) is not { } p) return CommandResult.Fail($"speler {which} bestaat niet");
        switch (action)
        {
            case "PLAY": p.Play(); break;
            case "PAUSE": p.Pause(); break;
            case "TOGGLE": p.TogglePlay(); break;
            case "STOP": p.Stop(); break;
            case "EJECT": p.Eject(); break;
            case "LOAD" when mediaId is long m && library.Get(m) is { } media: p.Load(media); break;
            default: return CommandResult.Fail("PLAY, PAUSE, TOGGLE, STOP, EJECT of LOAD <media-id>");
        }
        return CommandResult.Done(p.Name);
    }

    private static CommandResult Switch(string arg, bool current, Action<bool> set, string what)
    {
        bool? target = arg switch { "ON" or "1" or "AAN" => true, "OFF" or "0" or "UIT" => false, "TOGGLE" or "" => !current, _ => null };
        if (target is not bool on) return CommandResult.Fail("ON, OFF of TOGGLE");
        set(on);
        return CommandResult.Done($"{what} {(on ? "aan" : "uit")}");
    }

    private MixerStrip? Strip(string name)
    {
        var id = name switch
        {
            "LOG" or "AUTO" => AudioRouter.LogStrip, "STANDBY" => AudioRouter.StandbyStrip, "CART" or "CARTS" => AudioRouter.CartStrip,
            "OVERLAY" => AudioRouter.OverlayStrip, "MIC" => AudioRouter.MicStrip, "PFL" => AudioRouter.PflStrip, _ => null,
        };
        return id is null || !router.IsStarted ? null : router.Strip(id);
    }

    private static long? Long(List<string> w, int i) =>
        i < w.Count && long.TryParse(w[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? Double(List<string> w, int i) =>
        i < w.Count && double.TryParse(w[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>The text after the first word, as typed (keeps case and spacing for messages).</summary>
    private static string RawRest(string raw)
    {
        int space = raw.IndexOf(' ');
        return space < 0 ? "" : raw[(space + 1)..].Trim().Trim('"');
    }

    private static List<string> Split(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
}
