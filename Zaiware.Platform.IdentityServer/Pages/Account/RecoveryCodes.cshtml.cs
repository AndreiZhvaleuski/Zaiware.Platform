using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

/// <summary>
/// Displays freshly generated recovery codes exactly once. The codes travel through
/// TempData so a refresh or a later visit cannot reveal them again.
/// </summary>
[Authorize]
public sealed class RecoveryCodesModel : PageModel
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
        TempData["SuccessMessage"] = "Two-factor authentication enabled and recovery codes saved.";
        return RedirectToPage("/Account/Account");
    }
}
