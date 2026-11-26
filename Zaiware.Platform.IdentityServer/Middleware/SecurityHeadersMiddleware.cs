using System.Security.Cryptography;

namespace Zaiware.Platform.IdentityServer.Middleware;

/// <summary>
/// Adds the browser hardening headers used across the identity host and a per-request
/// CSP nonce for the inline theme bootstrap in <c>_Layout</c>. The nonce is stored in
/// <see cref="NonceItemKey"/> so Razor can put it on exactly that one script tag.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>The <see cref="HttpContext.Items"/> key holding the CSP nonce.</summary>
    public const string NonceItemKey = "csp-nonce";

    /// <summary>Runs the middleware.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceItemKey] = nonce;

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = string.Join(
            "; ",
            [
                "default-src 'self'",
                "base-uri 'self'",
                "frame-ancestors 'none'",
                "object-src 'none'",
                $"script-src 'self' 'nonce-{nonce}'",
                "style-src 'self'",
                "img-src 'self' data:",
                "form-action 'self'",
            ]);

        return next(context);
    }
}
