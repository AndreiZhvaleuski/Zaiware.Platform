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

        var model = CreateModel(subjectId, profileSelfService, profiles, authenticators, session);
        model.Security.NewPassword = "New-Password-2026!";
        model.Security.ConfirmPassword = "New-Password-2026!";

        var result = await model.OnPostPasswordAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("PasswordSet", redirect.RouteValues?["status"]);
        await authenticators.Received(1).TrySetPasswordAsync(
            subjectId,
            Arg.Any<ValidatedPlainTextPassword>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ProfileUpdated", "Profile updated.")]
    [InlineData("profileupdated", "Profile updated.")]
    public async Task A_known_status_query_renders_its_message(string value, string expected)
    {
        var subjectId = UserSubjectId.New();
        var profileSelfService = Substitute.For<IUserProfileSelfService>();
        profileSelfService.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateProfile(subjectId));
        var authenticators = Substitute.For<IUserAuthenticatorsSelfService>();
        authenticators.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateAuthenticators(subjectId));

        var model = CreateModel(
            subjectId,
            profileSelfService,
            Substitute.For<IUserProfileAdmin>(),
            authenticators,
            Substitute.For<IUserSessionSignIn>());

        var result = await model.OnGetAsync(value);

        Assert.IsType<PageResult>(result);
        Assert.Equal(expected, model.SuccessMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task A_missing_or_unknown_status_query_renders_no_message(string? value)
    {
        var subjectId = UserSubjectId.New();
        var profileSelfService = Substitute.For<IUserProfileSelfService>();
        profileSelfService.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateProfile(subjectId));
        var authenticators = Substitute.For<IUserAuthenticatorsSelfService>();
        authenticators.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateAuthenticators(subjectId));

        var model = CreateModel(
            subjectId,
            profileSelfService,
            Substitute.For<IUserProfileAdmin>(),
            authenticators,
            Substitute.For<IUserSessionSignIn>());

        var result = await model.OnGetAsync(value);

        Assert.IsType<PageResult>(result);
        Assert.Null(model.SuccessMessage);
    }

    private static AccountModel CreateModel(
        UserSubjectId subjectId,
        IUserProfileSelfService profileSelfService,
        IUserProfileAdmin profiles,
        IUserAuthenticatorsSelfService authenticators,
        IUserSessionSignIn session)
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
            NullLogger<AccountModel>.Instance)
        {
            PageContext = new PageContext(actionContext),
        };
    }
}
