using System.ComponentModel.DataAnnotations;
using System.Text;
using Duende.IdentityModel;
using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.RecoveryCodes;
using Duende.UserManagement.Authentication.Totp;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

/// <summary>
/// Starts and confirms TOTP enrollment. The pending key lives in TempData until the user
/// proves their authenticator app produces a valid code; only then is a device added.
/// </summary>
[Authorize]
public sealed class TotpSetupModel(
    IUserAuthenticatorsSelfService authenticators,
    IUserProfileSelfService profiles) : PageModel
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");

    [BindProperty]
    [Required(ErrorMessage = "Enter the 6-digit code from your authenticator app.")]
    [StringLength(9, MinimumLength = 6, ErrorMessage = "Enter the 6-digit code.")]
    public string Code { get; set; } = string.Empty;

    public string ManualKey { get; private set; } = string.Empty;

    public string QrCodeDataUri { get; private set; } = string.Empty;

    public List<string> ErrorMessages { get; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var existing = await authenticators.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (existing?.TotpDeviceNames.Count > 0)
        {
            return RedirectToPage("/Account/Account", new
            {
                status = AccountStatus.TwoFactorAlreadyEnabled.ToString(),
            });
        }

        var key = PlainBytesTotpKey.New();
        TempData["TotpKey"] = key.EncodeToBase32();
        await LoadAsync(subjectId, key);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var keyBase32 = TempData["TotpKey"]?.ToString();

        if (string.IsNullOrEmpty(keyBase32)
            || !PlainBytesTotpKey.TryDecodeFromBase32(keyBase32, out var key)
            || key is null)
        {
            // Do not redirect away from the error: the user needs to see why the page is
            // asking them to start over. Issue a fresh key and render it with the notice.
            var replacement = PlainBytesTotpKey.New();
            keyBase32 = replacement.EncodeToBase32();
            TempData["TotpKey"] = keyBase32;
            ErrorMessages.Add("Your setup session expired. A new key is shown; scan it and confirm again.");
            await LoadAsync(subjectId, replacement);
            return Page();
        }

        var normalizedCode = TotpCode.Normalize(Code);

        if (!PlainTextTotp.TryCreate(normalizedCode, out var totp))
        {
            ErrorMessages.Add("Enter the 6-digit code from your authenticator app.");
            TempData["TotpKey"] = keyBase32;
            await LoadAsync(subjectId, key.Value);
            return Page();
        }

        if (!await authenticators.TryAddTotpDeviceAsync(
                subjectId,
                TotpDeviceName.Default,
                key.Value,
                totp,
                HttpContext.RequestAborted))
        {
            ErrorMessages.Add("That code isn't valid. Check your authenticator app and try again.");
            TempData["TotpKey"] = keyBase32;
            await LoadAsync(subjectId, key.Value);
            return Page();
        }

        var recoveryCodes = await authenticators.TryCreateRecoveryCodesAsync(
            subjectId,
            HttpContext.RequestAborted);

        if (recoveryCodes is not { Count: > 0 })
        {
            // Keep enrollment atomic: no codes means the user would be locked out of recovery.
            _ = await authenticators.TryRemoveTotpDeviceAsync(
                subjectId,
                TotpDeviceName.Default,
                HttpContext.RequestAborted);

            ErrorMessages.Add("Recovery codes could not be generated, so two-factor authentication was not enabled. Please try again.");
            TempData["TotpKey"] = keyBase32;
            await LoadAsync(subjectId, key.Value);
            return Page();
        }

        TempData.Remove("TotpKey");
        TempData["RecoveryCodes"] = recoveryCodes
            .Select(code => string.Join("-", code.ToTextGroups()))
            .ToArray();

        return RedirectToPage("/Account/RecoveryCodes");
    }

    private async Task LoadAsync(UserSubjectId subjectId, PlainBytesTotpKey key)
    {
        ManualKey = FormatKey(key.EncodeToBase32());

        var profile = await profiles.TryGetAsync(subjectId, HttpContext.RequestAborted);
        var account = (profile is null ? null : TryGetString(profile, EmailCode))
            ?? (profile is null ? null : TryGetString(profile, ProfileSchema.PreferredUsername.Code))
            ?? "user";

        var uri = TotpAuthenticatorUri.Generate("Nimbus", account, key);
        using var data = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        QrCodeDataUri = $"data:image/png;base64,{Convert.ToBase64String(png.GetGraphic(6))}";
    }

    private static string FormatKey(string base32)
    {
        var builder = new StringBuilder();
        var position = 0;

        while (position + 4 < base32.Length)
        {
            builder.Append(base32.AsSpan(position, 4)).Append(' ');
            position += 4;
        }

        if (position < base32.Length)
        {
            builder.Append(base32.AsSpan(position));
        }

        return builder.ToString().ToLowerInvariant();
    }

    private UserSubjectId? GetSubjectId() =>
        UserSubjectId.TryCreate(User.FindFirst(JwtClaimTypes.Subject)?.Value, out var subjectId)
            ? subjectId
            : null;

    private static string? TryGetString(UserProfile profile, AttributeCode code) =>
        profile.Attributes.TryGetValue(code, out var attribute)
        && attribute.TryGetValue<string>(out var text)
        && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
