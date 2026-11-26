using System.Text.RegularExpressions;

namespace Zaiware.Platform.IdentityServer.Middleware;

/// <summary>
/// Serves content-fingerprinted static assets with a one-year immutable cache policy.
/// The fingerprint changes with the content, so the URL can be cached forever. Applied
/// through <c>OnStarting</c> so only successful responses are marked.
/// </summary>
public sealed class ImmutableStaticAssetCachingMiddleware(RequestDelegate next)
{
    private static readonly Regex Fingerprint = FingerprintedAssets.Path();

    /// <summary>Runs the middleware.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Fingerprint.IsMatch(context.Request.Path))
        {
            context.Response.OnStarting(static state =>
            {
                var response = ((HttpContext)state).Response;

                if (response.StatusCode == StatusCodes.Status200OK)
                {
                    response.Headers.CacheControl = "public, max-age=31536000, immutable";
                }

                return Task.CompletedTask;
            }, context);
        }

        return next(context);
    }

    private static class FingerprintedAssets
    {
        public static Regex Path() => Zaiware.Platform.IdentityServer.FingerprintedAssets.Path();
    }
}
