using System.Net;
using ReaRadioDj.Core.Remote;

namespace ReaRadioDj.Tests;

/// <summary>Remote controls with their own limits: recognising them and what they may do (no network).</summary>
public class RemoteClientTests
{
    private static readonly string[] AllGroups = ["Uitzending", "Log", "Mixer", "Cartwall", "Info", "Stream"];

    private static RemoteClient Client(string name, string key = "", string addresses = "", params string[] groups) =>
        new(name, name, true, RemoteClients.Hash(key), addresses, groups);

    [Fact]
    public void A_client_is_found_by_its_key_and_the_key_is_not_stored()
    {
        var key = RemoteClients.NewKey();
        Assert.Equal(20, key.Length);
        var tablet = Client("tablet", key, "", "Cartwall");
        Assert.DoesNotContain(key, tablet.KeyHash);
        Assert.Same(tablet, RemoteClients.ByKey([tablet], key, IPAddress.Parse("192.168.1.30")));
        Assert.Null(RemoteClients.ByKey([tablet], "fout", IPAddress.Parse("192.168.1.30")));
        Assert.Null(RemoteClients.ByKey([tablet with { Enabled = false }], key, null));
    }

    [Fact]
    public void A_key_can_be_tied_to_addresses_and_networks()
    {
        var box = Client("knoppen", "k1", "192.168.1.0/24, 10.0.0.5");
        Assert.NotNull(RemoteClients.ByKey([box], "k1", IPAddress.Parse("192.168.1.200")));
        Assert.NotNull(RemoteClients.ByKey([box], "k1", IPAddress.Parse("10.0.0.5")));
        Assert.NotNull(RemoteClients.ByKey([box], "k1", IPAddress.Parse("::ffff:192.168.1.7")));
        Assert.Null(RemoteClients.ByKey([box], "k1", IPAddress.Parse("192.168.2.1")));
        Assert.Null(RemoteClients.ByKey([box], "k1", null));
    }

    [Fact]
    public void Without_a_key_a_client_is_known_by_its_address_only()
    {
        var deck = Client("streamdeck", "", "192.168.1.50", "Cartwall");
        var anywhere = Client("overal");   // no key and no address: never matches by address
        Assert.Same(deck, RemoteClients.ByAddress([anywhere, deck], IPAddress.Parse("192.168.1.50")));
        Assert.Null(RemoteClients.ByAddress([anywhere, deck], IPAddress.Parse("192.168.1.51")));
        Assert.Null(RemoteClients.ByAddress([anywhere], IPAddress.Parse("192.168.1.51")));
    }

    [Fact]
    public void A_client_only_runs_its_groups_and_info_always()
    {
        var tablet = Client("tablet", "k", "", "Cartwall", "Uitzending");
        Assert.True(RemoteClients.Allows(tablet, "Cartwall"));
        Assert.True(RemoteClients.Allows(tablet, "uitzending"));
        Assert.True(RemoteClients.Allows(tablet, RemoteClients.InfoGroup));
        Assert.False(RemoteClients.Allows(tablet, "Stream"));
        Assert.False(RemoteClients.Allows(tablet, null));       // an unknown command
        Assert.True(RemoteClients.Allows(null, "Stream"));      // no client: no limits
    }

    [Fact]
    public void Clean_drops_unknown_groups_and_bad_addresses_and_gives_ids()
    {
        var list = RemoteClients.Clean([new RemoteClient("", "  ", true, "", "192.168.1.5, onzin; 10.0.0.0/8", ["Cartwall", "Bestaat niet", "Info", "cartwall"])], AllGroups);
        var c = Assert.Single(list);
        Assert.NotEmpty(c.Id);
        Assert.Equal("Afstandsbediening", c.Name);
        Assert.Equal("192.168.1.5, 10.0.0.0/8", c.Addresses);
        Assert.Equal(["Cartwall"], c.Groups);
    }
}
