using Zaiware.Platform.IdentityServer.Pages.Account;

namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class AccountStatusTests
{
    [Theory]
    [InlineData(AccountStatus.ProfileUpdated, "Profile updated.")]
    [InlineData(AccountStatus.AvatarUpdated, "Avatar updated.")]
    [InlineData(AccountStatus.PasswordChanged, "Password changed.")]
    [InlineData(AccountStatus.PasswordSet, "Password set.")]
    [InlineData(AccountStatus.TwoFactorDisabled, "Two-factor authentication disabled.")]
    [InlineData(AccountStatus.TwoFactorAlreadyEnabled, "Two-factor authentication is already enabled.")]
    [InlineData(AccountStatus.RecoveryCodesSaved, "Recovery codes saved.")]
    public void Every_status_maps_to_its_message(AccountStatus status, string expected)
    {
        Assert.True(AccountStatusExtensions.TryGetMessage(status, out var message));
        Assert.Equal(expected, message);
    }

    [Fact]
    public void Every_defined_status_has_a_message()
    {
        foreach (var status in Enum.GetValues<AccountStatus>())
        {
            Assert.True(AccountStatusExtensions.TryGetMessage(status, out _), status.ToString());
        }
    }

    [Theory]
    [InlineData("ProfileUpdated")]
    [InlineData("profileupdated")]
    [InlineData("PROFILEUPDATED")]
    [InlineData("tWoFaCtOrAlReAdYeNaBlEd")]
    public void Status_names_parse_case_insensitively(string value)
    {
        Assert.True(AccountStatusExtensions.TryParse(value, out var status));
        Assert.True(AccountStatusExtensions.TryGetMessage(status, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotAStatus")]
    [InlineData("Profile Updated")]
    public void Missing_empty_and_unknown_values_are_rejected(string? value)
    {
        Assert.False(AccountStatusExtensions.TryParse(value, out var status));
        Assert.Equal(default, status);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("999")]
    public void Numeric_values_outside_the_enum_are_rejected_by_the_defined_guard(string value)
    {
        Assert.False(AccountStatusExtensions.TryParse(value, out _));
    }

    [Fact]
    public void An_undefined_status_has_no_message()
    {
        Assert.False(AccountStatusExtensions.TryGetMessage((AccountStatus)42, out var message));
        Assert.Equal(string.Empty, message);
    }
}
