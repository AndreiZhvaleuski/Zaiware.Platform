using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Stores success messages in TempData under a page-scoped key
/// (<c>flash:success:{pagePath}</c>). Only the page named in the key consumes and removes
/// the value.
/// </summary>
public sealed class FlashMessage(
    ITempDataDictionaryFactory tempDataFactory,
    IHttpContextAccessor httpContextAccessor) : IFlashMessage
{
    private const string KeyPrefix = "flash:success:";

    /// <inheritdoc />
    public void SetSuccess(string message) =>
        SetSuccess(message, CurrentPagePath());

    /// <inheritdoc />
    public void SetSuccess(string message, string pagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        TempData[Key(pagePath)] = message;
    }

    /// <inheritdoc />
    public string? ConsumeSuccess()
    {
        var key = Key(CurrentPagePath());
        var message = TempData[key]?.ToString();
        TempData.Remove(key);
        return message;
    }

    private ITempDataDictionary TempData =>
        tempDataFactory.GetTempData(
            httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HttpContext is available."));

    private string CurrentPagePath() =>
        Normalize(httpContextAccessor.HttpContext?.Request.Path.Value ?? "/");

    private static string Key(string pagePath) => KeyPrefix + Normalize(pagePath);

    private static string Normalize(string pagePath)
    {
        var normalized = pagePath.Trim();
        return normalized.Length == 0 ? "/" : normalized.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";
    }
}
