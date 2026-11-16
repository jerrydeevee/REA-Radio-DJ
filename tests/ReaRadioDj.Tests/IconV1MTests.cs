using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Tests;

public class IconV1MTests
{
    private static readonly byte[] Token = [0x66, 0x09, 0x60];

    private static List<byte[]> SysEx(IReadOnlyList<IconStep> steps, byte cmd) =>
        steps.Select(s => s.Message.SysEx).OfType<byte[]>().Where(b => b[5] == cmd).ToList();

    private static byte[] Slot(IReadOnlyList<IconStep> steps, int number)
    {
        var page = SysEx(steps, 0x25)[number / 28];
        return page[(9 + number % 28 * 8)..(9 + number % 28 * 8 + 8)];
    }

    [Fact]
    public void A_write_is_framed_like_imap_does_it()
    {
        var steps = IconV1M.Write((_, _) => null, save: true, Token);
        var first = steps[0].Message.SysEx!;
        Assert.Equal(new byte[] { 0xF0, 0x1D, 0x03, 0x10, 0x09, 0x24, 0x66, 0x09, 0x60, 0, 0, 0, 0, 0, 0, 0xF7 }, first);
        Assert.Equal(0x27, SysEx(steps, 0x27).Single()[5]);
        Assert.Equal(Token, SysEx(steps, 0x27).Single()[6..9]);
        Assert.Equal(8, SysEx(steps, 0x25).Count);
        Assert.All(SysEx(steps, 0x25), p => Assert.Equal(5 + 4 + 28 * 8 + 1, p.Length));
        Assert.Equal(10, SysEx(steps, 0x26).Count);
        Assert.All(SysEx(steps, 0x26), p => Assert.Equal(5 + 7 + 12 * 16 + 1, p.Length));
        // Saved as the panel's user default at the end.
        Assert.Equal(0x012DECu, steps[^1].Message.Short);
        Assert.Equal(0x7F7FEFu, steps[^2].Message.Short);
    }

    [Fact]
    public void Without_saving_the_write_ends_with_the_closing_message()
    {
        var steps = IconV1M.Write((_, _) => null, save: false, Token);
        Assert.Equal(0x27, steps[^1].Message.SysEx![5]);
    }

    [Fact]
    public void The_touch_layers_send_notes_on_channel_2_3_and_4()
    {
        var steps = IconV1M.Write((_, _) => null, token: Token);
        Assert.Equal(new byte[] { 0x09, 0x09, 0x01, 0x00, 0x7F, 0, 0, 0 }, Slot(steps, 66));    // blue 1
        Assert.Equal(new byte[] { 0x09, 0x09, 0x01, 0x17, 0x7F, 0, 0, 0 }, Slot(steps, 89));    // blue 24
        Assert.Equal(new byte[] { 0x09, 0x09, 0x02, 0x00, 0x7F, 0, 0, 0 }, Slot(steps, 90));    // green 1
        Assert.Equal(new byte[] { 0x09, 0x09, 0x03, 0x17, 0x7F, 0, 0, 0 }, Slot(steps, 137));   // orange 24
        Assert.Equal(new byte[] { 0x09, 0x09, 0x04, 0x00, 0x7F, 0, 0, 0 }, Slot(steps, 138));   // purple (U1) 1
        Assert.Equal(new byte[] { 0x09, 0x09, 0x05, 0x17, 0x7F, 0, 0, 0 }, Slot(steps, 185));   // red (U2) 24
        // REA's DAW number (not in iMap's list): the name above the master fader stays blank.
        Assert.Contains(steps, s => s.Message.SysEx is { } x && x.Length > 6 && x[5] == 0x2C && x[6] == IconV1M.NoDaw);
        Assert.Contains(steps, s => s.Message.Short == (0xECu | 0x22u << 8 | (uint)IconV1M.NoDaw << 16));
        // The rest stays Mackie: the jog wheel on controller 60, fader 1 on pitch bend.
        Assert.Equal(new byte[] { 0x0B, 0x0B, 0x00, 0x3C, 0x41, 0x01, 0, 0 }, Slot(steps, 204));
        Assert.Equal(new byte[] { 0x0E, 0x0E, 0x00, 0, 0, 0, 0, 0 }, Slot(steps, 214));
    }

    [Fact]
    public void Each_button_gets_its_own_text()
    {
        var steps = IconV1M.Write((layer, button) => $"L{layer} B{button}", token: Token);
        var texts = SysEx(steps, 0x26);
        string Text(int page, int i) => System.Text.Encoding.ASCII.GetString(texts[page][(12 + i * 16)..(12 + i * 16 + 16)]);
        Assert.Equal("L0 B0           ", Text(0, 0));
        Assert.Equal("L0 B23          ", Text(1, 11));
        Assert.Equal("L1 B0           ", Text(2, 0));
        Assert.Equal("L2 B23          ", Text(5, 11));
        Assert.Equal("L3 B0           ", Text(6, 0));   // U1
        Assert.Equal("L4 B23          ", Text(9, 11));  // U2
    }

    [Fact]
    public void Each_cart_layer_is_a_page_of_16_jingles_with_the_same_switches_below()
    {
        Assert.Equal("CART 1 1", IconV1M.DefaultCommand(0, 0));
        Assert.Equal("CART 1 16", IconV1M.DefaultCommand(0, 15));
        Assert.Equal("CART 1 17", IconV1M.DefaultCommand(1, 0));
        Assert.Equal("CART 1 64", IconV1M.DefaultCommand(IconV1M.U1, 15));
        Assert.Null(IconV1M.DefaultCommand(0, 16));
        Assert.Equal("CARTS STOP", IconV1M.DefaultCommand(2, 17));
        for (int layer = 0; layer <= IconV1M.U1; layer++)
        {
            Assert.Equal("MODE AUTO", IconV1M.DefaultCommand(layer, 18));
            Assert.Equal("SKIP", IconV1M.DefaultCommand(layer, 23));
        }
        Assert.Equal("MODE AUTO", IconV1M.DefaultCommand(IconV1M.U2, 0));
        Assert.True(IconV1M.IsCartCommand("cart 2 5"));
        Assert.False(IconV1M.IsCartCommand("NEXT"));
        Assert.Equal(IconV1M.ButtonsPerLayer, IconV1M.Functions.Count);
        Assert.Equal("Mic", IconV1M.FunctionLabel("mic toggle"));
        Assert.All(IconV1M.Functions, f => Assert.True(IconV1M.Label(f.Label).Trim().Length > 0));
    }

    [Fact]
    public void A_lit_touch_button_gets_its_own_note_back()
    {
        Assert.Equal(0x7F_00_90u + 0x01, IconV1M.PadNote(2, 0, true).Short);    // ch2 note 0 on
        Assert.Equal(0x00_17_95u, IconV1M.PadNote(6, 23, false).Short);         // ch6 note 23 off
    }

    [Theory]
    [InlineData("Jingle Hit", "Jingle  Hit     ")]
    [InlineData("Weerbericht", "Weerbericht     ")]
    [InlineData("Café Muziek", "Cafe    Muziek  ")]
    [InlineData("Sport en spel extra", "Sport enspel ext")]
    [InlineData(null, "                ")]
    public void A_text_fits_on_two_lines_of_eight(string? text, string expected)
    {
        Assert.Equal(expected, IconV1M.Label(text));
    }

    [Fact]
    public void A_trigger_names_its_touch_button()
    {
        Assert.Equal((0, 5), IconV1M.Button("ch2 note 5"));
        Assert.Equal((2, 23), IconV1M.Button("ch4 note 23"));
        Assert.Equal((3, 0), IconV1M.Button("ch5 note 0"));
        Assert.Equal((4, 23), IconV1M.Button("ch6 note 23"));
        Assert.Null(IconV1M.Button("ch7 note 0"));
        Assert.Null(IconV1M.Button("ch2 note 24"));
        Assert.Null(IconV1M.Button("ch2 cc 5"));
        Assert.Equal("ch3 note 7", IconV1M.Trigger(1, 7));
    }
}
