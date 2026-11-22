using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Profiles;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Creates the one invited user the Development environment signs in with. With invite-only
/// authentication there is no self-registration, so a demo user has to exist before the first
/// login. This is a Development-only bootstrap; a real invitation flow is a future change.
/// </summary>
public static class DevelopmentUserSeed
{
    /// <summary>
    /// Ensures a user profile and its email OTP authenticator exist for the specified email.
    /// The method is idempotent: an existing profile is left in place and only a missing
    /// authenticator record or OTP address is repaired.
    /// </summary>
    /// <param name="services">The service provider from a request-like scope.</param>
    /// <param name="email">The invited email address; it is trimmed and lowercased before use.</param>
    /// <param name="displayName">The value stored in the profile's <c>name</c> attribute.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the user is present.</returns>
    /// <exception cref="InvalidOperationException">
    /// The profile attributes are invalid, or the profile/authenticator record could not be created.
    /// </exception>
    public static async Task EnsureInvitedUserAsync(
        IServiceProvider services,
        string email,
        string displayName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(displayName);

        // EmailAddress equality is case-sensitive and the default authenticator resolves users by
        // OtpAddress equality, so the seed, the login page, and the invite check all normalize the
        // same way: trim + lowercase.
        var normalized = email.Trim().ToLowerInvariant();
        var address = new OtpAddress(OtpChannel.Email, EmailAddress.Create(normalized));
        var emailCode = AttributeCode.Create("email");

        var profiles = services.GetRequiredService<IUserProfileAdmin>();
        var authenticators = services.GetRequiredService<IUserAuthenticatorsAdmin>();
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Zaiware.Platform.IdentityServer.DevelopmentUserSeed");

        var existing = await profiles.TryGetAsync(emailCode, normalized, ct);

        if (existing is not null)
        {
            await EnsureAuthenticatorsAsync(authenticators, existing.SubjectId, address, normalized, ct);

            logger.LogInformation("Invited user {Email} already present", normalized);
            return;
        }

        var schema = await profiles.GetSchemaAsync(ct);
        var attributes = new AttributeValueCollection(schema);
        attributes.Set(emailCode, normalized);
        attributes.Set(AttributeCode.Create("name"), displayName.Trim());

        if (!attributes.TryValidate(out var validated, out var errors))
        {
            throw new InvalidOperationException(
                $"Development seed user attributes are invalid: {string.Join("; ", errors)}");
        }

        var profile = await profiles.TryAddAsync(UserSubjectId.New(), validated, ct);

        if (profile is null)
        {
            throw new InvalidOperationException(
                $"Failed to seed the Development user '{normalized}': the profile could not be created.");
        }

        var authenticatorsRecord = await authenticators.TryAddAsync(profile.SubjectId, [address], [], ct);

        if (authenticatorsRecord is null)
        {
            throw new InvalidOperationException(
                $"Failed to seed the Development user '{normalized}': the OTP authenticator record could not be created.");
        }

        logger.LogInformation("Seeded invited user {Email}", normalized);
    }

    private static async Task EnsureAuthenticatorsAsync(
        IUserAuthenticatorsAdmin authenticators,
        UserSubjectId subjectId,
        OtpAddress address,
        string normalizedEmail,
        CancellationToken ct)
    {
        var existing = await authenticators.TryGetAsync(subjectId, ct);

        if (existing is null)
        {
            var created = await authenticators.TryAddAsync(subjectId, [address], [], ct);

            if (created is null)
            {
                throw new InvalidOperationException(
                    $"Failed to repair the Development user '{normalizedEmail}': the OTP authenticator record could not be created.");
            }

            return;
        }

        var hasAddress = existing.OtpAddresses.Any(candidate =>
            candidate.Channel.Equals(OtpChannel.Email)
            && string.Equals(
                candidate.SubjectId.ToString()?.Trim(),
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase));

        if (hasAddress)
        {
            return;
        }

        if (!await authenticators.TryAddOtpAddressesAsync(subjectId, [address], ct))
        {
            throw new InvalidOperationException(
                $"Failed to repair the Development user '{normalizedEmail}': the OTP address could not be added.");
        }
    }
}
