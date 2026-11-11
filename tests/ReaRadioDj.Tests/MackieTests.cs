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
        private void Change(string id, Func<MackieStripState, MackieStripState> f) { int i = List.FindIndex(s => s.Id == id); List[i] = f(List[i]); }
        public void SetVolume(string id, float v) => Change(id, s => s with { Volume = v });
        public void SetMute(string id, bool m) => Change(id, s => s with { Muted = m });
        public void SetPan(string id, double p) => Change(id, s => s with { Pan = p });
        public void SetOnAir(string id, bool o) => Change(id, s => s with { OnAir = o });
        public void SetRecording(string id, bool r) => Change(id, s => s with { Recording = r });
        public void Run(string command) => Commands.Add(command);
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
}
