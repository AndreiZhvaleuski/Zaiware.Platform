using System.Security.Claims;
using Duende.IdentityModel;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Passwords;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Zaiware.Platform.IdentityServer.Pages.Account;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class AccountModelTests
{
    [Fact]
    public async Task Sets_a_first_password_when_the_user_has_none()
    {
        var subjectId = UserSubjectId.New();
        var profileSelfService = Substitute.For<IUserProfileSelfService>();
        var profiles = Substitute.For<IUserProfileAdmin>();
        var authenticators = Substitute.For<IUserAuthenticatorsSelfService>();
        var flash = Substitute.For<IFlashMessage>();
        var session = Substitute.For<IUserSessionSignIn>();

        profileSelfService.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateProfile(subjectId));
        authenticators.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateAuthenticators(subjectId, hasPassword: false));
        authenticators.TryValidatePasswordAsync(subjectId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PasswordCreationResult.Success(Fakes.ValidatedPassword("New-Password-2026!")));
        authenticators.TrySetPasswordAsync(
                subjectId,
                Arg.Any<ValidatedPlainTextPassword>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var model = CreateModel(subjectId, profileSelfService, profiles, authenticators, session, flash);
        model.Security.NewPassword = "New-Password-2026!";
        model.Security.ConfirmPassword = "New-Password-2026!";

        var result = await model.OnPostPasswordAsync();

        Assert.IsType<RedirectToPageResult>(result);
        await authenticators.Received(1).TrySetPasswordAsync(
            subjectId,
            Arg.Any<ValidatedPlainTextPassword>(),
            Arg.Any<CancellationToken>());
        flash.Received(1).SetSuccess("Password set.");
    }

    private static AccountModel CreateModel(
        UserSubjectId subjectId,
        IUserProfileSelfService profileSelfService,
        IUserProfileAdmin profiles,
        IUserAuthenticatorsSelfService authenticators,
        IUserSessionSignIn session,
        IFlashMessage flash)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtClaimTypes.Subject, subjectId.ToString())],
                "test")),
        };
        var actionContext = new ActionContext(context, new RouteData(), new PageActionDescriptor());

        return new AccountModel(
            profileSelfService,
            profiles,
            authenticators,
            Substitute.For<IAvatarStorage>(),
            session,
            flash,
            NullLogger<AccountModel>.Instance)
        {
            PageContext = new PageContext(actionContext),
        };
    }
}
