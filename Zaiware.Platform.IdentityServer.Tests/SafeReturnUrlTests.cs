using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class SafeReturnUrlTests
{
    [Theory]
    [InlineData("/Account", "/Account")]
    [InlineData("/", "/")]
    [InlineData("https://client.example/callback", "https://client.example/callback")]
    [InlineData("https://evil.example/", null)]
    [InlineData("//evil.example/", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_accepts_local_and_identityserver_validated_urls(
        string? returnUrl,
        string? expected)
    {
        var interaction = Substitute.For<IIdentityServerInteractionService>();
        interaction.IsValidReturnUrl("https://client.example/callback").Returns(true);
        var (_, url) = CreatePage();

        Assert.Equal(expected, SafeReturnUrl.Normalize(returnUrl, interaction, url));
    }

    [Fact]
    public void Redirect_uses_LocalRedirect_for_local_and_Redirect_for_validated_external_urls()
    {
        var (page, _) = CreatePage();

        var local = SafeReturnUrl.Redirect(page, "/Account");
        var external = SafeReturnUrl.Redirect(page, "https://client.example/callback");
        var fallback = SafeReturnUrl.Redirect(page, null);

        var localResult = Assert.IsType<LocalRedirectResult>(local);
        Assert.Equal("/Account", localResult.Url);

        var externalResult = Assert.IsType<RedirectResult>(external);
        Assert.Equal("https://client.example/callback", externalResult.Url);

        var fallbackResult = Assert.IsType<RedirectToPageResult>(fallback);
        Assert.Equal("/Index", fallbackResult.PageName);
    }

    private static (PageModel Page, IUrlHelper Url) CreatePage()
    {
        var context = new DefaultHttpContext();
        var actionContext = new ActionContext(context, new RouteData(), new PageActionDescriptor());
        var url = new UrlHelper(actionContext);
        var page = new TestPage { PageContext = new PageContext(actionContext), Url = url };
        return (page, url);
    }

    private sealed class TestPage : PageModel;
}
