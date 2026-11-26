using System.Security.Claims;
using Duende.IdentityServer;
using Duende.IdentityServer.Configuration;
using Duende.UserManagement;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class UserSessionSignInTests
{
    [Fact]
    public async Task Issues_a_non_persistent_session_with_amr()
    {
        var subjectId = UserSubjectId.New();
        var profiles = Substitute.For<IUserProfileSelfService>();
        profiles.TryGetAsync(subjectId, Arg.Any<CancellationToken>())
            .Returns(Fakes.CreateProfile(subjectId));

        var authentication = new CapturingAuthenticationService();
        var services = new ServiceCollection();
        services.AddAuthentication();
        services.AddSingleton<IAuthenticationService>(authentication);
        var identityServerOptions = new IdentityServerOptions();
        identityServerOptions.Authentication.CookieAuthenticationScheme =
            IdentityServerConstants.DefaultCookieAuthenticationScheme;
        services.AddSingleton(identityServerOptions);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };

        await new UserSessionSignIn(profiles).SignInAsync(context, subjectId, "mfa", CancellationToken.None);

        Assert.NotNull(authentication.Principal);
        Assert.NotNull(authentication.Properties);
        Assert.Equal(subjectId.ToString(), authentication.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? authentication.Principal.FindFirst("sub")?.Value);
        Assert.Equal("mfa", authentication.Principal.FindFirst("amr")?.Value);
        Assert.False(authentication.Properties!.IsPersistent);
        Assert.Null(authentication.Properties.ExpiresUtc);
    }

    private sealed class CapturingAuthenticationService : IAuthenticationService
    {
        public ClaimsPrincipal? Principal { get; private set; }

        public AuthenticationProperties? Properties { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            Principal = principal;
            Properties = properties;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }
}
