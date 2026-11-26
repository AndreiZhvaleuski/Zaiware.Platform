namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Page-scoped success messages. The key includes the destination page path so a message
/// set by the Account page cannot leak into, or be consumed by, the OTP flow or another
/// account sub-page that still uses raw TempData.
/// </summary>
public interface IFlashMessage
{
    /// <summary>Stores a success message for the current request path.</summary>
    void SetSuccess(string message);

    /// <summary>Stores a success message for an explicit destination page path.</summary>
    void SetSuccess(string message, string pagePath);

    /// <summary>Reads and removes the success message for the current request path.</summary>
    string? ConsumeSuccess();
}
