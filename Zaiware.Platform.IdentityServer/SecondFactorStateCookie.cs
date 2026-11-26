using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Duende.UserManagement;
using Microsoft.AspNetCore.DataProtection;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Stores the interim authenticated subject id between the password step and the
/// second-factor step. The cookie is encrypted with Data Protection, is scoped to the
/// host (<c>__Host-</c> requires HTTPS), and expires after five minutes. It never
/// grants access on its own; the challenge page still has to verify a TOTP or recovery code.
/// </summary>
public sealed class SecondFactorStateCookie(
    IDataProtectionProvider dataProtectionProvider,
    IHttpContextAccessor httpContextAccessor)
{
    private const string CookieName = "__Host-SecondFactorState";
    private const int CookieLifetimeMinutes = 5;

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("SecondFactorState.v1");

    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("No active HttpContext is available.");

    /// <summary>Writes the encrypted subject id for the pending second-factor step.</summary>
    public void Write(UserSubjectId subjectId)
    {
        var json = JsonSerializer.Serialize(new CookieValues(subjectId.ToString()));
        var protectedJson = _protector.Protect(json);

        HttpContext.Response.Cookies.Append(CookieName, protectedJson, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddMinutes(CookieLifetimeMinutes),
        });
    }

    /// <summary>
    /// Reads and decrypts the pending subject id. Returns <see langword="false"/> for a
    /// missing, expired, tampered, or malformed cookie.
    /// </summary>
    public bool TryRead([NotNullWhen(true)] out UserSubjectId? subjectId)
    {
        subjectId = null;

        if (!HttpContext.Request.Cookies.TryGetValue(CookieName, out var protectedJson)
            || string.IsNullOrWhiteSpace(protectedJson))
        {
            return false;
        }

        string json;

        try
        {
            json = _protector.Unprotect(protectedJson);
        }
        catch (CryptographicException)
        {
            return false;
        }

        CookieValues? values;

        try
        {
            values = JsonSerializer.Deserialize<CookieValues>(json);
        }
        catch (JsonException)
        {
            return false;
        }

        return values is not null
            && UserSubjectId.TryCreate(values.SubjectId, out subjectId);
    }

    /// <summary>Deletes the pending second-factor state.</summary>
    public void Clear() =>
        HttpContext.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
        });

    private sealed record CookieValues(string SubjectId);
}
