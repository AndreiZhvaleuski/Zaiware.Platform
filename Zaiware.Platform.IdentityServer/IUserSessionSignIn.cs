using Duende.UserManagement;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Establishes the IdentityServer session cookie for an authenticated user.
/// </summary>
public interface IUserSessionSignIn
{
    /// <summary>
    /// Reads the profile, builds the sign-in principal, and issues the IdentityServer cookie.
    /// </summary>
    /// <param name="context">The current request context.</param>
    /// <param name="subjectId">The authenticated user.</param>
    /// <param name="authenticationMethod">The <c>amr</c> value (for example <c>pwd</c>, <c>mfa</c>, or <c>otp</c>).</param>
    /// <param name="ct">The cancellation token.</param>
    Task SignInAsync(
        HttpContext context,
        UserSubjectId subjectId,
        string authenticationMethod,
        CancellationToken ct);
}
