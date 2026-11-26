using System.Security.Claims;
using Duende.IdentityServer.DataExtensions;
using Duende.IdentityServer.Services;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Authentication.Passwords;
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

public sealed class LoginModelTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public async Task Empty_and_whitespace_password_forms_render_validation_instead_of_throwing(
        string identifier,
        string password)
    {
        var (model, _, _, _) = CreateModel();
        model.Input.Identifier = identifier;
        model.Input.Password = password;

        var result = await model.OnPostPasswordAsync();

        Assert.IsType<PageResult>(result);
        Assert.True(model.ModelState.ErrorCount >= 2);
    }

    [Fact]
    public async Task Null_bound_password_values_do_not_throw()
    {
        var (model, _, _, _) = CreateModel();
        model.Input.Identifier = null!;
        model.Input.Password = null!;

        var result = await model.OnPostPasswordAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("Input.Identifier", model.ModelState.Keys);
        Assert.Contains("Input.Password", model.ModelState.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task Invalid_otp_email_forms_render_validation_instead_of_dispatching(string email)
    {
        var (model, _, sender, _) = CreateModel();
        model.Input.Email = email;

        var result = await model.OnPostSendCodeAsync();

        Assert.IsType<PageResult>(result);
        await sender.DidNotReceiveWithAnyArgs().TrySendOtpAsync(default!, default);
    }

    [Fact]
    public async Task Null_otp_email_does_not_throw()
    {
        var (model, _, _, _) = CreateModel();
        model.Input.Email = null!;

        var result = await model.OnPostSendCodeAsync();

        Assert.IsType<PageResult>(result);
    }

    [Fact]
    public async Task Unknown_but_valid_email_dispatches_a_code_instead_of_revealing_absence()
    {
        var (model, _, sender, _) = CreateModel();
        model.Input.Email = "nobody@example.test";
        sender.TrySendOtpAsync(Arg.Any<OtpAddress>(), Arg.Any<CancellationToken>())
            .Returns(Fakes.SentOtp(DateTimeOffset.UtcNow));

        var result = await model.OnPostSendCodeAsync();

        Assert.IsType<RedirectToPageResult>(result);
        await sender.Received(1).TrySendOtpAsync(
            Arg.Is<OtpAddress>(address => address.SubjectId.ToString() == "nobody@example.test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_authenticator_state_fails_closed_and_does_not_sign_in()
    {
        var subjectId = UserSubjectId.New();
        var (model, password, _, userSession) = CreateModel();
        password.TryAuthenticateAsync(
                Arg.Any<AttributeCode>(),
                Arg.Any<string>(),
                Arg.Any<NonValidatedPassword>(),
                Arg.Any<CancellationToken>())
            .Returns(Fakes.PasswordSuccess(subjectId));
        model.Input.Identifier = "someone";
        model.Input.Password = "A-valid-looking-password";

        var result = await model.OnPostPasswordAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(
            model.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("couldn't complete sign-in", StringComparison.Ordinal));
        await userSession.DidNotReceiveWithAnyArgs().SignInAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Rate_limited_otp_send_shows_a_generic_notice_and_never_calls_the_sender()
    {
        var limiter = Substitute.For<IOtpSendRateLimiter>();
        limiter.TryAcquire(Arg.Any<string?>())
            .Returns(OtpSendRateLimitResult.Denied(TimeSpan.FromMinutes(5)));
        var (model, _, sender, _) = CreateModel(limiter);
        model.Input.Email = "nobody@example.test";

        var result = await model.OnPostSendCodeAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(
            model.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage == IOtpSendRateLimiter.TooManyRequestsMessage);
        await sender.DidNotReceiveWithAnyArgs().TrySendOtpAsync(default!, default);
    }

    [Fact]
    public void Login_get_falls_back_to_the_pending_tempdata_return_url_and_keeps_it()
    {
        var (model, _, _, _) = CreateModel();
        model.TempData["ReturnUrl"] = "/Account";

        var result = model.OnGet(returnUrl: null);

        Assert.IsType<PageResult>(result);
        Assert.Equal("/Account", model.Input.ReturnUrl);
        Assert.Equal("/Account", model.TempData["ReturnUrl"]);
    }

    private static (LoginModel Model, IPasswordAuthenticator Password, IOtpSender Sender, IUserSessionSignIn Session)
        CreateModel(IOtpSendRateLimiter? rateLimiter = null)
    {
        var context = new DefaultHttpContext();
        var actionContext = new ActionContext(context, new RouteData(), new PageActionDescriptor());
        var password = Substitute.For<IPasswordAuthenticator>();
        var authenticators = Substitute.For<IUserAuthenticatorsSelfService>();
        var sender = Substitute.For<IOtpSender>();
        var stateCookie = Substitute.For<ISecondFactorStateCookie>();
        var session = Substitute.For<IUserSessionSignIn>();
        var interaction = Substitute.For<IIdentityServerInteractionService>();
        interaction.IsValidReturnUrl(Arg.Any<string>()).Returns(false);

        var model = new LoginModel(
            password,
            authenticators,
            sender,
            rateLimiter ?? new OtpSendRateLimiter(
                TimeProvider.System,
                new OtpSendRateLimitOptions { PerClientLimit = 1000, GlobalLimit = 1000 }),
            stateCookie,
            session,
            interaction,
            NullLogger<LoginModel>.Instance)
        {
            PageContext = new PageContext(actionContext),
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                context,
                new TestTempDataProvider()),
            Url = new UrlHelper(actionContext),
        };

        return (model, password, sender, session);
    }
}
