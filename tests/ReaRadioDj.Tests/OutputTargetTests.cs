using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class OutputTargetTests
{
    [Theory]
    [InlineData("dev:3:0", 3, 0, 0u)]
    [InlineData("dev:3:1", 3, 1, 0x2000000u)]
    [InlineData("dev:1:3", 1, 3, 0x4000000u)]
    [InlineData("dev:-1:0", -1, 0, 0u)]
    public void Outputs_are_parsed_with_their_speaker_pair(string text, int device, int pair, uint flags)
    {
        Assert.True(OutputTarget.TryParse(text, out var t));
        Assert.Equal(device, t.Device);
        Assert.Equal(pair, t.Pair);
        Assert.Equal(flags, t.SpeakerFlags);
        Assert.Equal(text, t.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dev:3")]
    [InlineData("dev:3:4")]
    [InlineData("asio:1:0")]
    [InlineData("dev:x:0")]
    public void Unknown_outputs_are_refused(string? text) => Assert.False(OutputTarget.TryParse(text, out _));

    [Fact]
    public void Pairs_are_named_like_the_outputs_on_the_card() =>
        Assert.Equal(["1/2", "3/4", "5/6", "7/8"], Enumerable.Range(0, OutputTarget.Pairs).Select(OutputTarget.Name));
}
