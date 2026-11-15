using System.Text;

namespace ReaRadioDj.Core.Hardware;

// Mackie Control (MCU) over MIDI, written from the public description of the protocol. The surface has eight
// channel strips (motor fader with touch sensing, rotary encoder with LED ring, REC / SOLO / MUTE / SELECT buttons,
// a level meter and 2 x 7 characters of display) plus a master fader and transport buttons. An extender (XT) has only
// the eight strips, on a MIDI port of its own, and a display of its own (SysEx device 0x15 instead of 0x14). Everything is on MIDI
// channel 1 except the faders, which use pitch bend on channels 1-8 (strips) and 9 (master).
// Not yet checked against a real controller: see docs/WERKLIJST.md.

/// <summary>Something the surface sent.</summary>
public abstract record MackieInput;
/// <summary>A fader moved: strip 0-7, or 8 for the master; position 0-16383.</summary>
public sealed record MackieFader(int Strip, int Position) : MackieInput;
/// <summary>A fader was touched or let go (strip 0-7, 8 = master).</summary>
public sealed record MackieTouch(int Strip, bool Touched) : MackieInput;
/// <summary>A rotary encoder turned: positive = clockwise, in ticks.</summary>
public sealed record MackieVPot(int Strip, int Delta) : MackieInput;
/// <summary>A button (its note number) was pressed or let go.</summary>
public sealed record MackieButton(int Note, bool Pressed) : MackieInput;
/// <summary>The jog wheel turned: positive = clockwise.</summary>
public sealed record MackieJog(int Delta) : MackieInput;

/// <summary>Something to send to the surface: a short MIDI message (status in the low byte) or a SysEx message.</summary>
public sealed record MackieOutput(uint Short, byte[]? SysEx = null)
{
    public override string ToString() => SysEx is null
        ? $"{Short & 0xFF:X2} {(Short >> 8) & 0x7F:X2} {(Short >> 16) & 0x7F:X2}"
        : string.Join(' ', SysEx.Select(b => b.ToString("X2")));
}

public static class Mackie
{
    public const int Strips = 8, MasterFader = 8, FaderMax = 16383;

    // Button notes (MIDI channel 1). Strip buttons: base + strip 0-7.
    public const int Rec = 0x00, Solo = 0x08, Mute = 0x10, Select = 0x18, VPotPush = 0x20;
    public const int BankLeft = 0x2E, BankRight = 0x2F, ChannelLeft = 0x30, ChannelRight = 0x31, Flip = 0x32;
    public const int Rewind = 0x5B, Forward = 0x5C, Stop = 0x5D, Play = 0x5E, Record = 0x5F, Cycle = 0x56;
    public const int TouchBase = 0x68;   // 0x68-0x6F strips, 0x70 master
    public const int VPotCc = 0x10, RingCc = 0x30, JogCc = 0x3C;

    /// <summary>A MIDI short message from the surface; null for what it does not mean (clock, other channels).</summary>
    public static MackieInput? Parse(uint message)
    {
        int status = (int)(message & 0xFF), d1 = (int)((message >> 8) & 0x7F), d2 = (int)((message >> 16) & 0x7F);
        int kind = status & 0xF0, ch = status & 0x0F;
        switch (kind)
        {
            case 0xE0 when ch <= MasterFader:
                return new MackieFader(ch, d1 | (d2 << 7));
            case 0x90 when ch == 0 && d1 >= TouchBase && d1 <= TouchBase + MasterFader:
                return new MackieTouch(d1 - TouchBase, d2 > 0);
            case 0x80 when ch == 0 && d1 >= TouchBase && d1 <= TouchBase + MasterFader:
                return new MackieTouch(d1 - TouchBase, false);
            case 0x90 when ch == 0:
                return new MackieButton(d1, d2 > 0);
            case 0x80 when ch == 0:
                return new MackieButton(d1, false);
            case 0xB0 when ch == 0 && d1 >= VPotCc && d1 < VPotCc + Strips:
                return new MackieVPot(d1 - VPotCc, Relative(d2));
            case 0xB0 when ch == 0 && d1 == JogCc:
                return new MackieJog(Relative(d2));
        }
        return null;
    }

    /// <summary>Encoders send a direction bit (0x40 = anticlockwise) and a number of ticks.</summary>
    public static int Relative(int value) => (value & 0x40) != 0 ? -(value & 0x3F) : value & 0x3F;

    public static MackieOutput Fader(int strip, int position)
    {
        position = Math.Clamp(position, 0, FaderMax);
        return new MackieOutput((uint)(0xE0 | strip | ((position & 0x7F) << 8) | ((position >> 7) << 16)));
    }

    /// <summary>A button lamp: off, on or flashing.</summary>
    public static MackieOutput Led(int note, bool on, bool flash = false) =>
        new((uint)(0x90 | (note << 8) | ((flash ? 0x01 : on ? 0x7F : 0x00) << 16)));

    /// <summary>The LED ring of an encoder as a pan display: position -1..1 (centre = middle LED).</summary>
    public static MackieOutput PanRing(int strip, double pan)
    {
        int pos = 1 + (int)Math.Round((Math.Clamp(pan, -1, 1) + 1) * 5);   // 1..11, 6 = centre
        return new MackieOutput((uint)(0xB0 | ((RingCc + strip) << 8) | (pos << 16)));   // mode 0: a single dot
    }

    /// <summary>A level meter (0-12 segments) via channel pressure: strip in the high nibble.</summary>
    public static MackieOutput Meter(int strip, int segments) =>
        new((uint)(0xD0 | (((strip & 0x07) << 4 | Math.Clamp(segments, 0, 12)) << 8)));

    /// <summary>Segments for a peak level 0..1 (roughly -60 dB to 0 dB over twelve steps).</summary>
    public static int Segments(float peak)
    {
        if (peak <= 0.001f) return 0;
        double db = 20 * Math.Log10(peak);
        double[] steps = [-60, -50, -40, -30, -20, -14, -10, -8, -6, -4, -2, 0];
        return steps.Count(s => db >= s);
    }

    public const byte MainDisplay = 0x14, ExtenderDisplay = 0x15;

    /// <summary>Text on the display from a position (0-55 top row, 56-111 bottom row); plain ASCII only.</summary>
    public static MackieOutput Display(int offset, string text, bool extender = false)
    {
        var bytes = new List<byte> { 0xF0, 0x00, 0x00, 0x66, extender ? ExtenderDisplay : MainDisplay, 0x12, (byte)Math.Clamp(offset, 0, 111) };
        bytes.AddRange(Ascii(text));
        bytes.Add(0xF7);
        return new MackieOutput(0, [.. bytes]);
    }

    /// <summary>The second display of an iCON V1-M (the small one above the faders): 2 rows of 56 characters, like the main one.</summary>
    public static MackieOutput IconDisplay(int offset, string text)
    {
        var bytes = new List<byte> { 0xF0, 0x00, 0x02, 0x4E, 0x15, 0x13, (byte)Math.Clamp(offset, 0, 111) };
        bytes.AddRange(Ascii(text));
        bytes.Add(0xF7);
        return new MackieOutput(0, [.. bytes]);
    }

    /// <summary>The colour bar of an iCON V1-M: one colour per strip, 0-127 per component.</summary>
    public static MackieOutput IconColors(IReadOnlyList<MackieColor> colors)
    {
        var bytes = new List<byte> { 0xF0, 0x00, 0x02, 0x4E, 0x16, 0x14 };
        for (int i = 0; i < Strips; i++)
        {
            var c = i < colors.Count ? colors[i] : MackieColor.Off;
            bytes.AddRange([(byte)Math.Clamp(c.R, 0, 127), (byte)Math.Clamp(c.G, 0, 127), (byte)Math.Clamp(c.B, 0, 127)]);
        }
        bytes.Add(0xF7);
        return new MackieOutput(0, [.. bytes]);
    }

    /// <summary>
    /// One digit of the time display (digit 0 = rightmost, 9 = leftmost): a digit or letter, space for blank, with
    /// the dot after it lit or not.
    /// </summary>
    public static MackieOutput TimeDigit(int digit, char c, bool dot = false)
    {
        int value = c is >= '0' and <= '9' ? 0x30 + (c - '0')
            : c is >= 'A' and <= 'Z' ? c - 'A' + 1 : c is >= 'a' and <= 'z' ? c - 'a' + 1 : c == '-' ? 0x2D : 0x20;
        return new MackieOutput((uint)(0xB0 | ((0x40 + Math.Clamp(digit, 0, 9)) << 8) | ((value | (dot ? 0x40 : 0)) << 16)));
    }

    /// <summary>The master meter of an iCON V1-M (side 0 = left, 1 = right), 0-12 segments.</summary>
    public static MackieOutput MasterMeter(int side, int segments) =>
        new((uint)(0xD1 | (((side & 0x01) << 4 | Math.Clamp(segments, 0, 12)) << 8)));

    /// <summary>Minutes and seconds still to go ("325" for 3:25), or "" when nothing plays.</summary>
    public static string Countdown(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds)) return "";
        int total = (int)Math.Ceiling(seconds);
        return $"{total / 60}{total % 60:00}";
    }

    /// <summary>
    /// The time display of an iCON V1-M shows eight digits, digit 9 (left) to 2 (right), grouped 3.2.3 with fixed
    /// dots. A clock "HHmmss" goes on 8-3: " 14.25.07 ".
    /// </summary>
    public static IEnumerable<MackieOutput> Clock(string hhmmss)
    {
        string text = (" " + hhmmss.PadLeft(6)[..6] + "   ")[..10];   // positions 0-9 from the left = digits 9-0
        // The dots after the hours (digit 7) and the minutes (digit 5): " 14.25.07 ".
        for (int digit = 9; digit >= 0; digit--) yield return TimeDigit(digit, text[9 - digit], digit is 7 or 5);
    }

    /// <summary>A whole display row (56 characters), plain ASCII.</summary>
    public static string Row(string text)
    {
        var t = new string(Ascii(text).Select(b => (char)b).ToArray());
        return t.Length > 56 ? t[..56] : t.PadRight(56);
    }

    /// <summary>Seven characters per strip: six of the text and a space between strips.</summary>
    public static string Cell(string text)
    {
        var t = new string(Ascii(text).Select(b => (char)b).ToArray()).Trim();
        return (t.Length > 6 ? t[..6] : t).PadRight(6) + " ";
    }

    internal static IEnumerable<byte> Ascii(string text)
    {
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            yield return c is >= ' ' and <= '~' ? (byte)c : (byte)'?';
        }
    }

    /// <summary>Fader position for a volume 0..top (the studio fader goes to 125 %, a DJ deck fader to 100 %).</summary>
    public static int Position(float volume, float top = 1.25f) => (int)Math.Round(Math.Clamp(volume, 0f, top) / top * FaderMax);
    public static float Volume(int position, float top = 1.25f)
    {
        float v = Math.Clamp(position, 0, FaderMax) / (float)FaderMax * top;
        return Math.Abs(v - 1f) < 0.002f ? 1f : v;   // the 100 % step lands on 100 % exactly
    }
}

/// <summary>An item further on in the log, as the surface shows it.</summary>
public sealed record MackieLogItem(long Id, string Label);

/// <summary>
/// What the surface shows of one mixer channel. A DJ deck (<see cref="Deck"/> = "A"-"D") has its own buttons: the
/// fader is the deck fader (0..<see cref="Top"/> = 1), the encoder its gain (<see cref="Pan"/> = gain / 12 dB).
/// </summary>
public sealed record MackieStripState(string Id, string Name, float Volume, bool Muted, double Pan, bool OnAir, bool Recording, float Peak,
    string? Deck = null, bool Playing = false, bool Sync = false, bool Pfl = false, float Top = 1.25f,
    string? Track = null, double Left = 0);

/// <summary>A colour on the colour bar of an iCON panel, 0-127 per component.</summary>
public readonly record struct MackieColor(int R, int G, int B)
{
    public static readonly MackieColor Off = new(0, 0, 0);
    /// <summary>The same colour, darker: for a channel that is not on air.</summary>
    public MackieColor Dim() => new(R / 6, G / 6, B / 6);
}

/// <summary>What is on air: the item playing (null when nothing plays), the time it still has, and the programme level.</summary>
public sealed record MackieAir(string? Now, double Remaining, float Left, float Right);

/// <summary>What the strips of the panels are: the broadcast mixer, or the DJ desk with the decks on the first strips.</summary>
public enum MackieLayout { Broadcast, Dj }

/// <summary>The mixer as the surface sees it (the engine fills this in; the tests use a fake).</summary>
public interface IMackieMixer
{
    IReadOnlyList<MackieStripState> Strips();
    /// <summary>The strips of the DJ layout: deck A-D first, then the DJ master, mic, PFL and log.</summary>
    IReadOnlyList<MackieStripState> DjStrips();
    /// <summary>The DJ desk is on air (the panels then switch to the DJ layout by themselves).</summary>
    bool DjOnAir { get; }
    /// <summary>What plays now, for the screens of an iCON panel.</summary>
    MackieAir Air();
    void SetVolume(string id, float volume);
    void SetMute(string id, bool muted);
    void SetPan(string id, double pan);
    void SetOnAir(string id, bool onAir);
    void SetRecording(string id, bool recording);
    /// <summary>A command on the command bus (NEXT, STOP, AUTO TOGGLE); returns why it failed, or null when it worked.</summary>
    string? Run(string command);
    /// <summary>What comes next in the log, the next item first (browsed with the jog wheel).</summary>
    IReadOnlyList<MackieLogItem> Upcoming();
    /// <summary>Puts a log item on top and starts it now, mixing out of what plays (as NEXT does).</summary>
    void StartFromLog(long id);
    bool Playing { get; }
    bool AutoPlay { get; }
}

/// <summary>Choices for a Mackie Control surface (Instellingen › Hardware).</summary>
public sealed record MackieSettings
{
    /// <summary>The channel on the master fader ("" = none: the master fader does nothing).</summary>
    public string MasterStrip { get; init; } = "";
    /// <summary>Play, Stop and Cycle operate the broadcast (NEXT, STOP, AUTO). Off by default: a knock must not stop the station.</summary>
    public bool Transport { get; init; }
    /// <summary>Level meters on the surface.</summary>
    public bool Meters { get; init; } = true;
    /// <summary>Devices that are an extender (eight strips more, no master fader or transport), e.g. an iCON V1-X.</summary>
    public IReadOnlyList<long> Extenders { get; init; } = [];
    /// <summary>The extenders stand left of the main panel (their strips come first).</summary>
    public bool ExtendersLeft { get; init; }
    /// <summary>
    /// Fader start in the DJ layout: opening a deck's fader starts it, closing it pauses it; and a deck started on the
    /// screen with its fader closed gets its fader opened (the motor follows).
    /// </summary>
    public bool DjFaderStart { get; init; } = true;
}

/// <summary>
/// Where the panels stand together: the first mixer channel on the leftmost strip, and how many strips there are in
/// all. Shared by a main panel and its extenders, so Bank on one panel moves them all.
/// </summary>
public sealed class MackieBank
{
    private readonly object _gate = new();
    public int First { get; private set; }
    /// <summary>What the strips show; every panel of the group follows.</summary>
    public MackieLayout Layout { get; private set; }
    private bool? _djOnAir;
    private bool _followed;   // the DJ layout came from the desk going on air (not from FLIP)
    /// <summary>Strips of all panels together (8 per panel).</summary>
    public int Width { get; set; } = Mackie.Strips;
    /// <summary>Goes up on every move, so each panel knows it has to show everything again.</summary>
    public int Version { get; private set; }

    public void Move(int by, int channels)
    {
        lock (_gate)
        {
            First = Math.Clamp(First + by, 0, Math.Max(0, channels - Width));
            Version++;
        }
    }

    /// <summary>Another layout: the bank starts at the first strip again and everything is shown anew.</summary>
    public void SetLayout(MackieLayout layout) => Switch(layout, followed: false);

    private bool Switch(MackieLayout layout, bool followed)
    {
        lock (_gate)
        {
            if (layout == Layout) { if (!followed) _followed = false; return false; }   // a layout chosen by hand stays chosen by hand
            _followed = followed && layout == MackieLayout.Dj;
            Layout = layout;
            First = 0;
            Version++;
            return true;
        }
    }

    /// <summary>
    /// Follows the DJ desk: going on air gives the DJ layout; going off air gives the broadcast layout again, but only
    /// when the DJ layout came from going on air (one chosen with FLIP stays). Returns the change, or null.
    /// </summary>
    public string? Follow(bool djOnAir)
    {
        bool back;
        lock (_gate)
        {
            bool changed = (_djOnAir ?? false) != djOnAir;   // a desk that is on air already when the panel comes up counts too
            _djOnAir = djOnAir;
            if (!changed) return null;
            back = !djOnAir && _followed && Layout == MackieLayout.Dj;
        }
        if (djOnAir) return Switch(MackieLayout.Dj, followed: true) ? "indeling: DJ (DJ-tafel op de zender)" : null;
        return back && Switch(MackieLayout.Broadcast, followed: false) ? "indeling: uitzending (DJ-tafel van de zender)" : null;
    }

    /// <summary>Keeps the bank inside the channels (when channels come and go).</summary>
    public void Fit(int channels)
    {
        lock (_gate)
        {
            int f = Math.Clamp(First, 0, Math.Max(0, channels - Width));
            if (f != First) { First = f; Version++; }
        }
    }
}

/// <summary>
/// One Mackie Control surface on the mixer: maps eight strips onto a bank of mixer channels, turns what the surface
/// sends into mixer changes, and works out what to send back (fader positions, lamps, rings, meters, display) so the
/// surface follows the studio. Only changes are sent; <see cref="Refresh"/> with force sends everything again.
/// </summary>
public sealed class MackieSurface(IMackieMixer mixer, MackieSettings settings, MackieBank? bank = null)
{
    private readonly bool[] _touched = new bool[Mackie.Strips + 1];
    private readonly Dictionary<string, uint> _sent = [];
    private readonly Dictionary<int, string> _display = [];
    private int _version = -1;
    private long? _choice;
    private DateTime _choiceUntil;
    private readonly Dictionary<string, bool> _decksPlaying = [];
    private const float Closed = 0.001f, StartAt = 0.1f;
    private readonly HashSet<string> _closedDecks = [];   // deck faders seen closed: opening them past StartAt starts the deck

    /// <summary>How long an item chosen with the jog wheel stays on the display, and stays what PLAY starts.</summary>
    public static readonly TimeSpan ChoiceTime = TimeSpan.FromSeconds(8);
    /// <summary>What the surface did by itself (switched layout, opened a fader), for the list of what happened.</summary>
    public Action<string>? Log { get; set; }
    /// <summary>The local time for the clock on the time display (the tests set their own).</summary>
    public Func<DateTime> LocalTime { get; set; } = () => DateTime.Now;
    /// <summary>The time (the tests set their own).</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public MackieSettings Settings { get; set; } = settings;
    /// <summary>The bank shared with the other panels of this group.</summary>
    public MackieBank Group { get; } = bank ?? new MackieBank();
    /// <summary>The first strip of this panel within the group (0, 8, 16 …).</summary>
    public int Offset { get; set; }
    /// <summary>An extender: no master fader, no transport, its own display address.</summary>
    public bool Extender { get; set; }
    /// <summary>
    /// An iCON panel: the strips go on its second display (above the faders) and get a colour on its colour bar; the
    /// main display shows what plays and comes next, the time display counts down, the master meter shows the programme.
    /// </summary>
    public bool Icon { get; set; }
    /// <summary>The first mixer channel on strip 1 of this panel (0-based).</summary>
    public int Bank => Group.First + Offset;

    /// <summary>The strips of the layout the group is in.</summary>
    private IReadOnlyList<MackieStripState> All() => Group.Layout == MackieLayout.Dj ? mixer.DjStrips() : mixer.Strips();

    /// <summary>The channel on a strip (0-7), or null for an empty strip.</summary>
    public MackieStripState? StripAt(IReadOnlyList<MackieStripState> all, int strip) =>
        Bank + strip < all.Count ? all[Bank + strip] : null;

    /// <summary>Handles a message from the surface; returns what it did (for the hardware event list), or null.</summary>
    public string? Handle(uint message)
    {
        if (Mackie.Parse(message) is not { } input) return null;
        var all = All();
        switch (input)
        {
            case MackieTouch t:
                _touched[t.Strip] = t.Touched;
                if (!t.Touched) _sent.Remove(FaderKey(t.Strip));   // let go: put the motor where the channel really is
                return null;
            case MackieFader f when f.Strip == Mackie.MasterFader:
                if (Extender || Master(all) is not { } master) return null;
                mixer.SetVolume(master.Id, Mackie.Volume(f.Position, master.Top));
                _sent[FaderKey(f.Strip)] = (uint)f.Position;
                return null;
            case MackieFader f:
                if (StripAt(all, f.Strip) is not { } s) return null;
                float volume = Mackie.Volume(f.Position, s.Top);
                mixer.SetVolume(s.Id, volume);
                _sent[FaderKey(f.Strip)] = (uint)f.Position;
                return s.Deck is { } deck && Settings.DjFaderStart ? FaderStart(s, deck, volume) : null;
            case MackieVPot v:
                if (StripAt(all, v.Strip) is not { } p) return null;
                mixer.SetPan(p.Id, Math.Round(Math.Clamp(p.Pan + v.Delta * 0.05, -1, 1), 2));
                return null;
            case MackieJog j when Settings.Transport && !Extender:
                return Browse(j.Delta);
            case MackieButton { Pressed: true } b:
                return Press(b.Note, all);
        }
        return null;
    }

    /// <summary>
    /// Fader start: a closed deck fader that opens past <see cref="StartAt"/> starts the deck (a tap that moves it a
    /// little does not), one that closes pauses it.
    /// </summary>
    private string? FaderStart(MackieStripState s, string deck, float volume)
    {
        if (s.Volume <= Closed) _closedDecks.Add(deck);
        if (volume <= Closed)
        {
            _closedDecks.Add(deck);
            if (s.Volume <= Closed || !s.Playing) return null;
            _decksPlaying[deck] = false;
            return Deck(deck, "PAUSE", "fader dicht, pauze");
        }
        if (volume < StartAt || !_closedDecks.Remove(deck) || s.Playing) return null;
        _decksPlaying[deck] = true;   // started by the fader: no need to open it
        return Deck(deck, "PLAY", "faderstart");
    }

    /// <summary>The jog wheel walks through what comes next in the log; the display shows the choice for a while.</summary>
    private string? Browse(int delta)
    {
        if (delta == 0) return null;
        var log = mixer.Upcoming();
        if (log.Count == 0) { _choice = null; return "log: niets meer gepland"; }
        int at = Choice(log, out _);
        at = Math.Clamp((at < 0 ? -1 : at) + delta, 0, log.Count - 1);
        _choice = log[at].Id;
        _choiceUntil = Clock() + ChoiceTime;
        return $"log {at + 1}: {log[at].Label}";
    }

    /// <summary>Where the chosen item stands in the log (0 = next), or -1 when nothing is chosen (any more).</summary>
    private int Choice(IReadOnlyList<MackieLogItem> log, out MackieLogItem? item)
    {
        item = null;
        if (_choice is not { } id || Clock() >= _choiceUntil) { _choice = null; return -1; }
        for (int i = 0; i < log.Count; i++)
            if (log[i].Id == id) { item = log[i]; return i; }
        _choice = null;   // it played or was removed
        return -1;
    }

    /// <summary>PLAY: the item chosen with the jog wheel, otherwise the next one.</summary>
    private string Play()
    {
        int at = Choice(mixer.Upcoming(), out var item);
        _choice = null;
        if (at > 0 && item is not null) { mixer.StartFromLog(item.Id); return $"start {item.Label}"; }
        mixer.Run("NEXT");
        return "NEXT";
    }

    private string? Press(int note, IReadOnlyList<MackieStripState> all)
    {
        int strip = note & 0x07;
        if (note < Mackie.VPotPush && StripAt(all, strip) is { Deck: { } deck })
            return (note & ~0x07) switch
            {
                Mackie.Rec => Deck(deck, "CUE", "cue"),
                Mackie.Solo => Deck(deck, "PFL", "voorbeluisteren"),
                Mackie.Mute => Deck(deck, "TOGGLE", "spelen/pauze"),
                _ => Deck(deck, "SYNC", "sync"),
            };
        switch (note & ~0x07)
        {
            case Mackie.Rec when StripAt(all, strip) is { } s:
                mixer.SetRecording(s.Id, !s.Recording);
                return $"{s.Name}: opnemen {(s.Recording ? "uit" : "aan")}";
            case Mackie.Mute when StripAt(all, strip) is { } s:
                mixer.SetMute(s.Id, !s.Muted);
                return $"{s.Name}: {(s.Muted ? "niet meer gedempt" : "gedempt")}";
            case Mackie.Select when StripAt(all, strip) is { } s:
                mixer.SetOnAir(s.Id, !s.OnAir);
                return $"{s.Name}: {(s.OnAir ? "van de zender" : "naar de zender")}";
            case Mackie.VPotPush when StripAt(all, strip) is { } s:
                mixer.SetPan(s.Id, 0);
                return $"{s.Name}: balans in het midden";
        }
        switch (note)
        {
            case Mackie.Flip:
                Group.SetLayout(Group.Layout == MackieLayout.Dj ? MackieLayout.Broadcast : MackieLayout.Dj);
                return Group.Layout == MackieLayout.Dj ? "indeling: DJ" : "indeling: uitzending";
            case Mackie.BankLeft: return MoveBank(-Group.Width, all);
            case Mackie.BankRight: return MoveBank(Group.Width, all);
            case Mackie.ChannelLeft: return MoveBank(-1, all);
            case Mackie.ChannelRight: return MoveBank(1, all);
            case Mackie.Play when Settings.Transport && !Extender: return Play();
            case Mackie.Stop when Settings.Transport && !Extender: mixer.Run("STOP"); return "STOP";
            case Mackie.Cycle when Settings.Transport && !Extender: mixer.Run("AUTO TOGGLE"); return "AUTO TOGGLE";
        }
        // Not used (yet): named with its note, so a button on the panel can be found back.
        return $"knop {note:X2}h: geen functie";
    }

    private string Deck(string deck, string action, string what) =>
        mixer.Run($"DJ {deck} {action}") is { } problem ? $"deck {deck}: {what} lukt niet: {problem}" : $"deck {deck}: {what}";

    private string MoveBank(int by, IReadOnlyList<MackieStripState> all)
    {
        Group.Move(by, all.Count);
        return $"kanalen {Group.First + 1}-{Math.Min(all.Count, Group.First + Group.Width)}";
    }

    private MackieStripState? Master(IReadOnlyList<MackieStripState> all) =>
        Settings.MasterStrip.Length == 0 ? null : all.FirstOrDefault(s => s.Id == Settings.MasterStrip);

    private static string FaderKey(int strip) => "fader" + strip;

    /// <summary>What to send so the surface shows the mixer as it is now (only changes, unless forced).</summary>
    public List<MackieOutput> Refresh(bool force = false)
    {
        if (Group.Follow(mixer.DjOnAir) is { } switched) Log?.Invoke(switched);
        var all = All();
        Group.Fit(all.Count);
        if (force || _version != Group.Version) { _sent.Clear(); _display.Clear(); _decksPlaying.Clear(); _version = Group.Version; }
        if (Settings.DjFaderStart && Group.Layout == MackieLayout.Dj && !Extender && OpenStartedDecks(all)) all = All();
        var outs = new List<MackieOutput>();
        void Send(string key, MackieOutput o, uint value)
        {
            if (_sent.TryGetValue(key, out var was) && was == value) return;
            _sent[key] = value;
            outs.Add(o);
        }
        void Lamp(int note, bool on) => Send("led" + note, Mackie.Led(note, on), on ? 1u : 0u);

        for (int i = 0; i < Mackie.Strips; i++)
        {
            var s = StripAt(all, i);
            if (!_touched[i]) { int pos = s is null ? 0 : Mackie.Position(s.Volume, s.Top); Send(FaderKey(i), Mackie.Fader(i, pos), (uint)pos); }
            bool deck = s?.Deck is not null;
            Lamp(Mackie.Rec + i, !deck && s?.Recording == true);
            Lamp(Mackie.Mute + i, deck ? s!.Playing : s?.Muted == true);
            Lamp(Mackie.Select + i, deck ? s!.Sync : s?.OnAir == true);
            Lamp(Mackie.Solo + i, deck && s!.Pfl);
            var ring = s is null ? new MackieOutput((uint)(0xB0 | ((Mackie.RingCc + i) << 8))) : Mackie.PanRing(i, s.Pan);
            Send("ring" + i, ring, ring.Short);
        }
        var master = Extender ? null : Master(all);
        if (!_touched[Mackie.MasterFader] && !Extender)
        {
            int mp = master is null ? 0 : Mackie.Position(master.Volume, master.Top);
            Send(FaderKey(Mackie.MasterFader), Mackie.Fader(Mackie.MasterFader, mp), (uint)mp);
        }
        if (!Extender)
        {
            Lamp(Mackie.Play, Settings.Transport && mixer.Playing);
            Lamp(Mackie.Cycle, Settings.Transport && mixer.AutoPlay);
            Lamp(Mackie.Flip, Group.Layout == MackieLayout.Dj);
        }

        // Display: names on top, the fader in percent (or DEMP when muted) below. While an item is chosen with the jog
        // wheel the top row shows that item instead.
        var top = new StringBuilder();
        var bottom = new StringBuilder();
        for (int i = 0; i < Mackie.Strips; i++)
        {
            var s = StripAt(all, i);
            top.Append(Mackie.Cell(s?.Name ?? ""));
            string playing = s?.Deck is not null && s.Playing ? ">" : "";
            bottom.Append(Mackie.Cell(s is null ? "" : s.Muted ? "DEMP" : $"{playing}{Math.Round(s.Volume * 100):0}%".PadLeft(5)));
        }
        string topRow = top.ToString(), bottomRow = bottom.ToString();
        string? choice = null;
        if (!Extender && _choice is not null)
        {
            var log = mixer.Upcoming();
            if (Choice(log, out var item) is int at and >= 0 && item is not null)
                choice = Mackie.Row($"{(at == 0 ? "Volgende" : $"{at + 1}/{log.Count}")}: {item.Label}");
        }
        void Show(int key, string text, Func<string, MackieOutput> message)
        {
            if (_display.TryGetValue(key, out var was) && was == text) return;
            _display[key] = text;
            outs.Add(message(text));
        }
        if (Icon && !Extender)
        {
            // The strips go on the display above the faders; the main display is for what plays and what comes next.
            Show(IconRow, topRow, t => Mackie.IconDisplay(0, t));
            Show(IconRow + 56, bottomRow, t => Mackie.IconDisplay(56, t));
            // The main display: per fader what plays on that channel, the time left on top, the title scrolling below.
            var times = new StringBuilder();
            var titles = new StringBuilder();
            for (int i = 0; i < Mackie.Strips; i++)
            {
                var s = StripAt(all, i);
                string track = s?.Track is { Length: > 0 } t ? t : "";
                string c = track.Length > 0 ? Mackie.Countdown(s!.Left) : "";
                times.Append(Mackie.Cell(c.Length > 0 ? $"-{c[..^2]}:{c[^2..]}" : ""));
                titles.Append(Scroll(i, track, 6) + " ");
            }
            Show(0, times.ToString(), t => Mackie.Display(0, t));
            Show(56, choice ?? titles.ToString(), t => Mackie.Display(56, t));
            // The clock on the red time display: " 14.25.07 ".
            string clock = LocalTime().ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            if (!_display.TryGetValue(TimeKey, out var shown) || shown != clock)
            {
                _display[TimeKey] = clock;
                outs.AddRange(Mackie.Clock(clock));
            }
        }
        else
        {
            Show(0, choice ?? topRow, t => Mackie.Display(0, t, Extender));
            Show(56, bottomRow, t => Mackie.Display(56, t, Extender));
        }
        if (Icon)
        {
            var colors = Enumerable.Range(0, Mackie.Strips).Select(i => ColorOf(StripAt(all, i))).ToList();
            Show(ColorKey, string.Join(",", colors), _ => Mackie.IconColors(colors));
        }
        return outs;
    }

    private const int IconRow = 1000, TimeKey = 2000, ColorKey = 3000;

    private static string Fit(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width)];

    private readonly Dictionary<int, (string Text, DateTime Start)> _scrolls = [];
    /// <summary>How fast a title that does not fit scrolls by: one character per step.</summary>
    public static readonly TimeSpan ScrollStep = TimeSpan.FromMilliseconds(300);
    /// <summary>How long a new title stands still before it starts to scroll.</summary>
    public static readonly TimeSpan ScrollWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A text in a window of <paramref name="width"/> characters (one window per <paramref name="key"/>): as it is when it fits, otherwise scrolling by like a
    /// news ticker (it starts again from the beginning when the text changes).
    /// </summary>
    private string Scroll(int key, string text, int width)
    {
        width = Math.Max(0, width);
        text = new string(Mackie.Ascii(text).Select(b => (char)b).ToArray());
        if (text.Length <= width) { _scrolls.Remove(key); return text.PadRight(width); }
        if (!_scrolls.TryGetValue(key, out var was) || was.Text != text) _scrolls[key] = was = (text, Clock());
        var since = Clock() - was.Start - ScrollWait;
        string loop = text + "   ";
        int at = since <= TimeSpan.Zero ? 0 : (int)(since.Ticks / ScrollStep.Ticks % loop.Length);
        return (loop + loop)[at..(at + width)];
    }

    /// <summary>
    /// The colour of a strip on the colour bar: by kind of channel, dark when it is not on air (or muted), a deck
    /// bright while it plays.
    /// </summary>
    public static MackieColor ColorOf(MackieStripState? s)
    {
        if (s is null) return MackieColor.Off;
        var c = (s.Deck is not null ? "deck" : s.Id) switch
        {
            "log" => new MackieColor(0, 40, 127),
            "standby" => new MackieColor(0, 110, 127),
            "cart" => new MackieColor(127, 60, 0),
            "overlay" => new MackieColor(127, 120, 0),
            "dj" or "deck" => new MackieColor(90, 0, 127),
            "mic" => new MackieColor(127, 0, 0),
            "voip1" or "voip2" => new MackieColor(0, 127, 30),
            "pfl" => new MackieColor(127, 127, 127),
            _ => new MackieColor(60, 60, 60),
        };
        bool lit = s.Deck is not null ? s.Playing : s.OnAir && !s.Muted;
        return lit ? c : c.Dim();
    }

    /// <summary>A deck started on the screen while its fader was closed gets its fader opened (fader start the other way round).</summary>
    private bool OpenStartedDecks(IReadOnlyList<MackieStripState> all)
    {
        bool opened = false;
        foreach (var s in all.ToList())
        {
            if (s.Deck is not { } deck) continue;
            // Only a start seen here counts: a deck that already played when the layout came up keeps its fader.
            bool known = _decksPlaying.TryGetValue(deck, out bool was);
            _decksPlaying[deck] = s.Playing;
            if (known && s.Playing && !was && s.Volume <= Closed)
            {
                mixer.SetVolume(s.Id, s.Top);
                Log?.Invoke($"deck {deck}: gestart, fader open");
                opened = true;
            }
        }
        return opened;
    }

    /// <summary>Level meters: sent every time (the surface lets them fall by itself).</summary>
    public List<MackieOutput> Meters()
    {
        if (!Settings.Meters) return [];
        var all = All();
        var outs = new List<MackieOutput>();
        for (int i = 0; i < Mackie.Strips; i++)
            if (StripAt(all, i) is { } s) outs.Add(Mackie.Meter(i, Mackie.Segments(s.Peak)));
        if (Icon && !Extender)
        {
            var air = mixer.Air();
            outs.Add(Mackie.MasterMeter(0, Mackie.Segments(air.Left)));
            outs.Add(Mackie.MasterMeter(1, Mackie.Segments(air.Right)));
        }
        return outs;
    }
}
