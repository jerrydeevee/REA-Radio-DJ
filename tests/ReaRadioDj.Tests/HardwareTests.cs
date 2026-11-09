using System.Text;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Tests;

public sealed class HwSignalTests
{
    [Fact]
    public void Addresses_are_read_and_checked()
    {
        Assert.Equal(new HwAddress("note", 60), HwSignals.Parse(" NOTE 60 "));
        Assert.Equal(new HwAddress("cc", 7, 0, 2), HwSignals.Parse("ch2 cc 7"));
        Assert.Equal(new HwAddress("gpi", 3, 1), HwSignals.Parse("gpi 3.1"));
        Assert.Equal("ch2 cc 7", HwSignals.Parse("ch2 cc 7")!.ToString());
        Assert.Equal("gpo 4.2", HwSignals.Parse("gpo 4.2")!.ToString());
        Assert.Null(HwSignals.Parse("note 200"));
        Assert.Null(HwSignals.Parse("gpi 3"));     // a Livewire pin needs port.pin
        Assert.Null(HwSignals.Parse("gpi 3.6"));   // five pins per port
        Assert.Null(HwSignals.Parse("ch17 note 1"));
        Assert.Null(HwSignals.Parse("START"));     // a serial line
    }

    [Fact]
    public void MIDI_messages_become_on_and_off()
    {
        Assert.Equal(new HwEvent(1, "note 60", true, 1), HwSignals.FromMidi(1, 0x7F3C90));
        Assert.Equal(new HwEvent(1, "note 60", false, 1), HwSignals.FromMidi(1, 0x003C90)); // note on with velocity 0
        Assert.Equal(new HwEvent(1, "note 60", false, 3), HwSignals.FromMidi(1, 0x403C82));
        Assert.Equal(new HwEvent(1, "cc 7", true, 1), HwSignals.FromMidi(1, 0x4007B0));
        Assert.Equal(new HwEvent(1, "cc 7", false, 1), HwSignals.FromMidi(1, 0x3F07B0));
        Assert.Equal(new HwEvent(1, "pc 5", true, 16), HwSignals.FromMidi(1, 0x05CF));
        Assert.Null(HwSignals.FromMidi(1, 0xF8)); // clock

        Assert.Equal(0x7F3C90u, HwSignals.ToMidi(new HwAddress("note", 60), true));
        Assert.Equal(0x003C80u, HwSignals.ToMidi(new HwAddress("note", 60), false));
        Assert.Equal(0x7F14B1u, HwSignals.ToMidi(new HwAddress("cc", 20, 0, 2), true));
        Assert.Null(HwSignals.ToMidi(new HwAddress("pc", 1), false));
    }

    [Fact]
    public void A_trigger_matches_its_device_key_and_channel()
    {
        var any = new HwInput(1, 5, "note 60", "NEXT");
        var ch2 = new HwInput(2, 5, "ch2 note 60", "NEXT");
        var line = new HwInput(3, 6, "start", "PLAY");
        Assert.True(HwSignals.Matches(any, new HwEvent(5, "note 60", true, 9)));
        Assert.False(HwSignals.Matches(any, new HwEvent(4, "note 60", true, 9)));
        Assert.False(HwSignals.Matches(any, new HwEvent(5, "cc 60", true, 9)));
        Assert.True(HwSignals.Matches(ch2, new HwEvent(5, "note 60", false, 2)));
        Assert.False(HwSignals.Matches(ch2, new HwEvent(5, "note 60", true, 1)));
        Assert.True(HwSignals.Matches(line, new HwEvent(6, " START ", true)));
        Assert.False(HwSignals.Matches(line, new HwEvent(6, "STARTEN", true)));
    }

    [Fact]
    public void Livewire_lines_give_settled_pins_and_a_GPO_sets_one_pin()
    {
        var events = HwSignals.FromLivewire(2, "GPI 3 lhhHh");
        Assert.Equal(new[] { ("gpi 3.1", true), ("gpi 3.2", false), ("gpi 3.3", false), ("gpi 3.5", false) },
            events.Select(e => (e.Key, e.On)));                                   // pin 4 is still changing
        Assert.Empty(HwSignals.FromLivewire(2, "VER LWRP:1.4"));
        Assert.Equal("GPO 4 xLxxx", HwSignals.ToLivewire(new HwAddress("gpo", 4, 2), true));
        Assert.Equal("GPO 12 xxxxH", HwSignals.ToLivewire(new HwAddress("gpo", 12, 5), false));
    }

    [Fact]
    public void Only_real_changes_count_except_for_presses()
    {
        var last = new Dictionary<(long, string), bool>();
        Assert.True(HwSignals.IsChange(last, new HwEvent(1, "cc 7", true)));
        Assert.False(HwSignals.IsChange(last, new HwEvent(1, "cc 7", true)));   // the fader moves on above 64
        Assert.True(HwSignals.IsChange(last, new HwEvent(1, "cc 7", false)));
        Assert.True(HwSignals.IsChange(last, new HwEvent(2, "cc 7", true)));    // another device
        Assert.True(HwSignals.IsChange(last, new HwEvent(1, "cc 7", true, 2))); // another channel
        Assert.True(HwSignals.IsChange(last, new HwEvent(1, "gpi 1.2", false)));
        Assert.False(HwSignals.IsChange(last, new HwEvent(1, "gpi 1.2", false))); // a GPI line repeats every pin
        Assert.True(HwSignals.IsChange(last, new HwEvent(1, "pc 3", true)));
        Assert.True(HwSignals.IsChange(last, new HwEvent(1, "pc 3", true)));
        Assert.True(HwSignals.IsChange(last, new HwEvent(3, "START", true)));
        Assert.True(HwSignals.IsChange(last, new HwEvent(3, "START", true)));
    }

    [Fact]
    public void Serial_text_and_lines()
    {
        Assert.Equal("RELAY 1\r\n\u0002\\"u8.ToArray(), HwSignals.Unescape(@"RELAY 1\r\n\x02\\"));
        var pending = new StringBuilder();
        Assert.Empty(HwSignals.Lines(pending, "STA"));
        Assert.Equal(new[] { "START", "STOP" }, HwSignals.Lines(pending, "RT\r\n\nSTOP\rNE"));
        Assert.Equal("NE", pending.ToString());
    }
}

public sealed class HardwareStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-hw-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public HardwareStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Devices_keep_their_buttons_and_tallies_until_they_are_removed()
    {
        var store = new HardwareStore(_db);
        int changes = 0;
        store.Changed += () => changes++;
        long desk = store.Save(new HwDevice(0, "", true, HwKind.Livewire, " 192.168.1.20 ", 1, "geheim"));
        long midi = store.Save(new HwDevice(0, "Controller", true, HwKind.Midi, "nanoKONTROL2"));
        var d = store.Devices().Single(x => x.Id == desk);
        Assert.Equal(("Livewire", "192.168.1.20", 9600, "geheim"), (d.Name, d.Port, d.Baud, d.Password));

        long fader = store.Save(new HwInput(0, desk, " gpi 1.1 ", " PLAYER 1 PLAY ", "PLAYER 1 STOP"));
        store.Save(new HwInput(0, midi, "note 60", "NEXT"));
        store.Save(new HwOutput(0, desk, HwState.Mic, "gpo 2.1"));
        store.Save(new HwInput(fader, desk, "gpi 1.2", "MIC ON", "MIC OFF"));
        Assert.Equal(("gpi 1.2", "MIC ON"), (store.Inputs().First(i => i.Id == fader).Trigger, store.Inputs().First(i => i.Id == fader).OnCommand));
        Assert.Equal(HwState.Mic, Assert.Single(store.Outputs()).State);

        Assert.True(store.DeleteDevice(desk));
        Assert.Equal("note 60", Assert.Single(store.Inputs()).Trigger);
        Assert.Empty(store.Outputs());
        Assert.Equal(7, changes);
    }
}
