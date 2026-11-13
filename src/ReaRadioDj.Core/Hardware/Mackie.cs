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
    public const int BankLeft = 0x2E, BankRight = 0x2F, ChannelLeft = 0x30, ChannelRight = 0x31;
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

    /// <summary>Seven characters per strip: six of the text and a space between strips.</summary>
    public static string Cell(string text)
    {
        var t = new string(Ascii(text).Select(b => (char)b).ToArray()).Trim();
        return (t.Length > 6 ? t[..6] : t).PadRight(6) + " ";
    }

    private static IEnumerable<byte> Ascii(string text)
    {
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            yield return c is >= ' ' and <= '~' ? (byte)c : (byte)'?';
        }
    }

    /// <summary>Fader position for a volume 0..1.25 (the studio fader goes to 125 %).</summary>
    public static int Position(float volume) => (int)Math.Round(Math.Clamp(volume, 0f, 1.25f) / 1.25f * FaderMax);
    public static float Volume(int position)
    {
        float v = Math.Clamp(position, 0, FaderMax) / (float)FaderMax * 1.25f;
        return Math.Abs(v - 1f) < 0.002f ? 1f : v;   // the 100 % step lands on 100 % exactly
    }
}

/// <summary>What the surface shows of one mixer channel.</summary>
public sealed record MackieStripState(string Id, string Name, float Volume, bool Muted, double Pan, bool OnAir, bool Recording, float Peak);

/// <summary>The mixer as the surface sees it (the engine fills this in; the tests use a fake).</summary>
public interface IMackieMixer
{
    IReadOnlyList<MackieStripState> Strips();
    void SetVolume(string id, float volume);
    void SetMute(string id, bool muted);
    void SetPan(string id, double pan);
    void SetOnAir(string id, bool onAir);
    void SetRecording(string id, bool recording);
    /// <summary>A command on the command bus (NEXT, STOP, AUTO TOGGLE).</summary>
    void Run(string command);
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
}

/// <summary>
/// Where the panels stand together: the first mixer channel on the leftmost strip, and how many strips there are in
/// all. Shared by a main panel and its extenders, so Bank on one panel moves them all.
/// </summary>
public sealed class MackieBank
{
    private readonly object _gate = new();
    public int First { get; private set; }
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

    public MackieSettings Settings { get; set; } = settings;
    /// <summary>The bank shared with the other panels of this group.</summary>
    public MackieBank Group { get; } = bank ?? new MackieBank();
    /// <summary>The first strip of this panel within the group (0, 8, 16 …).</summary>
    public int Offset { get; set; }
    /// <summary>An extender: no master fader, no transport, its own display address.</summary>
    public bool Extender { get; set; }
    /// <summary>The first mixer channel on strip 1 of this panel (0-based).</summary>
    public int Bank => Group.First + Offset;

    /// <summary>The channel on a strip (0-7), or null for an empty strip.</summary>
    public MackieStripState? StripAt(IReadOnlyList<MackieStripState> all, int strip) =>
        Bank + strip < all.Count ? all[Bank + strip] : null;

    /// <summary>Handles a message from the surface; returns what it did (for the hardware event list), or null.</summary>
    public string? Handle(uint message)
    {
        if (Mackie.Parse(message) is not { } input) return null;
        var all = mixer.Strips();
        switch (input)
        {
            case MackieTouch t:
                _touched[t.Strip] = t.Touched;
                if (!t.Touched) _sent.Remove(FaderKey(t.Strip));   // let go: put the motor where the channel really is
                return null;
            case MackieFader f when f.Strip == Mackie.MasterFader:
                if (Extender || Master(all) is not { } master) return null;
                mixer.SetVolume(master.Id, Mackie.Volume(f.Position));
                _sent[FaderKey(f.Strip)] = (uint)f.Position;
                return null;
            case MackieFader f:
                if (StripAt(all, f.Strip) is not { } s) return null;
                mixer.SetVolume(s.Id, Mackie.Volume(f.Position));
                _sent[FaderKey(f.Strip)] = (uint)f.Position;
                return null;
            case MackieVPot v:
                if (StripAt(all, v.Strip) is not { } p) return null;
                mixer.SetPan(p.Id, Math.Round(Math.Clamp(p.Pan + v.Delta * 0.05, -1, 1), 2));
                return null;
            case MackieButton { Pressed: true } b:
                return Press(b.Note, all);
        }
        return null;
    }

    private string? Press(int note, IReadOnlyList<MackieStripState> all)
    {
        int strip = note & 0x07;
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
            case Mackie.BankLeft: return MoveBank(-Group.Width, all);
            case Mackie.BankRight: return MoveBank(Group.Width, all);
            case Mackie.ChannelLeft: return MoveBank(-1, all);
            case Mackie.ChannelRight: return MoveBank(1, all);
            case Mackie.Play when Settings.Transport && !Extender: mixer.Run("NEXT"); return "NEXT";
            case Mackie.Stop when Settings.Transport && !Extender: mixer.Run("STOP"); return "STOP";
            case Mackie.Cycle when Settings.Transport && !Extender: mixer.Run("AUTO TOGGLE"); return "AUTO TOGGLE";
        }
        return null;
    }

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
        var all = mixer.Strips();
        Group.Fit(all.Count);
        if (force || _version != Group.Version) { _sent.Clear(); _display.Clear(); _version = Group.Version; }
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
            if (!_touched[i]) { int pos = s is null ? 0 : Mackie.Position(s.Volume); Send(FaderKey(i), Mackie.Fader(i, pos), (uint)pos); }
            Lamp(Mackie.Rec + i, s?.Recording == true);
            Lamp(Mackie.Mute + i, s?.Muted == true);
            Lamp(Mackie.Select + i, s?.OnAir == true);
            Lamp(Mackie.Solo + i, false);
            var ring = s is null ? new MackieOutput((uint)(0xB0 | ((Mackie.RingCc + i) << 8))) : Mackie.PanRing(i, s.Pan);
            Send("ring" + i, ring, ring.Short);
        }
        var master = Extender ? null : Master(all);
        if (!_touched[Mackie.MasterFader] && !Extender)
        {
            int mp = master is null ? 0 : Mackie.Position(master.Volume);
            Send(FaderKey(Mackie.MasterFader), Mackie.Fader(Mackie.MasterFader, mp), (uint)mp);
        }
        if (!Extender)
        {
            Lamp(Mackie.Play, Settings.Transport && mixer.Playing);
            Lamp(Mackie.Cycle, Settings.Transport && mixer.AutoPlay);
        }

        // Display: names on top, the fader in percent (or DEMP when muted) below.
        var top = new StringBuilder();
        var bottom = new StringBuilder();
        for (int i = 0; i < Mackie.Strips; i++)
        {
            var s = StripAt(all, i);
            top.Append(Mackie.Cell(s?.Name ?? ""));
            bottom.Append(Mackie.Cell(s is null ? "" : s.Muted ? "DEMP" : $"{Math.Round(s.Volume * 100):0}%".PadLeft(5)));
        }
        foreach (var (offset, text) in new[] { (0, top.ToString()), (56, bottom.ToString()) })
        {
            if (_display.TryGetValue(offset, out var was) && was == text) continue;
            _display[offset] = text;
            outs.Add(Mackie.Display(offset, text, Extender));
        }
        return outs;
    }

    /// <summary>Level meters: sent every time (the surface lets them fall by itself).</summary>
    public List<MackieOutput> Meters()
    {
        if (!Settings.Meters) return [];
        var all = mixer.Strips();
        var outs = new List<MackieOutput>();
        for (int i = 0; i < Mackie.Strips; i++)
            if (StripAt(all, i) is { } s) outs.Add(Mackie.Meter(i, Mackie.Segments(s.Peak)));
        return outs;
    }
}
