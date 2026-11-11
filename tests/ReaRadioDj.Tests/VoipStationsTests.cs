using ReaRadioDj.Core.Voip;
using Xunit;

namespace ReaRadioDj.Tests;

public class VoipStationsTests
{
    private static VoipStation Sip(string name, string target, string id = "") => new(id, name, "sip", target, 0, "", 0, "", 0, "");

    private static VoipStation Rtp(string name, int local, string host = "", int remote = 0, string codec = "pcma", double jitter = 60, string id = "") =>
        new(id, name, "rtp", "", local, host, remote, codec, jitter, "");

    [Fact]
    public void Clean_tidies_stations_and_makes_missing_ids()
    {
        int n = 0;
        var list = VoipStations.Clean([Sip("  Verslaggever ", " 0612345678 "), Rtp("Studio Gieten", 5104, "10.0.0.5", 5106, "L16", 5000, "fixed")],
            out var error, () => $"id{++n}");
        Assert.Null(error);
        Assert.NotNull(list);
        Assert.Equal("Verslaggever", list[0].Name);
        Assert.Equal("0612345678", list[0].Target);
        Assert.Equal("id1", list[0].Id);
        Assert.Equal("fixed", list[1].Id);
        Assert.Equal("l16", list[1].Codec);
        Assert.Equal(1000, list[1].JitterMs); // clamped
        Assert.Equal(5106, list[1].RemotePort);
    }

    [Fact]
    public void Clean_drops_the_far_port_of_a_line_that_waits()
    {
        var list = VoipStations.Clean([Rtp("Wachten", 5104, "", 7000)], out var error);
        Assert.Null(error);
        Assert.Equal(0, list![0].RemotePort);
    }

    [Theory]
    [InlineData("", "sip", "100", "naam")]
    [InlineData("A", "fax", "100", "sip of rtp")]
    [InlineData("A", "sip", "", "nummer")]
    [InlineData("A", "sip", "100<script>", "ongeldige tekens")]
    public void Clean_refuses_bad_stations(string name, string kind, string target, string expected)
    {
        Assert.Null(VoipStations.Clean([new VoipStation("", name, kind, target, 0, "", 0, "", 0, "")], out var error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Clean_refuses_bad_lines_and_double_names()
    {
        Assert.Null(VoipStations.Clean([Rtp("R", 80)], out var e1));
        Assert.Contains("eigen poort", e1);
        Assert.Null(VoipStations.Clean([Rtp("R", 5104, "10.0.0.5", 0)], out var e2));
        Assert.Contains("overkant", e2);
        Assert.Null(VoipStations.Clean([Rtp("R", 5104, codec: "opus")], out var e3));
        Assert.Contains("codec", e3);
        Assert.Null(VoipStations.Clean([Rtp("R", 5104, "10.0.0 .5", 5106)], out var e4));
        Assert.Contains("spaties", e4);
        Assert.Null(VoipStations.Clean([Sip("Gieten", "1"), Sip("gieten", "2")], out var e5));
        Assert.Contains("twee keer", e5);
    }

    [Fact]
    public void Clean_gives_double_ids_a_new_one()
    {
        int n = 0;
        var list = VoipStations.Clean([Sip("A", "1", "x"), Sip("B", "2", "x")], out _, () => $"new{++n}");
        Assert.Equal(["x", "new1"], list!.Select(s => s.Id));
    }

    [Fact]
    public void Clean_has_a_maximum()
    {
        var many = Enumerable.Range(0, VoipStations.Max + 1).Select(i => Sip($"S{i}", "1"));
        Assert.Null(VoipStations.Clean(many, out var error));
        Assert.Contains("ten hoogste", error);
    }

    [Fact]
    public void Find_by_id_then_by_name_ignoring_case_and_quotes()
    {
        VoipStation[] list = [Sip("Studio Gieten", "1", "a1"), Sip("a1", "2", "b2")];
        Assert.Equal("Studio Gieten", VoipStations.Find(list, "a1")!.Name); // the id wins over a name
        Assert.Equal("a1", VoipStations.Find(list, "\"studio gieten\"")!.Id); // by name, quotes from the command bus removed
        Assert.Equal("b2", VoipStations.Find(list, "b2")!.Id);
        Assert.Equal("a1", VoipStations.Find(list, "  STUDIO GIETEN ")!.Id);
        Assert.Null(VoipStations.Find(list, "Assen"));
    }

    [Fact]
    public void Commands_fill_in_the_channel_and_split()
    {
        Assert.Equal(["OSC 10.0.0.9:8000 /lamp/2 1", "VOIP 2 PFL ON"],
            VoipStations.Commands(" OSC 10.0.0.9:8000 /lamp/{nr} 1 ;  VOIP {nr} PFL ON ; ", "voip2"));
        Assert.Equal(["CHANNEL voip1 ON"], VoipStations.Commands("CHANNEL {kanaal} ON", "voip1"));
        Assert.Empty(VoipStations.Commands("  ", "voip1"));
        Assert.Empty(VoipStations.Commands(null, "voip1"));
    }

    [Theory]
    [InlineData("1", "voip1")]
    [InlineData(" VOIP2 ", "voip2")]
    [InlineData("3", null)]
    [InlineData(null, null)]
    public void Channel_names(string? text, string? expected) => Assert.Equal(expected, VoipStations.Channel(text));
}
