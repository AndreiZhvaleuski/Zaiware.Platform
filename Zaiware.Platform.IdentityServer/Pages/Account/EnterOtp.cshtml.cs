using System.Globalization;
using System.Security.Claims;
using Duende.IdentityServer;
using Duende.IdentityServer.Services;
using Duende.UserManagement;
using Duende.UserManagement.Authentication.Otp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

public sealed class EnterOtpModel(
    IOtpAuthenticator otpAuthenticator,
    IOtpSender otpSender,
    IIdentityServerInteractionService interaction) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Email { get; private set; }

    public DateTimeOffset? SendingBlockedUntilUtc { get; private set; }

    public string? ResendNotice { get; private set; }

    public sealed class InputModel
    {
        public string Token { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        // A GUID-shaped TempData string deserializes as System.Guid, so read it as text rather
        // than assuming the cookie hands back a string.
        var token = TempData["OtpToken"]?.ToString();

        if (string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Account/Login");
        }

        Input.Token = token;
        PopulateFromTempData();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Input.Token))
        {
            return RedirectToPage("/Account/Login");
        }

        if (!OtpToken.TryCreate(Input.Token, out var token))
        {
            ModelState.AddModelError(
                string.Empty,
                "Your sign-in session has expired. Please request a new code.");
            PopulateFromTempData();
            return Page();
        }

        PopulateFromTempData();

        if (!PlainTextOtp.TryCreate(Input.Code, out var otp))
        {
            ModelState.AddModelError(
                "Input.Code",
                "Enter all 8 characters from the email we sent.");
            return Page();
        }

        var authResult = await otpAuthenticator.TryAuthenticateAsync(
            otp,
            token,
            HttpContext.RequestAborted);

        if (authResult is not OtpAuthenticationResult.Success success)
        {
            ModelState.AddModelError(
                "Input.Code",
                "That code has expired or is not correct. Check the latest email, or request a new code.");
            return Page();
        }

        var claims = new[]
        {
            new Claim("sub", success.UserSubjectId.ToString()),
            new Claim("email", success.Address.SubjectId.ToString()),
        };

        var identity = new ClaimsIdentity(claims, IdentityServerConstants.DefaultCookieAuthenticationScheme);
        await HttpContext.SignInAsync(
            IdentityServerConstants.DefaultCookieAuthenticationScheme,
            new ClaimsPrincipal(identity));

        var returnUrl = TempData["ReturnUrl"] as string;

        TempData.Remove("OtpToken");
        TempData.Remove("OtpEmail");

        if (!string.IsNullOrEmpty(returnUrl) && interaction.IsValidReturnUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        var emailText = TempData["OtpEmail"] as string;

        if (string.IsNullOrEmpty(emailText))
        {
            return RedirectToPage("/Account/Login");
        }

        // Defensive: Login stores the normalized form, but an older cookie may carry mixed case.
        var normalized = emailText.Trim().ToLowerInvariant();

        // Reading removes the value, so put it back for the page that follows.
        Email = normalized;
        TempData["OtpEmail"] = normalized;

        if (!EmailAddress.TryCreate(normalized, out var email))
        {
            return RedirectToPage("/Account/Login");
        }

        var result = await otpSender.TrySendOtpAsync(
            new OtpAddress(OtpChannel.Email, email),
            HttpContext.RequestAborted);

        switch (result)
        {
            case SendOtpResult.Sent sent:
                // The new token replaces the old one, so the previous code stops working.
                TempData["OtpToken"] = sent.Token.ToString();
                TempData["BlockedUntil"] = sent.SendingBlockedUntilUtc.ToString("O");
                TempData.Remove("ResendBlocked");
                return RedirectToPage("/Account/EnterOtp");

            case SendOtpResult.Blocked blocked:
                TempData["BlockedUntil"] = blocked.SendingBlockedUntilUtc.ToString("O");
                TempData["ResendBlocked"] =
                    $"Please wait {Math.Ceiling((blocked.SendingBlockedUntilUtc - DateTimeOffset.UtcNow).TotalSeconds)} second(s) before requesting a new code.";
                return RedirectToPage("/Account/EnterOtp");

            case SendOtpResult.SaveFailed:
                TempData["ResendBlocked"] = "Failed to send the code. Please try again.";
                return RedirectToPage("/Account/EnterOtp");

            default:
                TempData["ResendBlocked"] = "Failed to send the code. Please try again.";
                return RedirectToPage("/Account/EnterOtp");
        }
    }

    private void PopulateFromTempData()
    {
        Email = TempData["OtpEmail"] as string;
        SendingBlockedUntilUtc = ReadBlockedUntil(TempData["BlockedUntil"]);
        ResendNotice = TempData["ResendBlocked"] as string;

        TempData.Keep("OtpToken");
        TempData.Keep("OtpEmail");
        TempData.Keep("BlockedUntil");
    }

    // DefaultTempDataSerializer type-sniffs on deserialize: a GUID-shaped string comes back as
    // Guid and a date-shaped string as DateTime, so the stored value is not always a string.
    private static DateTimeOffset? ReadBlockedUntil(object? value) => value switch
    {
        null => null,
        DateTimeOffset offset => offset,
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc), TimeSpan.Zero),
        string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
        _ => null,
    };
}
