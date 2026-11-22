using System.ComponentModel.DataAnnotations;
using Duende.IdentityServer.DataExtensions;
using Duende.IdentityServer.Services;
using Duende.UserManagement;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

public sealed class LoginModel(
    IOtpSender otpSender,
    IUserProfileAdmin profiles,
    IIdentityServerInteractionService interaction) : PageModel
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? email = null, string? returnUrl = null)
    {
        // A valid session skips the email step; honor the original destination when it is safe.
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrEmpty(returnUrl) && interaction.IsValidReturnUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToPage("/Index");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            Input.Email = email;
        }

        // Keep an existing value when the link does not carry a return URL.
        if (returnUrl is not null)
        {
            TempData["ReturnUrl"] = returnUrl;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Normalize before anything else: EmailAddress equality is case-sensitive and the seeded
        // profile, the OTP address, and the invite check all use the same trimmed lowercase form.
        var normalized = Input.Email.Trim().ToLowerInvariant();

        if (!EmailAddress.TryCreate(normalized, out var email))
        {
            ModelState.AddModelError(
                "Input.Email",
                "That address looks off — check for a typo, then try again.");
            return Page();
        }

        // Decline unknown addresses before dispatching a code. Verification-layer enforcement in
        // InviteOnlyOtpAuthenticator remains the backstop for any other caller.
        var known = await profiles.TryGetAsync(
            EmailCode,
            normalized,
            HttpContext.RequestAborted);

        if (known is null)
        {
            ModelState.AddModelError(
                "Input.Email",
                "No account found for this address. Sign-in is by invitation — ask an administrator for access.");
            return Page();
        }

        var result = await otpSender.TrySendOtpAsync(
            new OtpAddress(OtpChannel.Email, email),
            HttpContext.RequestAborted);

        switch (result)
        {
            case SendOtpResult.Sent sent:
                TempData["OtpToken"] = sent.Token.ToString();
                TempData["OtpEmail"] = normalized;
                // TempData round-trips through JSON, so store primitives it can hand back intact.
                TempData["BlockedUntil"] = sent.SendingBlockedUntilUtc.ToString("O");
                return RedirectToPage("/Account/EnterOtp");

            case SendOtpResult.Blocked blocked:
                ModelState.AddModelError(
                    string.Empty,
                    $"Please wait {Math.Ceiling((blocked.SendingBlockedUntilUtc - DateTimeOffset.UtcNow).TotalSeconds)} second(s) before requesting a new code.");
                return Page();

            case SendOtpResult.SaveFailed:
                ModelState.AddModelError(string.Empty, "Failed to send the code. Please try again.");
                return Page();

            default:
                ModelState.AddModelError(string.Empty, "Failed to send the code. Please try again.");
                return Page();
        }
    }
}
