using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class FlashMessageTests
{
    [Fact]
    public void A_message_is_consumed_once_and_only_by_its_page()
    {
        var context = Fakes.CreateHttpContext("/Account");
        var (flash, tempData) = Create(context);

        flash.SetSuccess("Profile updated.");

        Assert.True(tempData.ContainsKey("flash:success:/Account"));

        context.Request.Path = "/Account/RecoveryCodes";
        Assert.Null(flash.ConsumeSuccess());
        Assert.True(tempData.ContainsKey("flash:success:/Account"));

        context.Request.Path = "/Account";
        Assert.Equal("Profile updated.", flash.ConsumeSuccess());
        Assert.False(tempData.ContainsKey("flash:success:/Account"));
        Assert.Null(flash.ConsumeSuccess());
    }

    [Fact]
    public void An_explicit_destination_page_is_honoured()
    {
        var context = Fakes.CreateHttpContext("/Account/RecoveryCodes");
        var (flash, _) = Create(context);

        flash.SetSuccess("Recovery codes saved.", "/Account");

        context.Request.Path = "/Account";
        Assert.Equal("Recovery codes saved.", flash.ConsumeSuccess());
    }

    [Fact]
    public void Trailing_slashes_normalize_to_the_same_key()
    {
        var context = Fakes.CreateHttpContext("/Account/");
        var (flash, tempData) = Create(context);

        flash.SetSuccess("Avatar updated.");

        Assert.True(tempData.ContainsKey("flash:success:/Account"));
    }

    private static (FlashMessage Flash, TempDataDictionary TempData) Create(DefaultHttpContext context)
    {
        var tempData = new TempDataDictionary(context, new TestTempDataProvider());
        var accessor = new HttpContextAccessor { HttpContext = context };
        return (new FlashMessage(new TestTempDataFactory(tempData), accessor), tempData);
    }

    private sealed class TestTempDataFactory(TempDataDictionary tempData) : ITempDataDictionaryFactory
    {
        public ITempDataDictionary GetTempData(HttpContext context) => tempData;
    }
}
