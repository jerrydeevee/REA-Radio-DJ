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
        Assert.Equal("Pointer         ", Text(6, 0));   // the labels after the touch layers stay
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
        Assert.Null(IconV1M.Button("ch5 note 0"));
        Assert.Null(IconV1M.Button("ch2 note 24"));
        Assert.Null(IconV1M.Button("ch2 cc 5"));
        Assert.Equal("ch3 note 7", IconV1M.Trigger(1, 7));
    }
}
