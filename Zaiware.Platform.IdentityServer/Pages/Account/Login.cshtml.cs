using System.ComponentModel.DataAnnotations;
using Duende.IdentityModel;
using Duende.IdentityServer;
using Duende.IdentityServer.DataExtensions;
using Duende.IdentityServer.Services;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Authentication.Passwords;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

public sealed class LoginModel(
    IPasswordAuthenticator passwordAuthenticator,
    IUserAuthenticatorsSelfService authenticatorsSelfService,
    IOtpSender otpSender,
    ISecondFactorStateCookie secondFactorStateCookie,
    IUserSessionSignIn userSession,
    IIdentityServerInteractionService interaction,
    ILogger<LoginModel> logger) : PageModel
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool SessionExpired { get; private set; }

    public sealed class InputModel
    {
        // Nullable so an omitted/empty field is validated by the handlers' own messages
        // instead of the implicit non-nullable-required binder error.
        public string? Identifier { get; set; }

        [DataType(DataType.Password)]
        public string? Password { get; set; }

        public string? Email { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public IActionResult OnGet(string? email = null, string? returnUrl = null, bool sessionExpired = false)
    {
        SessionExpired = sessionExpired;

        // A valid session skips the sign-in screen; honor the original destination when it is safe.
        if (User.Identity?.IsAuthenticated == true)
        {
            return SafeReturnUrl.Redirect(this, GetSafeReturnUrl(returnUrl));
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
        // A missing form field binds as null, so normalize before reading it. Trimming a
        // null value used to throw and produce 500.
        var identifier = (Input.Identifier ?? string.Empty).Trim().ToLowerInvariant();
        var passwordText = Input.Password ?? string.Empty;
        Input.Identifier = identifier;
        Input.Password = passwordText;
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);

        if (identifier.Length == 0)
        {
            ModelState.AddModelError("Input.Identifier", "Enter your username or email.");
        }

        if (string.IsNullOrWhiteSpace(passwordText))
        {
            ModelState.AddModelError("Input.Password", "Enter your password.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var password = NonValidatedPassword.Create(passwordText);
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

        if (authenticators is null)
        {
            // Fail closed. A missing authenticator record is a backend failure, not proof
            // that the user has no second factor. Issuing a session here would silently
            // bypass MFA, so show a temporary error and keep the user signed out.
            logger.LogError(
                "Could not read authenticator state for subject {SubjectId}; refusing password sign-in rather than bypassing a possible second factor.",
                subjectId);

            ModelState.AddModelError(
                string.Empty,
                "We couldn't complete sign-in right now. Please try again.");
            return Page();
        }

        if (authenticators.TotpDeviceNames.Count > 0)
        {
            secondFactorStateCookie.Write(subjectId);

            return RedirectToPage("/Account/TwoFactor", new { returnUrl = Input.ReturnUrl });
        }

        await userSession.SignInAsync(
            HttpContext,
            subjectId,
            OidcConstants.AuthenticationMethods.Password,
            HttpContext.RequestAborted);

        return SafeReturnUrl.Redirect(this, Input.ReturnUrl);
    }

    /// <summary>
    /// Sends a one-time code to the address entered in the passwordless form. Every
    /// syntactically valid address receives the same response and advances to code entry:
    /// pre-checking whether the account exists would turn this page into an account
    /// enumeration oracle. Invite-only enforcement stays at verification time in
    /// <see cref="InviteOnlyOtpAuthenticator"/>, so unknown addresses can never establish a
    /// session and never create a user.
    /// </summary>
    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);

        var normalizedInput = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (!EmailAddress.TryCreate(normalizedInput, out var email))
        {
            ModelState.AddModelError(
                "Input.Email",
                "That address looks off — check for a typo, then try again.");
            return Page();
        }

        var normalized = email.Value;

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
        SafeReturnUrl.Normalize(returnUrl, interaction, Url);
}
