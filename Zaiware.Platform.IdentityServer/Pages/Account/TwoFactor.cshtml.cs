using Duende.IdentityServer.Services;
using Duende.UserManagement.Authentication.RecoveryCodes;
using Duende.UserManagement.Authentication.Totp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

public sealed class TwoFactorModel(
    ITotpAuthenticator totpAuthenticator,
    IRecoveryCodeAuthenticator recoveryCodeAuthenticator,
    SecondFactorStateCookie secondFactorStateCookie,
    UserSessionSignIn userSession,
    IIdentityServerInteractionService interaction) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        public string[] TotpDigits { get; set; } = new string[6];

        public string RecoveryCode { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }

        public string TotpCode => string.Concat(TotpDigits ?? []);
    }

    public string CodeDigit(int index) =>
        Input.TotpDigits is { Length: > 0 } digits && index < digits.Length
            ? digits[index]
            : string.Empty;

    public IActionResult OnGet(string? returnUrl = null)
    {
        var safeReturnUrl = GetSafeReturnUrl(returnUrl);

        if (User.Identity?.IsAuthenticated == true)
        {
            return safeReturnUrl is null ? RedirectToPage("/Index") : LocalRedirect(safeReturnUrl);
        }

        if (!secondFactorStateCookie.TryRead(out _))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = safeReturnUrl, sessionExpired = true });
        }

        Input.ReturnUrl = safeReturnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostTotpAsync()
    {
        var safeReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);

        if (!secondFactorStateCookie.TryRead(out var subjectId))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = safeReturnUrl, sessionExpired = true });
        }

        var code = Input.TotpCode;

        if (code.Length != 6 || !PlainTextTotp.TryCreate(code, out var totp))
        {
            return FailWithGenericError();
        }

        if (!await totpAuthenticator.TryAuthenticateAsync(
                subjectId,
                TotpDeviceName.Default,
                totp,
                HttpContext.RequestAborted))
        {
            return FailWithGenericError();
        }

        await CompleteSignInAsync(subjectId);

        return safeReturnUrl is null ? RedirectToPage("/Index") : LocalRedirect(safeReturnUrl);
    }

    public async Task<IActionResult> OnPostRecoveryAsync()
    {
        var safeReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);

        if (!secondFactorStateCookie.TryRead(out var subjectId))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = safeReturnUrl, sessionExpired = true });
        }

        var cleanCode = Input.RecoveryCode.Replace(" ", string.Empty).Replace("-", string.Empty);

        if (!PlainTextRecoveryCode.TryCreate(cleanCode, out var recoveryCode)
            || !await recoveryCodeAuthenticator.TryAuthenticateAsync(
                subjectId,
                recoveryCode,
                HttpContext.RequestAborted))
        {
            return FailWithGenericError();
        }

        await CompleteSignInAsync(subjectId);

        return safeReturnUrl is null ? RedirectToPage("/Index") : LocalRedirect(safeReturnUrl);
    }

    private IActionResult FailWithGenericError()
    {
        ErrorMessage = "That code isn't valid. Check the code and try again.";
        ModelState.AddModelError(string.Empty, ErrorMessage);
        return Page();
    }

    private async Task CompleteSignInAsync(Duende.UserManagement.UserSubjectId subjectId)
    {
        secondFactorStateCookie.Clear();

        await userSession.SignInAsync(
            HttpContext,
            subjectId,
            "mfa",
            HttpContext.RequestAborted);
    }

    private string? GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && (interaction.IsValidReturnUrl(returnUrl) || Url.IsLocalUrl(returnUrl))
            ? returnUrl
            : null;
}
