namespace Zaiware.Platform.IdentityServer.Pages.Account;

/// <summary>
/// Success outcomes that can follow an account action. The value is carried to the account
/// page as a short enum name in the <c>status</c> query string, never as user-facing text.
/// </summary>
public enum AccountStatus
{
    ProfileUpdated,
    AvatarUpdated,
    PasswordChanged,
    PasswordSet,
    TwoFactorDisabled,
    TwoFactorAlreadyEnabled,
    RecoveryCodesSaved,
}

/// <summary>
/// Parses the <c>status</c> query value and maps an <see cref="AccountStatus"/> to the
/// user-facing success message. Only known enum names are accepted; a missing, empty, or
/// unknown value has no message.
/// </summary>
public static class AccountStatusExtensions
{
    /// <summary>
    /// Tries to parse a canonical enum name, ignoring case. Numeric aliases (including values
    /// that are defined in the enum) and combined values are rejected; the input must match
    /// <see cref="Enum.ToString()"/> for the parsed value.
    /// </summary>
    public static bool TryParse(string? value, out AccountStatus status)
    {
        status = default;

        return !string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<AccountStatus>(value, ignoreCase: true, out status)
            && Enum.IsDefined(status)
            && string.Equals(status.ToString(), value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps a known status to the message shown in the success banner.
    /// </summary>
    public static bool TryGetMessage(AccountStatus status, out string message)
    {
        message = status switch
        {
            AccountStatus.ProfileUpdated => "Profile updated.",
            AccountStatus.AvatarUpdated => "Avatar updated.",
            AccountStatus.PasswordChanged => "Password changed.",
            AccountStatus.PasswordSet => "Password set.",
            AccountStatus.TwoFactorDisabled => "Two-factor authentication disabled.",
            AccountStatus.TwoFactorAlreadyEnabled => "Two-factor authentication is already enabled.",
            AccountStatus.RecoveryCodesSaved => "Recovery codes saved.",
            _ => string.Empty,
        };

        return message.Length > 0;
    }
}
