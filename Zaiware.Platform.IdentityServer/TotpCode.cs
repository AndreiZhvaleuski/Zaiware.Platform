namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Normalizes user-entered authenticator codes. JavaScript may make the field pleasant to
/// type, but the server always normalizes and validates the submitted value independently.
/// </summary>
public static class TotpCode
{
    /// <summary>The required number of digits in a TOTP code.</summary>
    public const int Length = 6;

    /// <summary>
    /// Removes spaces, hyphens, and every other non-digit character. Returns an empty
    /// string for <see langword="null"/>.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        Span<char> digits = stackalloc char[code.Length];
        var count = 0;

        foreach (var character in code)
        {
            if (char.IsAsciiDigit(character))
            {
                digits[count++] = character;
            }
        }

        return new string(digits[..count]);
    }

    /// <summary>Whether the normalized code contains exactly six digits.</summary>
    public static bool IsValid(string? code) => Normalize(code).Length == Length;
}
