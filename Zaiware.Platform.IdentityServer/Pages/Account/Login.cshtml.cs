using System.ComponentModel.DataAnnotations;
using Duende.IdentityModel;
using Duende.IdentityServer;
using Duende.IdentityServer.DataExtensions;
using Duende.IdentityServer.Services;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Authentication.Passwords;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

public sealed class LoginModel(
    IPasswordAuthenticator passwordAuthenticator,
    IUserAuthenticatorsSelfService authenticatorsSelfService,
    IOtpSender otpSender,
    IUserProfileAdmin profiles,
    SecondFactorStateCookie secondFactorStateCookie,
    UserSessionSignIn userSession,
    IIdentityServerInteractionService interaction) : PageModel
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool SessionExpired { get; private set; }

    public sealed class InputModel
    {
        public string Identifier { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public IActionResult OnGet(string? email = null, string? returnUrl = null, bool sessionExpired = false)
    {
        SessionExpired = sessionExpired;

        // A valid session skips the sign-in screen; honor the original destination when it is safe.
        if (User.Identity?.IsAuthenticated == true)
        {
            var safeReturnUrl = GetSafeReturnUrl(returnUrl);

            return safeReturnUrl is null
                ? RedirectToPage("/Index")
                : LocalRedirect(safeReturnUrl);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            Input.Email = email;
        }

        Input.ReturnUrl = GetSafeReturnUrl(returnUrl);

        return Page();
    }

    /// <summary>
    /// Handles the password form. The identifier is resolved as the unique username first
    /// and as the email address second; both paths produce the same generic failure so the
    /// page does not reveal whether an account exists.
    /// </summary>
    public async Task<IActionResult> OnPostPasswordAsync()
    {
        Input.Identifier = Input.Identifier.Trim();
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);

        var identifier = Input.Identifier.ToLowerInvariant();

        if (identifier.Length == 0)
        {
            ModelState.AddModelError("Input.Identifier", "Enter your username or email.");
        }

        if (string.IsNullOrEmpty(Input.Password))
        {
            ModelState.AddModelError("Input.Password", "Enter your password.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var password = NonValidatedPassword.Create(Input.Password);
        UserSubjectId? subjectId = null;

        if (await passwordAuthenticator.TryAuthenticateAsync(
                ProfileSchema.PreferredUsername,
                identifier,
                password,
                HttpContext.RequestAborted) is PasswordAuthenticationResult.Success usernameSuccess)
        {
            subjectId = usernameSuccess.UserSubjectId;
        }
        else if (await passwordAuthenticator.TryAuthenticateAsync(
                     EmailCode,
                     identifier,
                     password,
                     HttpContext.RequestAborted) is PasswordAuthenticationResult.Success emailSuccess)
        {
            subjectId = emailSuccess.UserSubjectId;
        }

        if (subjectId is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return Page();
        }

        var authenticators = await authenticatorsSelfService.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (authenticators?.TotpDeviceNames.Count > 0)
        {
            secondFactorStateCookie.Write(subjectId);

            return RedirectToPage("/Account/TwoFactor", new { returnUrl = Input.ReturnUrl });
        }

        await userSession.SignInAsync(
            HttpContext,
            subjectId,
            OidcConstants.AuthenticationMethods.Password,
            HttpContext.RequestAborted);

        return Input.ReturnUrl is null
            ? RedirectToPage("/Index")
            : LocalRedirect(Input.ReturnUrl);
    }

    /// <summary>
    /// Sends a one-time code to the address entered in the passwordless form. The existing
    /// invite-only posture is preserved: unknown addresses are declined before a code is sent,
    /// and the authenticator remains the enforcement point at verification time.
    /// </summary>
    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);

        if (!EmailAddress.TryCreate(Input.Email.Trim().ToLowerInvariant(), out var email))
        {
            ModelState.AddModelError(
                "Input.Email",
                "That address looks off — check for a typo, then try again.");
            return Page();
        }

        var normalized = email.Value;

        var known = await profiles.TryGetAsync(EmailCode, normalized, HttpContext.RequestAborted);

        if (known is null)
        {
            const string message = "No account found for this address. Sign-in is by invitation — ask an administrator for access.";
            ModelState.AddModelError("Input.Email", message);
            ModelState.AddModelError(string.Empty, message);
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
                TempData["BlockedUntil"] = sent.SendingBlockedUntilUtc.ToString("O");
                TempData["ReturnUrl"] = Input.ReturnUrl;
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

    private string? GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && (interaction.IsValidReturnUrl(returnUrl) || Url.IsLocalUrl(returnUrl))
            ? returnUrl
            : null;
}
