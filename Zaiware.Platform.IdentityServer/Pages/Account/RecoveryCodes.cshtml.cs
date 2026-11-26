using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zaiware.Platform.IdentityServer;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

/// <summary>
/// Displays freshly generated recovery codes exactly once. The codes travel through
/// TempData so a refresh or a later visit cannot reveal them again.
/// </summary>
[Authorize]
public sealed class RecoveryCodesModel(IFlashMessage flashMessage) : PageModel
{
    public string[] Codes { get; private set; } = [];

    public IActionResult OnGet()
    {
        Codes = TempData["RecoveryCodes"] as string[] ?? [];

        if (Codes.Length == 0)
        {
            return RedirectToPage("/Account/Account");
        }

        return Page();
    }

    public IActionResult OnPost()
    {
        // Neutral text: this page is reached both after first enrollment and after
        // regeneration, so claiming "enabled" would be wrong for the latter.
        flashMessage.SetSuccess("Recovery codes saved.", "/Account");
        return RedirectToPage("/Account/Account");
    }
}
