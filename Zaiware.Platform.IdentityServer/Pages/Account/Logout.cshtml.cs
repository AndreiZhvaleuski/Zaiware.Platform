using Duende.IdentityServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

public sealed class LogoutModel : PageModel
{
    public async Task<IActionResult> OnPost()
    {
        await HttpContext.SignOutAsync(IdentityServerConstants.DefaultCookieAuthenticationScheme);
        return RedirectToPage("/Account/Login");
    }
}
