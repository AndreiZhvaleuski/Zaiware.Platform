using Duende.IdentityServer.Services;
using Duende.UserManagement.Authentication.Otp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Zaiware.Platform.IdentityServer.Pages.Account;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class EnterOtpModelTests
{
    [Fact]
    public void Get_keeps_the_pending_return_url_for_the_back_link_and_verification()
    {
        var (model, context, _) = CreateModel();
        model.TempData["OtpToken"] = OtpToken.Create(Guid.CreateVersion7().ToString()).ToString();
        model.TempData["OtpEmail"] = "person@example.test";
        model.TempData["ReturnUrl"] = "/Account";

        var result = model.OnGet();

        Assert.IsType<PageResult>(result);
        Assert.Equal("/Account", model.ReturnUrl);
        Assert.Equal("/Account", model.TempData["ReturnUrl"]);
        Assert.Equal("person@example.test", model.Email);
        Assert.NotNull(context);
    }

    [Fact]
    public void Missing_token_redirect_carries_the_email_and_return_url()
    {
        var (model, _, _) = CreateModel();
        model.TempData["OtpEmail"] = "person@example.test";
        model.TempData["ReturnUrl"] = "/Account";

        var result = model.OnGet();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Account/Login", redirect.PageName);
        Assert.Equal("person@example.test", redirect.RouteValues!["email"]);
        Assert.Equal("/Account", redirect.RouteValues["returnUrl"]);
    }

    [Fact]
    public async Task Rate_limited_resend_renders_the_generic_notice_without_calling_the_sender()
    {
        var limiter = Substitute.For<IOtpSendRateLimiter>();
        limiter.TryAcquire(Arg.Any<string?>())
            .Returns(OtpSendRateLimitResult.Denied(TimeSpan.FromMinutes(5)));
        var (model, _, sender) = CreateModel(limiter);
        model.TempData["OtpToken"] = OtpToken.Create(Guid.CreateVersion7().ToString()).ToString();
        model.TempData["OtpEmail"] = "person@example.test";
        model.TempData["ReturnUrl"] = "/Account";

        var result = await model.OnPostResendAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(IOtpSendRateLimiter.TooManyRequestsMessage, model.ResendNotice);
        Assert.Equal("/Account", model.ReturnUrl);
        await sender.DidNotReceiveWithAnyArgs().TrySendOtpAsync(default!, default);
    }

    private static (EnterOtpModel Model, HttpContext Context, IOtpSender Sender) CreateModel(
        IOtpSendRateLimiter? rateLimiter = null)
    {
        var context = new DefaultHttpContext();
        var actionContext = new ActionContext(context, new RouteData(), new PageActionDescriptor());
        var authenticator = Substitute.For<IOtpAuthenticator>();
        var sender = Substitute.For<IOtpSender>();
        var session = Substitute.For<IUserSessionSignIn>();
        var interaction = Substitute.For<IIdentityServerInteractionService>();
        interaction.IsValidReturnUrl(Arg.Any<string>()).Returns(false);

        var model = new EnterOtpModel(
            authenticator,
            sender,
            rateLimiter ?? new OtpSendRateLimiter(
                TimeProvider.System,
                new OtpSendRateLimitOptions { PerClientLimit = 1000, GlobalLimit = 1000 }),
            session,
            interaction,
            NullLogger<EnterOtpModel>.Instance)
        {
            PageContext = new PageContext(actionContext),
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                context,
                new TestTempDataProvider()),
            Url = new UrlHelper(actionContext),
        };

        return (model, context, sender);
    }
}
