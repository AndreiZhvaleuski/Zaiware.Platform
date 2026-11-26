namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class TotpCodeTests
{
    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData(" 123 - 456 ", "123456")]
    [InlineData("12a34b56", "123456")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_keeps_only_digits(string? input, string expected) =>
        Assert.Equal(expected, TotpCode.Normalize(input));

    [Theory]
    [InlineData("123456", true)]
    [InlineData("123 456", true)]
    [InlineData("123-456", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("abcdef", false)]
    [InlineData(null, false)]
    public void IsValid_requires_exactly_six_digits(string? input, bool expected) =>
        Assert.Equal(expected, TotpCode.IsValid(input));
}
