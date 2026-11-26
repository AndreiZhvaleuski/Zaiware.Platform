namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class AvatarPlaceholderTests
{
    [Theory]
    [InlineData("Ada Lovelace", null, null, "AL")]
    [InlineData("Ada", null, null, "A")]
    [InlineData(null, "grace", null, "GR")]
    [InlineData(null, "g", null, "G")]
    [InlineData(null, null, "linus@example.com", "L")]
    [InlineData(null, null, null, "?")]
    [InlineData("   ", "  ", "  ", "?")]
    public void Initials_uses_the_documented_fallback_order(
        string? displayName,
        string? username,
        string? email,
        string expected)
    {
        Assert.Equal(expected, AvatarPlaceholder.Initials(displayName, username, email));
    }

    [Fact]
    public void Initials_handles_unicode_display_names()
    {
        Assert.Equal("ÉL", AvatarPlaceholder.Initials("Élodie Laurent", null, null));
    }

    [Fact]
    public void Create_escapes_initials_so_display_names_cannot_inject_markup()
    {
        var svg = AvatarPlaceholder.Create("<script>", null, null);

        Assert.Contains("&lt;", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", svg, StringComparison.Ordinal);
    }
}
