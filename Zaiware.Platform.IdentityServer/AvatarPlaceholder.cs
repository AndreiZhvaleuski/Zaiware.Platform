using System.Net;
using System.Text;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Generates the inline SVG initials placeholder shown when a user has no avatar object.
/// </summary>
public static class AvatarPlaceholder
{
    /// <summary>
    /// Builds a 256×256 SVG with the supplied initials. The initials are escaped so a
    /// display name can never inject markup into the response.
    /// </summary>
    public static string Create(string? displayName, string? username, string? email)
    {
        var initials = Initials(displayName, username, email);
        var safeInitials = WebUtility.HtmlEncode(initials);

        return $$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" role="img" aria-label="Avatar placeholder">
              <rect width="256" height="256" rx="56" fill="#1c4fd8"/>
              <text x="50%" y="50%" dominant-baseline="central" text-anchor="middle"
                    font-family="system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
                    font-size="96" font-weight="650" fill="#ffffff">{{safeInitials}}</text>
            </svg>
            """;
    }

    /// <summary>
    /// Computes the initials shown in the placeholder: first and last word initials for a
    /// display name, the first two username characters, the first email character, or
    /// <c>?</c> when no value is available.
    /// </summary>
    public static string Initials(string? displayName, string? username, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var initials = words.Length >= 2
                ? string.Concat(words[0][0], words[^1][0])
                : words[0][..1];

            return initials.ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return username[..Math.Min(2, username.Length)].ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email[..1].ToUpperInvariant();
        }

        return "?";
    }
}
