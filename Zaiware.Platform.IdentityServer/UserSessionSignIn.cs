using System.Security.Claims;
using Duende.IdentityModel;
using Duende.IdentityServer;
using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Authentication;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Establishes the IdentityServer session cookie with the claims the rest of the platform
/// relies on (<c>sub</c>, email, display name, username, and <c>amr</c>). Password,
/// second-factor, and one-time-code sign-ins all use this helper so the claim shape is
/// identical regardless of the authentication method.
/// </summary>
/// <remarks>
/// The cookie is deliberately a browser-session cookie (<c>IsPersistent = false</c>, no
/// <c>ExpiresUtc</c>). The previous OTP flow used a session cookie, and this change keeps
/// that behavior for password and TOTP sign-ins too: closing the browser ends the local
/// session instead of leaving an eight-hour persistent credential on a shared machine.
/// </remarks>
public sealed class UserSessionSignIn(IUserProfileSelfService profiles) : IUserSessionSignIn
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");

    /// <summary>
    /// Reads the profile, builds the sign-in principal, and issues the IdentityServer cookie.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <param name="subjectId">The authenticated user.</param>
    /// <param name="authenticationMethod">The <c>amr</c> value (for example <c>pwd</c>, <c>mfa</c>, or <c>otp</c>).</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task SignInAsync(
        HttpContext context,
        UserSubjectId subjectId,
        string authenticationMethod,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subjectId);

        var profile = await profiles.TryGetAsync(subjectId, ct);
        var additionalClaims = new List<Claim>
        {
            new(JwtClaimTypes.AuthenticationMethod, authenticationMethod),
        };

        string? displayName = null;

        if (profile is not null)
        {
            if (TryGetString(profile, EmailCode, out var email))
            {
                additionalClaims.Add(new Claim(JwtClaimTypes.Email, email));
            }

            if (TryGetString(profile, ProfileSchema.PreferredUsername.Code, out var username))
            {
                additionalClaims.Add(new Claim(JwtClaimTypes.PreferredUserName, username));
            }

            if (TryGetString(profile, OidcStandardAttributes.Name.Code, out var name))
            {
                displayName = name;
            }
        }

        var user = new IdentityServerUser(subjectId.ToString())
        {
            DisplayName = displayName,
            AdditionalClaims = additionalClaims,
        };

        await context.SignInAsync(user, new AuthenticationProperties
        {
            // Session cookie: no ExpiresUtc and IsPersistent = false. The server-side
            // session and the browser lifetime govern how long the user stays signed in.
            IsPersistent = false,
            IssuedUtc = DateTimeOffset.UtcNow,
            AllowRefresh = true,
        });
    }

    private static bool TryGetString(UserProfile profile, AttributeCode code, out string value)
    {
        value = string.Empty;

        if (!profile.Attributes.TryGetValue(code, out var attribute)
            || !attribute.TryGetValue<string>(out var text)
            || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }
}
