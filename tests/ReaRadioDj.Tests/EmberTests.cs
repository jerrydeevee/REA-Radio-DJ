using System.Text;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Tests;

public sealed class EmberTests
{
    [Fact]
    public void Integers_and_relative_oids_use_the_shortest_form()
    {
        Assert.Equal(new byte[] { 0x02, 0x01, 0x00 }, Ber.Integer(0));
        Assert.Equal(new byte[] { 0x02, 0x01, 0x7F }, Ber.Integer(127));
        Assert.Equal(new byte[] { 0x02, 0x02, 0x00, 0x80 }, Ber.Integer(128));
        Assert.Equal(new byte[] { 0x02, 0x01, 0xFF }, Ber.Integer(-1));
        Assert.Equal(new byte[] { 0x02, 0x02, 0xFF, 0x7F }, Ber.Integer(-129));
        foreach (long v in new long[] { 0, 1, -1, 255, -256, 70000, long.MaxValue, long.MinValue })
            Assert.Equal(v, Ber.ReadInteger(Ber.Read(Ber.Integer(v))[0].Content));
        Assert.Equal(new byte[] { 0x0D, 0x04, 0x01, 0x81, 0x48, 0x03 }, Ber.RelativeOid([1, 200, 3]));
        Assert.Equal(new[] { 1, 200, 3 }, Ber.ReadRelativeOid(Ber.Read(Ber.RelativeOid([1, 200, 3]))[0].Content));
    }

    [Fact]
    public void Reals_round_trip_with_the_leading_bit_as_exponent()
    {
        Assert.Equal(new byte[] { 0x80, 0x00, 0x01 }, Ber.RealBytes(1.0));
        Assert.Equal(new byte[] { 0x80, 0x00, 0x03 }, Ber.RealBytes(1.5));
        Assert.Equal(new byte[] { 0xC0, 0x05, 0x05 }, Ber.RealBytes(-40.0));
        Assert.Empty(Ber.RealBytes(0.0));
        foreach (double v in new[] { 1.0, 1.5, -40.0, 0.1, -128.25, 1e300, 5e-324, 0.0, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Equal(v, Ber.ReadReal(Ber.RealBytes(v)));
        Assert.True(double.IsNaN(Ber.ReadReal(Ber.RealBytes(double.NaN))));
        Assert.True(double.IsNegative(Ber.ReadReal(Ber.RealBytes(-0.0))));
    }

    [Fact]
    public void The_CRC_is_CCITT_reflected_and_a_frame_escapes_its_special_bytes()
    {
        Assert.Equal(0x906E, S101.Crc("123456789"u8)); // the published check value of this CRC
        byte[] payload = [0x60, 0xFE, 0xFF, 0xFD, 0xF8, 0x00, 0xF7];
        var frame = S101.EmberPacket(payload);
        Assert.Equal(S101.Bof, frame[0]);
        Assert.Equal(S101.Eof, frame[^1]);
        Assert.DoesNotContain(frame[1..^1], b => b is 0xFE or 0xFF);

        var reader = new S101.Reader();
        var got = reader.Feed([0x01, 0x02, .. frame, .. S101.KeepAliveRequest]); // noise before the first frame
        Assert.Equal(2, got.Count);
        Assert.Equal(payload, got[0].Payload);
        Assert.Equal(S101Message.KeepAliveRequest, got[1].Command);

        var damaged = (byte[])frame.Clone();
        damaged[12] ^= 0x01;
        Assert.Empty(reader.Feed(damaged));
        Assert.Equal(1, reader.Damaged);
    }

    [Fact]
    public void Packets_of_one_message_come_back_together_even_when_split_over_reads()
    {
        var bytes = S101.FirstPacket([1, 2, 3]).Concat(S101.LastPacket([4, 5])).ToArray();
        var reader = new S101.Reader();
        var got = reader.Feed(bytes.AsSpan(0, 9)).Concat(reader.Feed(bytes.AsSpan(9))).ToList();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, Assert.Single(got).Payload);
    }

    [Fact]
    public void GetDirectory_on_the_root_and_on_a_node()
    {
        Assert.Equal(new byte[] { 0x60, 0x0B, 0x6B, 0x09, 0xA0, 0x07, 0x62, 0x05, 0xA0, 0x03, 0x02, 0x01, 0x20 }, Glow.GetDirectory([]));
        var node = Ber.Read(Glow.GetDirectory([1, 2]))[0].Children[0].Children[0].Children[0];
        Assert.True(node.Is(Ber.Application, 10));
        Assert.Equal(new[] { 1, 2 }, Ber.ReadRelativeOid(node.Field(0)!.Content));
        var command = node.Field(2)!.Field(0)!;
        Assert.True(command.Is(Ber.Application, 2));
        Assert.Equal(32, Ber.ReadInteger(command.Field(0)!.Content));
    }

    /// <summary>A constructed element with indefinite length, as many providers write them.</summary>
    private static byte[] I(byte tag, params byte[][] parts) => [tag, 0x80, .. parts.SelectMany(p => p), 0x00, 0x00];

    private static byte[] Utf8(string s) => [0x0C, (byte)s.Length, .. Encoding.UTF8.GetBytes(s)];

    [Fact]
    public void A_provider_tree_with_indefinite_lengths_gives_full_paths()
    {
        byte[] fader = I(0x61, I(0xA0, [0x02, 0x01, 0x03]), I(0xA1, I(0x31,
            I(0xA0, Utf8("Fader")), I(0xA2, [0x09, 0x03, 0xC0, 0x05, 0x05]), I(0xA5, [0x02, 0x01, 0x03]), I(0xAD, [0x02, 0x01, 0x02]))));
        byte[] mic = I(0x61, I(0xA0, [0x02, 0x01, 0x02]), I(0xA1, I(0x31, I(0xA0, Utf8("Mic")), I(0xA2, [0x01, 0x01, 0xFF]))));
        byte[] node = I(0x63, I(0xA0, [0x02, 0x01, 0x01]), I(0xA1, I(0x31, I(0xA0, Utf8("Kanaal 1")))),
            I(0xA2, I(0x64, I(0xA0, mic), I(0xA0, fader))));
        byte[] update = I(0x69, I(0xA0, [0x0D, 0x02, 0x01, 0x02]), I(0xA1, I(0x31, I(0xA2, [0x01, 0x01, 0x00]))));
        var elements = Glow.Parse(I(0x60, I(0x6B, I(0xA0, node), I(0xA0, update))));

        Assert.Equal(new[] { "1", "1.2", "1.3", "1.2" }, elements.Select(e => e.Path));
        Assert.Equal(("node", "Kanaal 1"), (elements[0].Kind, elements[0].Identifier));
        Assert.Equal(("Mic", true, EmberType.Boolean), (elements[1].Identifier, elements[1].Value, elements[1].EffectiveType));
        Assert.Equal(("Fader", -40.0, EmberType.Real, true), (elements[2].Identifier, elements[2].Value, elements[2].EffectiveType, elements[2].Writable));
        var merged = elements[3].Merge(elements[1]);
        Assert.Equal(("Mic", false), (merged.Identifier, merged.Value));
    }

    [Fact]
    public void SetValue_is_read_back_as_the_same_parameter_and_value()
    {
        var e = Assert.Single(Glow.Parse(Glow.SetValue([1, 4, 7], -12.5)));
        Assert.Equal(("1.4.7", "parameter", -12.5), (e.Path, e.Kind, e.Value));
        Assert.Equal(true, Assert.Single(Glow.Parse(Glow.SetValue([2], true))).Value);
        Assert.Equal(3L, Assert.Single(Glow.Parse(Glow.SetValue([2], 3L))).Value);
    }

    [Fact]
    public void Addresses_compare_values_and_match_however_they_were_typed()
    {
        Assert.Equal("ember 1.2.3", EmberAddress.Parse(" Ember 1.2.3 ")!.ToString());
        Assert.Equal("ember 1.3.5 > -40", EmberAddress.Parse("ember 1.3.5>-40")!.ToString());
        Assert.Equal("ember 1.3.5 >= 0.5", EmberAddress.Parse("ember 1.3.5 >= 0,5")!.ToString());
        Assert.Null(EmberAddress.Parse("ember 1..2"));
        Assert.Null(EmberAddress.Parse("ember"));

        var plain = EmberAddress.Parse("ember 1.2")!;
        Assert.Equal(true, plain.IsOn(true));
        Assert.Equal(false, plain.IsOn(0L));
        Assert.Equal(true, plain.IsOn(-3.0));
        Assert.Null(plain.IsOn(null));
        var fader = EmberAddress.Parse("ember 1.3 > -40")!;
        Assert.Equal(true, fader.IsOn(-30.0));
        Assert.Equal(false, fader.IsOn(-40.0));
        Assert.Equal(false, fader.IsOn(double.NegativeInfinity));

        var input = new HwInput(1, 7, "ember 1.3>-40", "PLAYER 1 PLAY");
        Assert.True(HwSignals.Matches(input, new HwEvent(7, "ember 1.3 > -40", true)));
        Assert.False(HwSignals.Matches(input, new HwEvent(7, "ember 1.3", true)));
        var last = new Dictionary<(long, string), bool>();
        Assert.True(HwSignals.IsChange(last, new HwEvent(7, "ember 1.3 > -40", true)));
        Assert.False(HwSignals.IsChange(last, new HwEvent(7, "ember 1.3 > -40", true)));
    }

    [Fact]
    public void Tally_values_follow_the_parameter_type()
    {
        Assert.Equal(true, EmberAddress.ValueFor(EmberType.Boolean, true, ""));
        Assert.Equal(0L, EmberAddress.ValueFor(EmberType.Integer, false, ""));
        Assert.Equal(-6.0, EmberAddress.ValueFor(EmberType.Real, true, "-6"));
        Assert.Equal(2L, EmberAddress.ValueFor(EmberType.Enum, true, "2"));
        Assert.Equal("ON AIR", EmberAddress.ValueFor(EmberType.String, true, "ON AIR"));
        Assert.Equal(false, EmberAddress.ValueFor(EmberType.Boolean, false, "uit"));
        Assert.Null(EmberAddress.ValueFor(EmberType.Trigger, false, ""));
    }
}
