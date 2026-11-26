using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Duende.UserManagement;
using Microsoft.AspNetCore.DataProtection;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Stores the interim authenticated subject id between the password step and the
/// second-factor step. The cookie is encrypted with Data Protection, is scoped to the
/// host with the <c>__Host-</c> prefix, and carries a server-checked issue time so a
/// stolen browser cookie cannot be replayed after five minutes even if the browser
/// ignored the expiration. It never grants access on its own; the challenge page still
/// has to verify a TOTP or recovery code.
/// </summary>
/// <remarks>
/// <c>__Host-</c> requires HTTPS (Secure, no Domain, Path=/). There is no plain-HTTP
/// fallback: a request over HTTP still receives this Secure cookie, the browser refuses
/// to store or send it, and the challenge page bounces back to sign-in. Second-factor
/// sign-in therefore requires HTTPS; the AppHost SelfHost profile is explicitly HTTPS.
/// </remarks>
public sealed class SecondFactorStateCookie(
    IDataProtectionProvider dataProtectionProvider,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider) : ISecondFactorStateCookie
{
    /// <summary>The only cookie name: the <c>__Host-</c> prefix requires HTTPS.</summary>
    public const string CookieName = "__Host-SecondFactorState";

    /// <summary>How long a half-authenticated state remains usable.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("SecondFactorState.v1");

    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("No active HttpContext is available.");

    /// <summary>Writes the encrypted subject id for the pending second-factor step.</summary>
    public void Write(UserSubjectId subjectId)
    {
        var now = timeProvider.GetUtcNow();
        var json = JsonSerializer.Serialize(new CookieValues(subjectId.ToString(), now));
        var protectedJson = _protector.Protect(json);

        HttpContext.Response.Cookies.Append(CookieName, protectedJson, CreateOptions(now.Add(Lifetime)));
    }

    /// <summary>
    /// Reads and decrypts the pending subject id. Returns <see langword="false"/> for a
    /// missing, stale, tampered, or malformed cookie.
    /// </summary>
    public bool TryRead([NotNullWhen(true)] out UserSubjectId? subjectId)
    {
        subjectId = null;

        if (!HttpContext.Request.Cookies.TryGetValue(CookieName, out var protectedJson)
            || string.IsNullOrWhiteSpace(protectedJson))
        {
            return false;
        }

        if (TryUnprotect(protectedJson, out var values)
            && values is not null
            && IsFresh(values.IssuedAtUtc)
            && UserSubjectId.TryCreate(values.SubjectId, out subjectId))
        {
            return true;
        }

        // The cookie exists but is not usable; remove it so the browser stops sending it.
        Clear();
        return false;
    }

    /// <summary>Deletes the pending second-factor state.</summary>
    public void Clear() =>
        HttpContext.Response.Cookies.Delete(CookieName, CreateOptions(expires: null));

    private bool IsFresh(DateTimeOffset issuedAtUtc)
    {
        var now = timeProvider.GetUtcNow();

        // Reject payloads from the future as well as stale ones; a small clock skew is allowed.
        return issuedAtUtc <= now.AddMinutes(1) && now - issuedAtUtc <= Lifetime;
    }

    private bool TryUnprotect(string protectedJson, [NotNullWhen(true)] out CookieValues? values)
    {
        values = null;

        try
        {
            var json = _protector.Unprotect(protectedJson);
            values = JsonSerializer.Deserialize<CookieValues>(json);
            return values is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static CookieOptions CreateOptions(DateTimeOffset? expires) =>
        new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
            Expires = expires,
        };

    private sealed record CookieValues(string SubjectId, DateTimeOffset IssuedAtUtc);
}
