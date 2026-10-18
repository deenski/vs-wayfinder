using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class PlayerNameParserTests
{
    [Fact]
    public void Trims_a_name()
    {
        var ok = PlayerNameParser.TryParse("  Maili  ", out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Maili", name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_name_explains_the_usage(string? text)
    {
        var ok = PlayerNameParser.TryParse(text, out _, out var error);

        Assert.False(ok);
        Assert.Equal(PlayerNameParser.Usage, error);
    }

    [Fact]
    public void Rejects_a_name_longer_than_64_characters()
    {
        var ok = PlayerNameParser.TryParse(new string('a', 65), out _, out var error);

        Assert.False(ok);
        Assert.Equal("That player name is too long.", error);
    }

    [Fact]
    public void Accepts_a_name_of_64_characters()
    {
        var ok = PlayerNameParser.TryParse(new string('a', 64), out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal(64, name.Length);
    }

    [Fact]
    public void Rejects_control_characters()
    {
        var ok = PlayerNameParser.TryParse("Mai\nli", out _, out var error);

        Assert.False(ok);
        Assert.Equal("That player name is not valid.", error);
    }
}
