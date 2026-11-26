using System.ComponentModel.DataAnnotations;
using Duende.IdentityModel;
using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Passwords;
using Duende.UserManagement.Authentication.Totp;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zaiware.Platform.IdentityServer.Pages.Account;

/// <summary>
/// The signed-in account area. The profile section manages the unique username, display
/// name, read-only email, and avatar; the security section (added below) manages passwords,
/// TOTP, and recovery codes.
/// </summary>
[Authorize]
[RequestSizeLimit(AvatarStorage.MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = AvatarStorage.MaxRequestBytes)]
public sealed class AccountModel(
    IUserProfileSelfService profileSelfService,
    IUserProfileAdmin profiles,
    IUserAuthenticatorsSelfService authenticatorsSelfService,
    IAvatarStorage avatarStorage,
    IUserSessionSignIn userSession,
    IFlashMessage flashMessage,
    ILogger<AccountModel> logger) : PageModel
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");

    [BindProperty]
    public ProfileInput Profile { get; set; } = new();

    [BindProperty]
    public IFormFile? Avatar { get; set; }

    [BindProperty]
    public SecurityInput Security { get; set; } = new();

    public string Email { get; private set; } = string.Empty;

    public string PictureSrc { get; private set; } = string.Empty;

    public bool HasPassword { get; private set; }

    public bool TotpEnabled { get; private set; }

    public int RecoveryCodeCount { get; private set; }

    public string? SuccessMessage { get; private set; }

    /// <summary>Whether the last POST was the password form; drives its aria-invalid state.</summary>
    public bool PasswordError { get; private set; }

    /// <summary>Whether the last POST was the avatar form; drives its aria-invalid state.</summary>
    public bool AvatarError { get; private set; }

    /// <summary>The avatar-specific error rendered next to the file input.</summary>
    public string? AvatarErrorMessage { get; private set; }

    public List<string> ErrorMessages { get; } = [];

    public sealed class SecurityInput
    {
        public string CurrentPassword { get; set; } = string.Empty;

        public string NewPassword { get; set; } = string.Empty;

        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class ProfileInput
    {
        [Required(ErrorMessage = "Enter a username.")]
        [StringLength(32, MinimumLength = 3, ErrorMessage = "Use between 3 and 32 characters.")]
        [RegularExpression(
            "^[A-Za-z0-9][A-Za-z0-9._-]*$",
            ErrorMessage = "Use letters, numbers, dots, underscores, or hyphens; start with a letter or number.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a display name.")]
        [StringLength(100, ErrorMessage = "Use 100 characters or fewer.")]
        public string DisplayName { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var profile = await profileSelfService.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (profile is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Load(profile);
        SuccessMessage = flashMessage.ConsumeSuccess();
        await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);

        return Page();
    }

    /// <summary>
    /// Saves the unique username and display name, then re-issues the sign-in cookie so
    /// the updated claims are reflected immediately. Unrelated profile attributes are kept.
    /// </summary>
    public async Task<IActionResult> OnPostProfileAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var profile = await profileSelfService.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (profile is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Load(profile, loadEditableFields: false);

        if (!ModelState.IsValid)
        {
            await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);
            return Page();
        }

        var username = Profile.Username.Trim().ToLowerInvariant();
        var displayName = Profile.DisplayName.Trim();

        var owner = await profiles.TryGetAsync(
            ProfileSchema.PreferredUsername.Code,
            username,
            HttpContext.RequestAborted);

        if (owner is not null && owner.SubjectId != subjectId)
        {
            ModelState.AddModelError(
                "Profile.Username",
                "That username is already taken. Try another one.");
            await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);
            return Page();
        }

        var schema = await profileSelfService.GetSchemaAsync(HttpContext.RequestAborted);
        var attributes = new AttributeValueCollection(schema, profile.Attributes.Values);
        attributes.Set(ProfileSchema.PreferredUsername, username);
        attributes.Set(OidcStandardAttributes.Name.Code, displayName);

        if (!attributes.TryValidate(out var validated, out var errors))
        {
            ErrorMessages.Add($"Could not save your profile: {string.Join(" ", errors)}");
            await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);
            return Page();
        }

        if (await profileSelfService.TryUpdateAsync(subjectId, validated, HttpContext.RequestAborted) is null)
        {
            ErrorMessages.Add("Could not save your profile. Please try again.");
            await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);
            return Page();
        }

        await userSession.SignInAsync(HttpContext, subjectId, CurrentAuthenticationMethod(), HttpContext.RequestAborted);

        flashMessage.SetSuccess("Profile updated.");
        return RedirectToPage();
    }

    /// <summary>
    /// Validates and stores an avatar, then points the profile's picture attribute at the
    /// stable avatar route. The object itself stays private in the avatars bucket.
    /// </summary>
    public async Task<IActionResult> OnPostAvatarAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var profile = await profileSelfService.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (profile is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Load(profile);
        await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);

        if (Avatar is null || Avatar.Length == 0)
        {
            AddAvatarError("Choose an image to upload.");
            return Page();
        }

        if (Avatar.Length > AvatarStorage.MaxBytes)
        {
            AddAvatarError("Avatars must be 2 MB or smaller.");
            return Page();
        }

        if (!AvatarStorage.IsAllowedContentType(Avatar.ContentType))
        {
            AddAvatarError("Choose a PNG, JPEG, or WebP image.");
            return Page();
        }

        await using (var signatureStream = Avatar.OpenReadStream())
        {
            if (!await AvatarSignature.HasExpectedSignatureAsync(signatureStream, HttpContext.RequestAborted))
            {
                AddAvatarError("That file does not look like a PNG, JPEG, or WebP image.");
                return Page();
            }
        }

        try
        {
            await using var stream = Avatar.OpenReadStream();
            await avatarStorage.UploadAsync(
                subjectId.ToString(),
                stream,
                Avatar.ContentType,
                HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Avatar upload failed for subject {SubjectId}", subjectId);
            AddAvatarError("The avatar could not be stored. Please try again.");
            return Page();
        }

        var pictureRoute = AvatarRoutes.ForSubject(subjectId);
        var schema = await profileSelfService.GetSchemaAsync(HttpContext.RequestAborted);
        var attributes = new AttributeValueCollection(schema, profile.Attributes.Values);
        attributes.Set(ProfileSchema.Picture, pictureRoute);

        if (!attributes.TryValidate(out var validated, out var errors)
            || await profileSelfService.TryUpdateAsync(subjectId, validated, HttpContext.RequestAborted) is null)
        {
            AddAvatarError($"The avatar was stored but the profile could not be updated: {string.Join(" ", errors ?? [])}");
            return Page();
        }

        flashMessage.SetSuccess("Avatar updated.");
        return RedirectToPage();
    }

    /// <summary>
    /// Sets a first password or changes an existing one. Policy violations are surfaced from
    /// Duende's password validator; an existing password requires the current value.
    /// </summary>
    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var profile = await profileSelfService.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (profile is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Load(profile);
        PasswordError = true;
        await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);

        if (string.IsNullOrEmpty(Security.NewPassword))
        {
            ErrorMessages.Add("Enter a new password.");
            return Page();
        }

        if (!string.Equals(Security.NewPassword, Security.ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessages.Add("The new password and confirmation do not match.");
            return Page();
        }

        var validation = await authenticatorsSelfService.TryValidatePasswordAsync(
            subjectId,
            Security.NewPassword,
            HttpContext.RequestAborted);

        if (validation is PasswordCreationResult.Failed failed)
        {
            ErrorMessages.AddRange(failed.Errors);
            return Page();
        }

        if (validation is not PasswordCreationResult.Success success)
        {
            ErrorMessages.Add("The password could not be validated. Please try again.");
            return Page();
        }

        if (HasPassword)
        {
            if (string.IsNullOrEmpty(Security.CurrentPassword))
            {
                ErrorMessages.Add("Enter your current password.");
                return Page();
            }

            var changed = await authenticatorsSelfService.TryChangePasswordAsync(
                subjectId,
                NonValidatedPassword.Create(Security.CurrentPassword),
                success.Password,
                HttpContext.RequestAborted);

            if (!changed)
            {
                ErrorMessages.Add("Your current password is incorrect.");
                return Page();
            }

            flashMessage.SetSuccess("Password changed.");
        }
        else
        {
            if (!await authenticatorsSelfService.TrySetPasswordAsync(
                    subjectId,
                    success.Password,
                    HttpContext.RequestAborted))
            {
                ErrorMessages.Add("The password could not be set. Please try again.");
                return Page();
            }

            flashMessage.SetSuccess("Password set.");
        }

        return RedirectToPage();
    }

    /// <summary>Removes every enrolled TOTP device, disabling the second factor.</summary>
    public async Task<IActionResult> OnPostDisableTotpAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var authenticators = await authenticatorsSelfService.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (authenticators is null)
        {
            return RedirectToPage();
        }

        foreach (var device in authenticators.TotpDeviceNames.ToArray())
        {
            if (!await authenticatorsSelfService.TryRemoveTotpDeviceAsync(
                    subjectId,
                    device,
                    HttpContext.RequestAborted))
            {
                ErrorMessages.Add("Two-factor authentication could not be disabled. Please try again.");
                await ReloadAsync(subjectId);
                return Page();
            }
        }

        flashMessage.SetSuccess("Two-factor authentication disabled.");
        return RedirectToPage();
    }

    /// <summary>
    /// Issues a fresh set of recovery codes and shows them once. Every previously issued
    /// code stops working.
    /// </summary>
    public async Task<IActionResult> OnPostRegenerateRecoveryCodesAsync()
    {
        var subjectId = GetSubjectId();

        if (subjectId is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var codes = await authenticatorsSelfService.TryCreateRecoveryCodesAsync(
            subjectId,
            HttpContext.RequestAborted);

        if (codes is not { Count: > 0 })
        {
            ErrorMessages.Add("Enable two-factor authentication before generating recovery codes.");
            await ReloadAsync(subjectId);
            return Page();
        }

        TempData["RecoveryCodes"] = codes
            .Select(code => string.Join("-", code.ToTextGroups()))
            .ToArray();

        return RedirectToPage("/Account/RecoveryCodes");
    }

    private void AddAvatarError(string message)
    {
        AvatarError = true;
        AvatarErrorMessage = message;
        ErrorMessages.Add(message);
    }

    private async Task ReloadAsync(UserSubjectId subjectId)
    {
        var profile = await profileSelfService.TryGetAsync(subjectId, HttpContext.RequestAborted);

        if (profile is not null)
        {
            Load(profile);
            await LoadAuthenticatorStateAsync(subjectId, HttpContext.RequestAborted);
        }
    }

    private void Load(UserProfile profile, bool loadEditableFields = true)
    {
        Email = TryGetString(profile, EmailCode) ?? string.Empty;
        PictureSrc = $"{AvatarRoutes.ForSubject(profile.SubjectId)}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        if (loadEditableFields)
        {
            Profile.Username = TryGetString(profile, ProfileSchema.PreferredUsername.Code) ?? string.Empty;
            Profile.DisplayName = TryGetString(profile, OidcStandardAttributes.Name.Code) ?? string.Empty;
        }
    }

    private async Task LoadAuthenticatorStateAsync(UserSubjectId subjectId, CancellationToken ct)
    {
        var authenticators = await authenticatorsSelfService.TryGetAsync(subjectId, ct);
        HasPassword = authenticators?.HasPassword == true;
        TotpEnabled = authenticators?.TotpDeviceNames.Count > 0;
        RecoveryCodeCount = authenticators?.RecoveryCodeCount ?? 0;
    }

    private UserSubjectId? GetSubjectId() =>
        UserSubjectId.TryCreate(User.FindFirst(JwtClaimTypes.Subject)?.Value, out var subjectId)
            ? subjectId
            : null;

    private string CurrentAuthenticationMethod() =>
        User.FindFirst(JwtClaimTypes.AuthenticationMethod)?.Value
        ?? OidcConstants.AuthenticationMethods.Password;

    private static string? TryGetString(UserProfile profile, AttributeCode code) =>
        profile.Attributes.TryGetValue(code, out var attribute)
        && attribute.TryGetValue<string>(out var text)
        && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
