using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Tests;

public class MackieTests
{
    private sealed class FakeMixer : IMackieMixer
    {
        public List<MackieStripState> List = Enumerable.Range(1, 10)
            .Select(i => new MackieStripState($"s{i}", $"Kanaal{i}", 1f, false, 0, true, false, 0)).ToList();
        public List<string> Commands = [];
        public IReadOnlyList<MackieStripState> Strips() => List;
        public List<MackieStripState> Dj = new[] { "A", "B", "C", "D" }
            .Select(d => new MackieStripState("deck:" + d, "Deck " + d, 0f, false, 0, false, false, 0, d, Top: 1f))
            .Append(new MackieStripState("dj", "DJ", 1f, false, 0, true, false, 0)).ToList();
        public IReadOnlyList<MackieStripState> DjStrips() => Dj;
        public bool DjOnAir { get; set; }
        public MackieAir OnAir = new("Artiest X - Nu", 205, 1f, 0.01f);
        public MackieAir Air() => OnAir;
        private void Change(string id, Func<MackieStripState, MackieStripState> f)
        {
            var list = List.Any(s => s.Id == id) ? List : Dj;
            int i = list.FindIndex(s => s.Id == id);
            list[i] = f(list[i]);
        }
        public void SetVolume(string id, float v) => Change(id, s => s with { Volume = v });
        public void SetMute(string id, bool m) => Change(id, s => s with { Muted = m });
        public void SetPan(string id, double p) => Change(id, s => s with { Pan = p });
        public void SetOnAir(string id, bool o) => Change(id, s => s with { OnAir = o });
        public void SetRecording(string id, bool r) => Change(id, s => s with { Recording = r });
        public string? Fails;
        public string? Run(string command) { Commands.Add(command); return Fails; }
        public List<MackieLogItem> Log = [new(11, "Artiest A - Een"), new(12, "Artiest B - Twee"), new(13, "Artiest C - Drie")];
        public IReadOnlyList<MackieLogItem> Upcoming() => Log;
        public void StartFromLog(long id) => Commands.Add($"START {id}");
        public bool Playing { get; set; }
        public bool AutoPlay { get; set; }
    }

    private static uint Msg(int status, int d1, int d2) => (uint)(status | (d1 << 8) | (d2 << 16));
    private static uint Bend(int ch, int pos) => Msg(0xE0 | ch, pos & 0x7F, pos >> 7);
    private static uint Note(int n, bool on) => Msg(0x90, n, on ? 127 : 0);

    [Fact]
    public void Messages_from_the_surface_are_read()
    {
        Assert.Equal(new MackieFader(2, 16383), Mackie.Parse(Bend(2, 16383)));
        Assert.Equal(new MackieFader(8, 8192), Mackie.Parse(Bend(8, 8192)));
        Assert.Equal(new MackieTouch(0, true), Mackie.Parse(Note(0x68, true)));
        Assert.Equal(new MackieTouch(8, false), Mackie.Parse(Note(0x70, false)));
        Assert.Equal(new MackieVPot(3, 2), Mackie.Parse(Msg(0xB0, 0x13, 0x02)));
        Assert.Equal(new MackieVPot(3, -1), Mackie.Parse(Msg(0xB0, 0x13, 0x41)));
        Assert.Equal(new MackieButton(Mackie.Mute + 1, true), Mackie.Parse(Note(0x11, true)));
        Assert.Equal(new MackieJog(-3), Mackie.Parse(Msg(0xB0, 0x3C, 0x43)));
        Assert.Null(Mackie.Parse(Msg(0x91, 0x10, 127)));   // another MIDI channel
        Assert.Null(Mackie.Parse(0xF8));                    // clock
    }

    [Fact]
    public void Messages_to_the_surface_are_built()
    {
        Assert.Equal(Bend(1, 16383), Mackie.Fader(1, 99999).Short);
        Assert.Equal(Note(0x5E, true), Mackie.Led(0x5E, true).Short);
        Assert.Equal(Msg(0x90, 0x10, 0x01), Mackie.Led(0x10, false, flash: true).Short);
        Assert.Equal(Msg(0xD0, 0x2C, 0), Mackie.Meter(2, 12).Short);
        Assert.Equal(Msg(0xB0, 0x30, 6), Mackie.PanRing(0, 0).Short);
        Assert.Equal(Msg(0xB0, 0x31, 1), Mackie.PanRing(1, -1).Short);
        Assert.Equal(Msg(0xB0, 0x31, 11), Mackie.PanRing(1, 1).Short);
        var lcd = Mackie.Display(56, "Hé!")!.SysEx!;
        Assert.Equal(new byte[] { 0xF0, 0x00, 0x00, 0x66, 0x14, 0x12, 56, (byte)'H', (byte)'e', (byte)'!', 0xF7 }, lcd);
        Assert.Equal("Automa ", Mackie.Cell("Automatisering"));
        Assert.Equal("Mic    ", Mackie.Cell("Mic"));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.0005f, 0)]
    [InlineData(1f, 12)]
    [InlineData(0.5f, 8)]    // -6.02 dB: just under the -6 dB segment
    [InlineData(0.011f, 3)]  // about -39 dB
    public void Meter_segments_follow_the_level(float peak, int segments) => Assert.Equal(segments, Mackie.Segments(peak));

    [Fact]
    public void Fader_positions_cover_the_studio_fader()
    {
        Assert.Equal(16383, Mackie.Position(1.25f));
        Assert.Equal(13106, Mackie.Position(1f));
        Assert.Equal(1f, Mackie.Volume(Mackie.Position(1f)), 3);
    }

    [Fact]
    public void A_fader_sets_the_channel_and_is_not_sent_back()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Refresh();
        surface.Handle(Note(0x68, true));
        surface.Handle(Bend(0, 8192));
        Assert.Equal(0.625f, mixer.List[0].Volume, 3);
        Assert.DoesNotContain(surface.Refresh(), o => o.Short == Bend(0, 8192) || (o.Short & 0xFF) == 0xE0);
    }

    [Fact]
    public void A_change_in_the_studio_moves_the_motor_unless_the_fader_is_held()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Refresh();
        mixer.SetVolume("s2", 0.5f);
        Assert.Contains(surface.Refresh(), o => o.Short == Bend(1, Mackie.Position(0.5f)));

        surface.Handle(Note(0x69, true));     // held
        mixer.SetVolume("s2", 0.25f);
        Assert.DoesNotContain(surface.Refresh(), o => (o.Short & 0xFF) == 0xE1);
        surface.Handle(Note(0x69, false));    // let go: back to where the channel is
        Assert.Contains(surface.Refresh(), o => o.Short == Bend(1, Mackie.Position(0.25f)));
    }

    [Fact]
    public void Buttons_switch_the_channel_and_light_up()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Refresh();
        Assert.Contains("gedempt", surface.Handle(Note(Mackie.Mute + 2, true)));
        surface.Handle(Note(Mackie.Mute + 2, false));
        Assert.True(mixer.List[2].Muted);
        surface.Handle(Note(Mackie.Rec, true));
        surface.Handle(Note(Mackie.Select + 1, true));
        Assert.True(mixer.List[0].Recording);
        Assert.False(mixer.List[1].OnAir);
        var outs = surface.Refresh();
        Assert.Contains(outs, o => o.Short == Mackie.Led(Mackie.Mute + 2, true).Short);
        Assert.Contains(outs, o => o.Short == Mackie.Led(Mackie.Rec, true).Short);
        Assert.Contains(outs, o => o.Short == Mackie.Led(Mackie.Select + 1, false).Short);
    }

    [Fact]
    public void An_encoder_turns_the_balance_and_a_push_centres_it()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Handle(Msg(0xB0, 0x10, 0x04));
        Assert.Equal(0.2, mixer.List[0].Pan, 2);
        surface.Handle(Msg(0xB0, 0x10, 0x7F));   // 63 ticks back: stops at the left
        Assert.Equal(-1, mixer.List[0].Pan, 2);
        surface.Handle(Note(Mackie.VPotPush, true));
        Assert.Equal(0, mixer.List[0].Pan, 2);
    }

    [Fact]
    public void Banks_move_over_the_channels()
    {
        var mixer = new FakeMixer();   // ten channels
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Refresh();
        Assert.Equal("kanalen 3-10", surface.Handle(Note(Mackie.BankRight, true)));
        Assert.Equal(2, surface.Bank);   // never past the last channel
        surface.Handle(Bend(0, 0));
        Assert.Equal(0f, mixer.List[2].Volume);
        var top = surface.Refresh().First(o => o.SysEx is { } x && x[6] == 0).SysEx!;
        Assert.StartsWith("Kanaal3 "[..6], System.Text.Encoding.ASCII.GetString(top, 7, 6));
        surface.Handle(Note(Mackie.ChannelLeft, true));
        Assert.Equal(1, surface.Bank);
        surface.Handle(Note(Mackie.BankLeft, true));
        Assert.Equal(0, surface.Bank);
    }

    [Fact]
    public void Transport_and_master_are_off_unless_chosen()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Handle(Note(Mackie.Stop, true));
        surface.Handle(Note(Mackie.Play, true));
        surface.Handle(Bend(8, 0));
        Assert.Empty(mixer.Commands);
        Assert.All(mixer.List, s => Assert.Equal(1f, s.Volume));

        surface.Settings = new MackieSettings { Transport = true, MasterStrip = "s5" };
        surface.Handle(Note(Mackie.Play, true));
        surface.Handle(Note(Mackie.Stop, true));
        surface.Handle(Note(Mackie.Cycle, true));
        Assert.Equal(["NEXT", "STOP", "AUTO TOGGLE"], mixer.Commands);
        surface.Handle(Bend(8, 0));
        Assert.Equal(0f, mixer.List[4].Volume);
    }

    private static uint Jog(int ticks) => Msg(0xB0, Mackie.JogCc, ticks < 0 ? 0x40 | -ticks : ticks);

    private static string TopRow(List<MackieOutput> outs) =>
        outs.Where(o => o.SysEx is { } x && x[6] == 0).Select(o => new string(o.SysEx![7..^1].Select(b => (char)b).ToArray())).LastOrDefault() ?? "";

    [Fact]
    public void The_jog_wheel_browses_the_log_and_play_starts_the_choice()
    {
        var mixer = new FakeMixer();
        var now = new DateTime(2026, 11, 15, 10, 0, 0);
        var surface = new MackieSurface(mixer, new MackieSettings { Transport = true }) { Clock = () => now };
        surface.Refresh();

        Assert.Equal("log 1: Artiest A - Een", surface.Handle(Jog(1)));
        Assert.StartsWith("Volgende: Artiest A - Een", TopRow(surface.Refresh()));
        Assert.Equal("log 3: Artiest C - Drie", surface.Handle(Jog(5)));   // stops at the end
        Assert.Equal("log 2: Artiest B - Twee", surface.Handle(Jog(-1)));
        Assert.StartsWith("2/3: Artiest B - Twee", TopRow(surface.Refresh()));

        Assert.Equal("start Artiest B - Twee", surface.Handle(Note(Mackie.Play, true)));
        Assert.Equal(["START 12"], mixer.Commands);
        Assert.StartsWith("Kanaal", TopRow(surface.Refresh()));   // the names are back

        surface.Handle(Note(Mackie.Play, true));                  // nothing chosen: the next item, as before
        Assert.Equal(["START 12", "NEXT"], mixer.Commands);
    }

    [Fact]
    public void A_choice_with_the_jog_wheel_runs_out()
    {
        var mixer = new FakeMixer();
        var now = new DateTime(2026, 11, 15, 10, 0, 0);
        var surface = new MackieSurface(mixer, new MackieSettings { Transport = true }) { Clock = () => now };
        surface.Handle(Jog(3));
        now += MackieSurface.ChoiceTime;
        Assert.StartsWith("Kanaal", TopRow(surface.Refresh()));
        surface.Handle(Note(Mackie.Play, true));
        Assert.Equal(["NEXT"], mixer.Commands);   // not the item chosen a while ago

        surface.Handle(Jog(2));
        mixer.Log.RemoveAt(1);                    // the chosen item played or was removed
        surface.Handle(Note(Mackie.Play, true));
        Assert.Equal(["NEXT", "NEXT"], mixer.Commands);
    }

    [Fact]
    public void The_jog_wheel_does_nothing_without_transport()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        Assert.Null(surface.Handle(Jog(1)));
        mixer.Log.Clear();
        surface.Settings = new MackieSettings { Transport = true };
        Assert.Equal("log: niets meer gepland", surface.Handle(Jog(1)));
    }

    [Fact]
    public void Only_changes_are_sent_and_force_sends_everything()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        var first = surface.Refresh();
        Assert.Contains(first, o => o.SysEx is not null);
        Assert.Empty(surface.Refresh());
        Assert.Equal(first.Count, surface.Refresh(force: true).Count);
    }

    [Fact]
    public void Meters_are_sent_for_the_channels_on_the_strips()
    {
        var mixer = new FakeMixer();
        mixer.List[0] = mixer.List[0] with { Peak = 1f };
        var surface = new MackieSurface(mixer, new MackieSettings());
        var meters = surface.Meters();
        Assert.Equal(8, meters.Count);
        Assert.Equal(Mackie.Meter(0, 12).Short, meters[0].Short);
        surface.Settings = new MackieSettings { Meters = false };
        Assert.Empty(surface.Meters());
    }

    private static (FakeMixer Mixer, MackieSurface Main, MackieSurface Xt) Pair(int channels, bool extenderLeft)
    {
        var mixer = new FakeMixer();
        mixer.List = Enumerable.Range(1, channels).Select(i => new MackieStripState($"s{i}", $"K{i}", 1f, false, 0, true, false, 0)).ToList();
        var bank = new MackieBank { Width = 16 };
        var settings = new MackieSettings { Transport = true, MasterStrip = "s1" };
        var main = new MackieSurface(mixer, settings, bank) { Offset = extenderLeft ? 8 : 0 };
        var xt = new MackieSurface(mixer, settings, bank) { Offset = extenderLeft ? 0 : 8, Extender = true };
        return (mixer, main, xt);
    }

    [Fact]
    public void An_extender_takes_the_next_eight_channels()
    {
        var (mixer, main, xt) = Pair(20, extenderLeft: false);
        xt.Handle(Bend(0, 0));      // first fader of the extender = channel 9
        Assert.Equal(0f, mixer.List[8].Volume);
        Assert.Equal(1f, mixer.List[0].Volume);
        Assert.Equal(new byte[] { 0xF0, 0x00, 0x00, 0x66, 0x15, 0x12, 0 }, xt.Refresh().First(o => o.SysEx is not null).SysEx![..7]);
        Assert.Equal(0x14, main.Refresh().First(o => o.SysEx is not null).SysEx![4]);
    }

    [Fact]
    public void An_extender_on_the_left_comes_first()
    {
        var (mixer, main, xt) = Pair(20, extenderLeft: true);
        main.Handle(Bend(0, 0));    // first fader of the main panel = channel 9
        xt.Handle(Bend(7, 0));      // last fader of the extender = channel 8
        Assert.Equal(0f, mixer.List[8].Volume);
        Assert.Equal(0f, mixer.List[7].Volume);
    }

    [Fact]
    public void Bank_on_either_panel_moves_both()
    {
        var (mixer, main, xt) = Pair(20, extenderLeft: false);
        main.Refresh(); xt.Refresh();
        Assert.Equal("kanalen 5-20", xt.Handle(Note(Mackie.BankRight, true)));   // 16 further, but never past the last channel
        Assert.Equal(4, main.Bank);
        Assert.Equal(12, xt.Bank);
        // both panels show their new channels again
        Assert.Contains(main.Refresh(), o => o.SysEx is not null);
        Assert.Contains(xt.Refresh(), o => o.SysEx is not null);
        main.Handle(Note(Mackie.ChannelLeft, true));
        Assert.Equal(3, main.Bank);
        Assert.Equal(11, xt.Bank);
    }

    [Fact]
    public void An_extender_has_no_master_fader_or_transport()
    {
        var (mixer, main, xt) = Pair(20, extenderLeft: false);
        xt.Handle(Note(Mackie.Stop, true));
        xt.Handle(Bend(8, 0));
        Assert.Empty(mixer.Commands);
        Assert.Equal(1f, mixer.List[0].Volume);
        Assert.DoesNotContain(xt.Refresh(), o => (o.Short & 0xFF) == 0xE8 || o.Short == Mackie.Led(Mackie.Play, false).Short);
        main.Handle(Bend(8, 0));
        Assert.Equal(0f, mixer.List[0].Volume);
    }

    // ---------------------------------------------------------------- DJ layout

    private static (MackieSurface Surface, FakeMixer Mixer) DjPanel(bool faderStart = true)
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings { DjFaderStart = faderStart });
        surface.Refresh(force: true);
        Assert.Equal("indeling: DJ", surface.Handle(Note(Mackie.Flip, true)));
        surface.Refresh();
        return (surface, mixer);
    }

    [Fact]
    public void Flip_puts_the_decks_on_the_first_strips()
    {
        var (surface, _) = DjPanel();
        Assert.Equal(MackieLayout.Dj, surface.Group.Layout);
        var outs = surface.Refresh(force: true);
        Assert.Contains(outs, o => o.SysEx is { } b && System.Text.Encoding.ASCII.GetString(b).Contains("Deck A Deck B"));
        Assert.Contains(Mackie.Led(Mackie.Flip, true), outs);
        Assert.Equal("indeling: uitzending", surface.Handle(Note(Mackie.Flip, true)));
        Assert.Equal(MackieLayout.Broadcast, surface.Group.Layout);
    }

    [Fact]
    public void The_buttons_of_a_deck_strip_operate_the_deck()
    {
        var (surface, mixer) = DjPanel();
        surface.Handle(Note(Mackie.Mute + 0, true));
        surface.Handle(Note(Mackie.Select + 1, true));
        surface.Handle(Note(Mackie.Solo + 2, true));
        surface.Handle(Note(Mackie.Rec + 3, true));
        Assert.Equal(["DJ A TOGGLE", "DJ B SYNC", "DJ C PFL", "DJ D CUE"], mixer.Commands);
        // The DJ master on strip 5 is a normal channel: MUTE mutes it.
        surface.Handle(Note(Mackie.Mute + 4, true));
        Assert.True(mixer.Dj[4].Muted);
    }

    [Fact]
    public void A_deck_fader_uses_the_whole_travel()
    {
        var (surface, mixer) = DjPanel(faderStart: false);
        surface.Handle(Bend(0, Mackie.FaderMax));
        Assert.Equal(1f, mixer.Dj[0].Volume);
        surface.Handle(Bend(0, Mackie.FaderMax / 2));
        Assert.InRange(mixer.Dj[0].Volume, 0.49f, 0.51f);
        Assert.Empty(mixer.Commands);
    }

    [Fact]
    public void Fader_start_starts_and_pauses_a_deck()
    {
        var (surface, mixer) = DjPanel();
        Assert.Equal("deck A: faderstart", surface.Handle(Bend(0, 4000)));
        mixer.Dj[0] = mixer.Dj[0] with { Playing = true };
        Assert.Null(surface.Handle(Bend(0, 9000)));   // further open: nothing new
        Assert.Equal("deck A: fader dicht, pauze", surface.Handle(Bend(0, 0)));
        Assert.Equal(["DJ A PLAY", "DJ A PAUSE"], mixer.Commands);
    }

    [Fact]
    public void A_tap_on_a_closed_deck_fader_does_not_start_the_deck()
    {
        var (surface, mixer) = DjPanel();
        Assert.Null(surface.Handle(Bend(0, 800)));   // about 5 %
        Assert.Null(surface.Handle(Bend(0, 0)));
        Assert.Empty(mixer.Commands);
        Assert.Equal("deck A: faderstart", surface.Handle(Bend(0, 3000)));
    }

    [Fact]
    public void A_deck_button_that_fails_says_why()
    {
        var (surface, mixer) = DjPanel();
        mixer.Fails = "geen ander spelend deck met een tempo om te volgen";
        Assert.Equal("deck A: sync lukt niet: geen ander spelend deck met een tempo om te volgen", surface.Handle(Note(Mackie.Select, true)));
    }

    [Fact]
    public void A_deck_started_on_the_screen_opens_its_fader()
    {
        var (surface, mixer) = DjPanel();
        mixer.Dj[1] = mixer.Dj[1] with { Playing = true };
        surface.Refresh();
        Assert.Equal(1f, mixer.Dj[1].Volume);
        // Without fader start the fader stays where it is.
        var (other, otherMixer) = DjPanel(faderStart: false);
        otherMixer.Dj[1] = otherMixer.Dj[1] with { Playing = true };
        other.Refresh();
        Assert.Equal(0f, otherMixer.Dj[1].Volume);
    }

    [Fact]
    public void A_deck_that_already_played_keeps_its_fader_when_the_layout_comes_up()
    {
        var mixer = new FakeMixer();
        mixer.Dj[0] = mixer.Dj[0] with { Playing = true };
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Handle(Note(Mackie.Flip, true));
        surface.Refresh();
        surface.Refresh();
        Assert.Equal(0f, mixer.Dj[0].Volume);
    }

    [Fact]
    public void The_panel_follows_the_dj_desk_going_on_and_off_air()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Refresh();
        Assert.Equal(MackieLayout.Broadcast, surface.Group.Layout);
        mixer.DjOnAir = true;
        surface.Refresh();
        Assert.Equal(MackieLayout.Dj, surface.Group.Layout);
        surface.Handle(Note(Mackie.Flip, true));   // FLIP still switches back by hand
        surface.Refresh();
        Assert.Equal(MackieLayout.Broadcast, surface.Group.Layout);
        mixer.DjOnAir = false;
        surface.Refresh();
        Assert.Equal(MackieLayout.Broadcast, surface.Group.Layout);
    }

    [Fact]
    public void A_dj_layout_chosen_with_flip_stays_when_the_desk_goes_off_air()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Refresh();
        surface.Handle(Note(Mackie.Flip, true));
        mixer.DjOnAir = true;
        surface.Refresh();
        mixer.DjOnAir = false;
        surface.Refresh();
        Assert.Equal(MackieLayout.Dj, surface.Group.Layout);
    }

    [Fact]
    public void A_button_without_a_function_is_named_with_its_note()
    {
        var surface = new MackieSurface(new FakeMixer(), new MackieSettings());
        Assert.Equal("knop 4Ah: geen functie", surface.Handle(Note(0x4A, true)));
    }

    [Fact]
    public void A_panel_that_comes_up_while_the_desk_is_on_air_starts_in_the_dj_layout()
    {
        var mixer = new FakeMixer { DjOnAir = true };
        var surface = new MackieSurface(mixer, new MackieSettings());
        surface.Refresh(force: true);
        Assert.Equal(MackieLayout.Dj, surface.Group.Layout);
    }

    // ---------------------------------------------------------------- iCON screens

    private static string Text(MackieOutput o) => System.Text.Encoding.ASCII.GetString(o.SysEx!, 7, o.SysEx!.Length - 8);

    private static (MackieSurface Surface, FakeMixer Mixer) IconPanel()
    {
        var mixer = new FakeMixer();
        var surface = new MackieSurface(mixer, new MackieSettings()) { Icon = true, LocalTime = () => new DateTime(2026, 11, 15, 14, 25, 7) };
        return (surface, mixer);
    }

    [Fact]
    public void An_icon_panel_shows_the_strips_above_the_faders_and_what_plays_on_top()
    {
        var (surface, _) = IconPanel();
        var outs = surface.Refresh(force: true);
        var second = outs.Where(o => o.SysEx is [0xF0, 0x00, 0x02, 0x4E, 0x15, 0x13, ..]).ToList();
        Assert.StartsWith("Kanaal Kanaal", Text(second[0]));
        var main = outs.Where(o => o.SysEx is [0xF0, 0x00, 0x00, 0x66, 0x14, 0x12, ..]).Select(Text).ToList();
        Assert.StartsWith("Nu: Artiest X - Nu", main[0]);
        Assert.EndsWith(" -3:25", main[0]);
        Assert.Equal(56, main[0].Length);
        Assert.StartsWith("Straks: Artiest A - Een", main[1]);
    }

    [Fact]
    public void A_long_title_scrolls_on_the_top_screen()
    {
        var (surface, mixer) = IconPanel();
        var now = new DateTime(2026, 11, 15, 13, 0, 0, DateTimeKind.Utc);
        surface.Clock = () => now;
        string title = "Een artiest met een hele lange naam - En een titel die echt niet op het scherm past";
        mixer.OnAir = mixer.OnAir with { Now = title };
        string Top() => surface.Refresh(force: true).Where(o => o.SysEx is [0xF0, 0x00, 0x00, 0x66, 0x14, 0x12, 0, ..]).Select(Text).Single();
        string first = Top();
        Assert.StartsWith("Nu: Een artiest", first);
        Assert.EndsWith(" -3:25", first);
        now += MackieSurface.ScrollWait + MackieSurface.ScrollStep * 4;
        string later = Top();
        Assert.StartsWith("Een artiest", later);
        Assert.EndsWith(" -3:25", later);
        Assert.Equal(56, later.Length);
        mixer.OnAir = mixer.OnAir with { Now = "Kort" };
        Assert.StartsWith("Nu: Kort ", Top());
    }

    [Fact]
    public void A_plain_mackie_panel_keeps_the_strips_on_its_display()
    {
        var outs = new MackieSurface(new FakeMixer(), new MackieSettings()).Refresh(force: true);
        Assert.DoesNotContain(outs, o => o.SysEx is [0xF0, 0x00, 0x02, 0x4E, ..]);
        Assert.StartsWith("Kanaal", Text(outs.First(o => o.SysEx is [0xF0, 0x00, 0x00, 0x66, 0x14, 0x12, 0, ..])));
    }

    [Fact]
    public void The_clock_runs_on_the_time_display()
    {
        var (surface, _) = IconPanel();
        var outs = surface.Refresh(force: true);
        var digits = outs.Where(o => o.SysEx is null && (o.Short & 0xFF) == 0xB0 && ((o.Short >> 8) & 0x7F) is >= 0x40 and <= 0x49)
            .ToDictionary(o => (int)((o.Short >> 8) & 0x7F) - 0x40, o => (int)((o.Short >> 16) & 0x7F));
        // " 14.25.07 ": digit 9 blank, 8-7 = 14, 6-5 = 25, 4-3 = 07, 2 blank.
        Assert.Equal(0x20, digits[9]);
        // Digits 7 and 5 carry the dot (+40h) between hours, minutes and seconds.
        Assert.Equal(new[] { 0x31, 0x34 | 0x40, 0x32, 0x35 | 0x40, 0x30, 0x37 }, new[] { digits[8], digits[7], digits[6], digits[5], digits[4], digits[3] });
        Assert.Equal(0x20, digits[2]);
        Assert.DoesNotContain(surface.Refresh(), o => o.SysEx is null && (o.Short & 0xFF) == 0xB0 && ((o.Short >> 8) & 0x7F) >= 0x40 && ((o.Short >> 8) & 0x7F) <= 0x49);
    }

    [Fact]
    public void The_colour_bar_shows_the_kind_of_channel_and_whether_it_is_on_air()
    {
        Assert.Equal(new MackieColor(127, 0, 0), MackieSurface.ColorOf(new MackieStripState("mic", "Mic", 1, false, 0, true, false, 0)));
        Assert.Equal(new MackieColor(127, 0, 0).Dim(), MackieSurface.ColorOf(new MackieStripState("mic", "Mic", 1, false, 0, false, false, 0)));
        Assert.Equal(new MackieColor(90, 0, 127), MackieSurface.ColorOf(new MackieStripState("deck:A", "Deck A", 1, false, 0, false, false, 0, "A", Playing: true)));
        var (surface, _) = IconPanel();
        var color = surface.Refresh(force: true).Single(o => o.SysEx is [0xF0, 0x00, 0x02, 0x4E, 0x16, 0x14, ..]);
        Assert.Equal(6 + 24 + 1, color.SysEx!.Length);
    }

    [Fact]
    public void An_icon_panel_gets_the_programme_on_its_master_meter()
    {
        var (surface, _) = IconPanel();
        var meters = surface.Meters();
        Assert.Contains(Mackie.MasterMeter(0, 12), meters);
        Assert.Contains(Mackie.MasterMeter(1, Mackie.Segments(0.01f)), meters);
        Assert.Equal(0xD1u, Mackie.MasterMeter(1, 5).Short & 0xFF);
        Assert.Equal(0x15u, (Mackie.MasterMeter(1, 5).Short >> 8) & 0xFF);
    }
}
