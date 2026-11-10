using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Tests;

public sealed class DjMidiTests
{
    private static DjControl C(string id) => DjMidi.Find(id)!;

    [Fact]
    public void Values_map_to_the_ranges_of_the_desk()
    {
        Assert.Equal(0, DjMidi.Unit(0));
        Assert.Equal(1, DjMidi.Unit(127));
        Assert.Equal(1, DjMidi.Unit(0, invert: true));
        Assert.Equal(-1, DjMidi.Centered(0));
        Assert.Equal(0, DjMidi.Centered(64));   // a knob at its click is exactly flat
        Assert.Equal(1, DjMidi.Centered(127));
        Assert.Equal(-1, DjMidi.Centered(127, invert: true));
    }

    [Fact]
    public void A_jog_wheel_sends_steps_in_twos_complement()
    {
        Assert.Equal(0, DjMidi.Step(0));
        Assert.Equal(0, DjMidi.Step(64));
        Assert.Equal(1, DjMidi.Step(1));
        Assert.Equal(3, DjMidi.Step(3));
        Assert.Equal(-1, DjMidi.Step(127));
        Assert.Equal(-3, DjMidi.Step(125));
    }

    [Fact]
    public void Controls_become_commands_on_the_bus()
    {
        Assert.Equal("DJ A TOGGLE", DjMidi.Command(C("A.play"), 127));
        Assert.Null(DjMidi.Command(C("A.play"), 0));               // released
        Assert.Equal("DJ B CUE", DjMidi.Command(C("b.cue"), 100));
        Assert.Equal("DJ C HOTCUE 3", DjMidi.Command(C("C.hotcue3"), 127));
        Assert.Equal("DJ A LOOPTOGGLE 4", DjMidi.Command(C("A.loop"), 127));
        Assert.Equal("DJ XFADER 0", DjMidi.Command(C("xfader"), 0));
        Assert.Equal("DJ XFADER 1", DjMidi.Command(C("xfader"), 127));
        Assert.Equal("DJ XFADER 0", DjMidi.Command(C("xfader"), 127, invert: true));
        Assert.Equal("DJ A TEMPO 0", DjMidi.Command(C("A.tempo"), 64));
        Assert.Equal("DJ A TEMPO -8", DjMidi.Command(C("A.tempo"), 0));
        Assert.Equal("DJ A TEMPO 16", DjMidi.Command(C("A.tempo"), 127, tempoRange: 16));
        Assert.Equal("DJ A TEMPO -16", DjMidi.Command(C("A.tempo"), 127, invert: true, tempoRange: 16));
        Assert.Equal("DJ B EQLOW -1", DjMidi.Command(C("B.eqlow"), 0));
        Assert.Equal("DJ B EQHIGH 0", DjMidi.Command(C("B.eqhigh"), 64));
        Assert.Equal("DJ A GAIN 12", DjMidi.Command(C("A.gain"), 127));
        Assert.Equal("DJ A FADER 1", DjMidi.Command(C("A.fader"), 127));
        Assert.Equal("DJ A BEND 1.5", DjMidi.Command(C("A.jog"), 1));
        Assert.Equal("DJ A BEND -3", DjMidi.Command(C("A.jog"), 126));
        Assert.Equal("DJ A BEND 8", DjMidi.Command(C("A.jog"), 20));  // clamped
        Assert.Null(DjMidi.Command(C("A.jog"), 0));
        Assert.Equal("DJ AUTOMIX TOGGLE", DjMidi.Command(C("automix"), 127));
    }

    [Fact]
    public void Buttons_learn_from_a_press_and_faders_only_from_a_controller()
    {
        var note = new HwEvent(1, "note 11", true, 1, 100);
        var noteOff = new HwEvent(1, "note 11", false, 1, 0);
        var ccHigh = new HwEvent(1, "cc 7", true, 2, 127);
        var ccLow = new HwEvent(1, "cc 7", false, 2, 10);
        Assert.True(DjMidi.CanLearn(C("A.play"), note));
        Assert.False(DjMidi.CanLearn(C("A.play"), noteOff));
        Assert.True(DjMidi.CanLearn(C("A.play"), ccHigh));
        Assert.False(DjMidi.CanLearn(C("A.play"), ccLow));
        Assert.True(DjMidi.CanLearn(C("A.tempo"), ccLow));
        Assert.False(DjMidi.CanLearn(C("A.tempo"), note));
        Assert.Equal(127, DjMidi.ValueOf(note));
        Assert.Equal(0, DjMidi.ValueOf(noteOff));
        Assert.Equal(10, DjMidi.ValueOf(ccLow));
    }

    [Fact]
    public void A_binding_listens_to_its_own_device_message_and_channel()
    {
        var e = new HwEvent(1, "cc 7", false, 2, 40);
        Assert.Equal("ch2 cc 7", DjMidi.Trigger(e));
        Assert.True(DjMidi.Matches(new DjMidiBinding("xfader", 1, "ch2 cc 7"), e));
        Assert.True(DjMidi.Matches(new DjMidiBinding("xfader", 1, "cc 7"), e));      // any channel
        Assert.False(DjMidi.Matches(new DjMidiBinding("xfader", 1, "ch1 cc 7"), e));
        Assert.False(DjMidi.Matches(new DjMidiBinding("xfader", 2, "ch2 cc 7"), e));
        Assert.False(DjMidi.Matches(new DjMidiBinding("xfader", 1, "ch2 note 7"), e));
    }
}
