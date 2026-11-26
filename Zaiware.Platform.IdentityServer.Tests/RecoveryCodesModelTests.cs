using Microsoft.AspNetCore.Mvc;
using Zaiware.Platform.IdentityServer.Pages.Account;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class RecoveryCodesModelTests
{
    [Fact]
    public void Saving_codes_redirects_to_the_account_with_the_saved_status()
    {
        var model = new RecoveryCodesModel();

        var redirect = Assert.IsType<RedirectToPageResult>(model.OnPost());

        Assert.Equal("/Account/Account", redirect.PageName);
        Assert.Equal("RecoveryCodesSaved", redirect.RouteValues?["status"]);
    }
}
