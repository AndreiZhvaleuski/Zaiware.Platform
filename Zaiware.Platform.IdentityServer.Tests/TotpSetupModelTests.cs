using System.Security.Claims;
using Duende.IdentityModel;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Totp;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Zaiware.Platform.IdentityServer.Pages.Account;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class TotpSetupModelTests
{
    [Fact]
    public async Task An_expired_setup_key_is_reported_on_the_rendered_page_with_a_new_key()
    {
        var subjectId = UserSubjectId.New();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtClaimTypes.Subject, subjectId.ToString())],
                "test")),
        };
        var actionContext = new ActionContext(context, new RouteData(), new PageActionDescriptor());
        var authenticators = Substitute.For<IUserAuthenticatorsSelfService>();
        var profiles = Substitute.For<IUserProfileSelfService>();
        profiles.TryGetAsync(subjectId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var model = new TotpSetupModel(authenticators, profiles)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(context, new TestTempDataProvider()),
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(
            model.ErrorMessages,
            message => message.Contains("new key", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(model.TempData["TotpKey"]?.ToString()));
        Assert.False(string.IsNullOrWhiteSpace(model.ManualKey));
    }

    [Fact]
    public async Task An_enrolled_user_is_redirected_with_the_already_enabled_status()
    {
        var subjectId = UserSubjectId.New();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtClaimTypes.Subject, subjectId.ToString())],
                "test")),
        };
        var actionContext = new ActionContext(context, new RouteData(), new PageActionDescriptor());
        var authenticators = Substitute.For<IUserAuthenticatorsSelfService>();
        authenticators.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateAuthenticators(
                subjectId,
                totpDevices: [TotpDeviceName.Default]));

        var model = new TotpSetupModel(authenticators, Substitute.For<IUserProfileSelfService>())
        {
            PageContext = new PageContext(actionContext),
        };

        var result = await model.OnGetAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Account/Account", redirect.PageName);
        Assert.Equal("TwoFactorAlreadyEnabled", redirect.RouteValues?["status"]);
    }
}
