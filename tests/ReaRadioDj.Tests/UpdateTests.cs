using ReaRadioDj.Contracts;

namespace ReaRadioDj.Tests;

public class UpdateTests
{
    private const string Repo = "https://github.com/jerrydeevee/REA-Radio-DJ";

    [Theory]
    [InlineData("v1.0.1", "1.0.1")]
    [InlineData("1.1.0-rc.1+111c77d", "1.1.0-rc.1")]
    [InlineData("2", "2.0.0")]
    [InlineData("1.2.3.4", "1.2.3")]
    public void Versions_are_read(string text, string expected) =>
        Assert.Equal(expected, ReleaseVersion.Parse(text)!.ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.x")]
    public void Nonsense_is_no_version(string? text) => Assert.Null(ReleaseVersion.Parse(text));

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("1.1.0", "1.1.0-rc.1")]
    [InlineData("1.1.0-rc.2", "1.1.0-rc.1")]
    [InlineData("1.1.0-rc.10", "1.1.0-rc.9")]
    [InlineData("1.1.0-rc.1", "1.0.1")]
    public void Newer_versions_compare_higher(string newer, string older) =>
        Assert.True(ReleaseVersion.Parse(newer)! > ReleaseVersion.Parse(older)!);

    [Fact]
    public void The_build_part_does_not_count() =>
        Assert.Equal(0, ReleaseVersion.Parse("1.1.0+abc")!.CompareTo(ReleaseVersion.Parse("v1.1.0")));

    [Fact]
    public void A_github_answer_is_read()
    {
        var info = ReleaseInfo.Parse("""{"tag_name":"v1.0.1","name":"REA-Radio-DJ 1.0.1","html_url":"https://github.com/jerrydeevee/REA-Radio-DJ/releases/tag/v1.0.1","draft":false}""", Repo)!;
        Assert.Equal("1.0.1", info.Version.ToString());
        Assert.Equal("REA-Radio-DJ 1.0.1", info.Name);
        Assert.EndsWith("/tag/v1.0.1", info.Url);
        Assert.False(info.IsNewerThan("1.1.0-rc.1+111c77d"));
        Assert.True(info.IsNewerThan("1.0.0"));
        Assert.False(info.IsNewerThan("1.0.1"));
    }

    [Fact]
    public void A_link_elsewhere_is_replaced_by_the_project_page()
    {
        var info = ReleaseInfo.Parse("""{"tag_name":"v9.0.0","html_url":"https://example.com/evil"}""", Repo)!;
        Assert.Equal(Repo + "/releases", info.Url);
        Assert.Equal("v9.0.0", info.Name);
    }

    [Theory]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":"v2.0.0","draft":true}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Unusable_answers_give_nothing(string json) => Assert.Null(ReleaseInfo.Parse(json, Repo));
}
