using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Validates and follows post-sign-in return URLs. A URL is accepted only when it is
/// application-local or explicitly allowed by IdentityServer (registered client redirect
/// URIs). <see cref="PageModel.LocalRedirect(string)"/> throws for non-local URLs, so
/// externally valid URLs must go through <see cref="ControllerBase.Redirect(string)"/>.
/// </summary>
public static class SafeReturnUrl
{
    /// <summary>
    /// Returns the URL when it is local or an IdentityServer-validated return URL;
    /// otherwise returns <see langword="null"/>.
    /// </summary>
    public static string? Normalize(
        string? returnUrl,
        IIdentityServerInteractionService interaction,
        IUrlHelper url)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(url);

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        return url.IsLocalUrl(returnUrl) || interaction.IsValidReturnUrl(returnUrl)
            ? returnUrl
            : null;
    }

    /// <summary>
    /// Redirects to the normalized return URL, or to the fallback page when none was
    /// supplied. Local URLs use <see cref="PageModel.LocalRedirect(string)"/>; validated
    /// external URLs use <see cref="PageModel.Redirect(string)"/>.
    /// </summary>
    public static IActionResult Redirect(
        PageModel page,
        string? returnUrl,
        string fallbackPage = "/Index")
    {
        ArgumentNullException.ThrowIfNull(page);

        if (returnUrl is null)
        {
            return page.RedirectToPage(fallbackPage);
        }

        return page.Url.IsLocalUrl(returnUrl)
            ? new LocalRedirectResult(returnUrl)
            : new RedirectResult(returnUrl);
    }
}
