namespace Zaiware.Platform.IdentityServer.Middleware;

/// <summary>
/// Marks the signed-in landing page and every account/sign-in page as non-cacheable.
/// Headers are applied through <c>OnStarting</c> so redirects (including the 302 that
/// bounces an anonymous user to sign-in) carry them as well as rendered pages.
/// </summary>
public sealed class AuthPageNoStoreMiddleware(RequestDelegate next)
{
    /// <summary>Whether the path belongs to the auth/account surface.</summary>
    public static bool IsAuthPage(PathString path) =>
        path == "/" || path.StartsWithSegments("/Account");

    /// <summary>Runs the middleware.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsAuthPage(context.Request.Path))
        {
            context.Response.OnStarting(static state =>
            {
                var response = ((HttpContext)state).Response;
                response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                response.Headers.Pragma = "no-cache";
                return Task.CompletedTask;
            }, context);
        }

        return next(context);
    }
}
