using Duende.UserManagement;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Zaiware.Platform.IdentityServer.Pages.Avatars;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class AvatarEndpointTests
{
    [Fact]
    public async Task Placeholder_responses_are_never_cacheable()
    {
        var storage = Substitute.For<IAvatarStorage>();
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var profiles = Substitute.For<IUserProfileSelfService>();
        profiles.TryGetAsync(Arg.Any<UserSubjectId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UserProfile?>(null));
        var context = Fakes.CreateHttpContext("/avatars/" + UserSubjectId.New());
        var subjectId = UserSubjectId.New();
        var model = new IndexModel(storage, profiles, NullLogger<IndexModel>.Instance)
        {
            PageContext = new PageContext(new ActionContext(context, new RouteData(), new PageActionDescriptor())),
        };

        var result = await model.OnGetAsync(subjectId.ToString());

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("image/svg+xml", content.ContentType);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Existing_avatars_redirect_to_a_presigned_url_without_caching()
    {
        var subjectId = UserSubjectId.New();
        var storage = Substitute.For<IAvatarStorage>();
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        storage.CreatePresignedGetUrl(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns("http://localhost:9000/avatars/key?X-Amz-Signature=test");
        var profiles = Substitute.For<IUserProfileSelfService>();
        var context = Fakes.CreateHttpContext("/avatars/" + subjectId);
        var model = new IndexModel(storage, profiles, NullLogger<IndexModel>.Instance)
        {
            PageContext = new PageContext(new ActionContext(context, new RouteData(), new PageActionDescriptor())),
        };

        var result = await model.OnGetAsync(subjectId.ToString());

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("X-Amz-Signature", redirect.Url, StringComparison.Ordinal);
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl.ToString());
    }
}
