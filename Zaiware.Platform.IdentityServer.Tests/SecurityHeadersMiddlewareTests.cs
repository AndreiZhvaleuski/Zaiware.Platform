using Microsoft.AspNetCore.Http;
using Zaiware.Platform.IdentityServer.Middleware;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Csp_allows_the_configured_public_avatar_origin_without_widening_img_src()
    {
        var context = await InvokeAsync(new AvatarStorageOptions
        {
            ServiceUrl = "http://rustfs:9000",
            PublicServiceUrl = "http://127.0.0.1:9000",
            AccessKey = "key",
            SecretKey = "secret",
            BucketName = "avatars",
        });

        var csp = context.Response.Headers.ContentSecurityPolicy.ToString();

        Assert.Contains("img-src 'self' data: http://127.0.0.1:9000", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("img-src *", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("http://rustfs:9000", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csp_falls_back_to_the_service_origin_when_no_public_endpoint_is_configured()
    {
        var context = await InvokeAsync(new AvatarStorageOptions
        {
            ServiceUrl = "http://localhost:9000",
            AccessKey = "key",
            SecretKey = "secret",
            BucketName = "avatars",
        });

        var csp = context.Response.Headers.ContentSecurityPolicy.ToString();

        Assert.Contains("img-src 'self' data: http://localhost:9000", csp, StringComparison.Ordinal);
    }

    private static async Task<HttpContext> InvokeAsync(AvatarStorageOptions options)
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, options);

        await middleware.InvokeAsync(context);

        return context;
    }
}
