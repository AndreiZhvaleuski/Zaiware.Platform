using System.Globalization;
using Duende.IdentityServer.Services;
using Duende.UserManagement;
using Duende.UserManagement.Authentication.Otp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

public sealed class EnterOtpModel(
    IOtpAuthenticator otpAuthenticator,
    IOtpSender otpSender,
    IOtpSendRateLimiter otpSendRateLimiter,
    IUserSessionSignIn userSession,
    IIdentityServerInteractionService interaction,
    ILogger<EnterOtpModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Email { get; private set; }

    /// <summary>
    /// The original destination carried through code entry. It is read from TempData (and
    /// refreshed from the query string when present) so "Change email" and resends do not
    /// drop it before verification.
    /// </summary>
    public string? ReturnUrl { get; private set; }

    public DateTimeOffset? SendingBlockedUntilUtc { get; private set; }

    public string? ResendNotice { get; private set; }

    public sealed class InputModel
    {
        public string Token { get; set; } = string.Empty;

        public string[] CodeDigits { get; set; } = new string[8];

        public string Code => string.Concat(CodeDigits ?? []);
    }

    public string CodeDigit(int index) =>
        Input.CodeDigits is { Length: > 0 } digits && index < digits.Length
            ? digits[index]
            : string.Empty;

    public IActionResult OnGet(string? returnUrl = null)
    {
        // A GUID-shaped TempData string deserializes as System.Guid, so read it as text rather
        // than assuming the cookie hands back a string.
        var token = TempData["OtpToken"]?.ToString();

        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("EnterOtp requested without an active OTP session (browser cookie missing, blocked, or expired); redirecting to login.");
            return RedirectToPage("/Account/Login", new
            {
                email = TempData["OtpEmail"]?.ToString(),
                returnUrl = ResolveReturnUrl(returnUrl),
                sessionExpired = true,
            });
        }

        Input.Token = token;
        ResolveReturnUrl(returnUrl);
        PopulateFromTempData();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Input.Token))
        {
            logger.LogWarning("EnterOtp requested without an active OTP session (browser cookie missing, blocked, or expired); redirecting to login.");
            return RedirectToPage("/Account/Login", new
            {
                email = TempData["OtpEmail"]?.ToString(),
                returnUrl = ResolveReturnUrl(null),
                sessionExpired = true,
            });
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

        await userSession.SignInAsync(
            HttpContext,
            success.UserSubjectId,
            "otp",
            HttpContext.RequestAborted);

        var returnUrl = ReturnUrl
            ?? SafeReturnUrl.Normalize(TempData["ReturnUrl"]?.ToString(), interaction, Url);

        TempData.Remove("OtpToken");
        TempData.Remove("OtpEmail");
        TempData.Remove("ReturnUrl");

        return SafeReturnUrl.Redirect(this, returnUrl);
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        var emailText = TempData["OtpEmail"] as string;
        var token = TempData["OtpToken"]?.ToString();

        if (string.IsNullOrEmpty(emailText) || string.IsNullOrEmpty(token))
        {
            return RedirectToPage("/Account/Login", new
            {
                email = emailText,
                returnUrl = ResolveReturnUrl(null),
                sessionExpired = true,
            });
        }

        // Defensive: Login stores the normalized form, but an older cookie may carry mixed case.
        var normalized = emailText.Trim().ToLowerInvariant();
        var returnUrl = ResolveReturnUrl(null);

        // Reading removes the value, so put both the address, token, and destination back for
        // the page that follows. Without re-adding the token, a blocked resend would end the
        // OTP session.
        Email = normalized;
        Input.Token = token;
        TempData["OtpEmail"] = normalized;
        TempData["OtpToken"] = token;
        if (returnUrl is not null)
        {
            TempData["ReturnUrl"] = returnUrl;
        }

        SendingBlockedUntilUtc = ReadBlockedUntil(TempData["BlockedUntil"]);
        TempData.Keep("BlockedUntil");

        if (!EmailAddress.TryCreate(normalized, out var email))
        {
            return RedirectToPage("/Account/Login", new { returnUrl });
        }

        var rateLimit = otpSendRateLimiter.TryAcquire(HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!rateLimit.Allowed)
        {
            logger.LogWarning(
                "OTP resend rate limit reached for client {ClientAddress}; retry after {RetryAfter}.",
                HttpContext.Connection.RemoteIpAddress,
                rateLimit.RetryAfter);

            ResendNotice = IOtpSendRateLimiter.TooManyRequestsMessage;
            return Page();
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

    private string? ResolveReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            returnUrl = TempData["ReturnUrl"]?.ToString();
        }

        ReturnUrl = SafeReturnUrl.Normalize(returnUrl, interaction, Url);

        if (ReturnUrl is not null)
        {
            TempData["ReturnUrl"] = ReturnUrl;
            TempData.Keep("ReturnUrl");
        }

        return ReturnUrl;
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
