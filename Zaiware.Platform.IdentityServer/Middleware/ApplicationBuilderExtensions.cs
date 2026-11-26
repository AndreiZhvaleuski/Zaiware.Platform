namespace Zaiware.Platform.IdentityServer.Middleware;

/// <summary>Pipeline registrations for the small middleware used by the identity host.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>Adds the auth/account no-store middleware.</summary>
    public static IApplicationBuilder UseAuthPageNoStore(this IApplicationBuilder app) =>
        app.UseMiddleware<AuthPageNoStoreMiddleware>();

    /// <summary>Adds immutable caching for fingerprinted static assets.</summary>
    public static IApplicationBuilder UseImmutableStaticAssetCaching(this IApplicationBuilder app) =>
        app.UseMiddleware<ImmutableStaticAssetCachingMiddleware>();

    /// <summary>Adds the security headers and CSP nonce middleware.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
