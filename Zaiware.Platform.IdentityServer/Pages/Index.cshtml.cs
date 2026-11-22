using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages;

[Authorize]
public sealed class IndexModel : PageModel
{
    public string? Email => User.FindFirst("email")?.Value ?? User.FindFirstValue(ClaimTypes.Email);
}
