using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class DeviceChoiceTests
{
    private const string Umc = "OUT 1-2 (BEHRINGER UMC 404HD 192k)", Dell = "DELL U2715H (NVIDIA High Definition Audio)";

    // The list as it was when the card was chosen, and after a new driver and a restart of Windows.
    private static readonly AudioDevice[] Before =
        [new(0, "No sound", true, false), new(1, "Default", true, true), new(2, Dell, true, false), new(3, "Luidsprekers (Realtek USB Audio)", true, false), new(4, Umc, true, false), new(5, Dell, true, false)];
    private static readonly AudioDevice[] After =
        [new(0, "No sound", true, false), new(1, "Default", true, true), new(2, Dell, true, false), new(3, "OUT 3-4 (BEHRINGER UMC 404HD 192k)", true, false), new(4, Dell, true, false), new(5, "Luidsprekers (Realtek USB Audio)", true, false), new(6, Umc, true, false)];

    [Fact]
    public void A_card_that_moved_is_found_by_its_name()
    {
        Assert.Equal(Umc, DeviceChoice.NameOf(4, Before));
        Assert.Equal((6, (string?)null), DeviceChoice.Resolve(4, Umc, After, "Uitzending"));
    }

    [Fact]
    public void Of_two_cards_with_the_same_name_the_one_at_the_stored_number_wins()
    {
        Assert.Equal((4, (string?)null), DeviceChoice.Resolve(4, Dell, After, "Uitzending"));
        Assert.Equal((2, (string?)null), DeviceChoice.Resolve(5, Dell, After, "Uitzending"));   // number 5 is no DELL now: the first one
    }

    [Fact]
    public void A_card_that_is_gone_falls_back_to_the_default_with_a_note()
    {
        AudioDevice[] gone = [new(0, "No sound", true, false), new(1, "Default", true, true), new(2, Dell, true, false), new(3, "Luidsprekers (Realtek USB Audio)", true, false), new(4, Dell, true, false)];
        var (index, note) = DeviceChoice.Resolve(4, Umc, gone, "Uitzending");
        Assert.Equal(-1, index);   // not number 4, which is a DELL screen now
        Assert.Contains(Umc, note);
        Assert.StartsWith("Uitzending", note);
    }

    [Fact]
    public void The_default_no_name_or_no_list_keep_the_number()
    {
        Assert.Equal((-1, (string?)null), DeviceChoice.Resolve(-1, Umc, After, "Uitzending"));
        Assert.Equal((4, (string?)null), DeviceChoice.Resolve(4, null, After, "Uitzending"));   // stored before names were kept
        Assert.Equal((4, (string?)null), DeviceChoice.Resolve(4, Umc, [], "Uitzending"));       // no BASS: nothing to check against
        Assert.Null(DeviceChoice.NameOf(-1, Before));
        Assert.Null(DeviceChoice.NameOf(9, Before));
        Assert.Equal("No sound", DeviceChoice.NameOf(0, Before));
    }
}
