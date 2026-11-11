using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Remote;

namespace ReaRadioDj.Tests;

public sealed class EventRuleTests
{
    [Fact]
    public void An_osc_message_built_here_reads_back_the_same()
    {
        var packet = Osc.Build("/licht/onair", Osc.Arguments(["1", "0.5", "Racoon - Oceaan", "true"]));
        Assert.Equal(0, packet.Length % 4);
        var m = Assert.Single(Osc.Parse(packet));
        Assert.Equal("/licht/onair", m.Address);
        Assert.Equal(1, m.Args[0]);
        Assert.Equal(0.5f, m.Args[1]);
        Assert.Equal("Racoon - Oceaan", m.Args[2]);
        Assert.Equal(true, m.Args[3]);

        var empty = Assert.Single(Osc.Parse(Osc.Build("/rea/next", [])));
        Assert.Empty(empty.Args);
        Assert.Equal("next", Osc.ToCommand(empty));
    }

    [Fact]
    public void Rules_fire_for_their_event_and_fill_in_what_started()
    {
        var rules = EventRules.Clean(
        [
            new("start", "OSC 10.0.0.5:9000 /nu \"{artiest} - {titel}\""),
            new("start:jingle", "MESSAGE jingle {titel}"),
            new("mic-on", "HTTP http://lamp.local/on"),
            new("mic-off", "  ", true),                       // no command: dropped
            new("onbekend", "NEXT"),                           // unknown event: dropped
            new("start:song", "MESSAGE uit", Enabled: false),
        ]);
        Assert.Equal(4, rules.Count);

        var song = new NowPlaying(7, "song", "", "Racoon", "Oceaan", "NL", "", 0, 0, 200, 195, null, null, null, 3);
        Assert.Equal(["OSC 10.0.0.5:9000 /nu \"Racoon - Oceaan\""], EventRules.CommandsFor(rules, "start:song", song, "Zender"));

        var jingle = song with { Type = "jingle", Artist = "", Title = "Station ID" };
        Assert.Equal(["OSC 10.0.0.5:9000 /nu \" - Station ID\"", "MESSAGE jingle Station ID"], EventRules.CommandsFor(rules, "start:jingle", jingle, "Zender"));

        Assert.Equal(["HTTP http://lamp.local/on"], EventRules.CommandsFor(rules, "mic-on", null, "Zender"));
        Assert.Empty(EventRules.CommandsFor(rules, "stream-down", null, "Zender"));
    }
}
