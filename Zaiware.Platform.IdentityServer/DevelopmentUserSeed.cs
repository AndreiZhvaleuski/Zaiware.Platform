using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Authentication.Passwords;
using Duende.UserManagement.Authentication.Totp;
using Duende.UserManagement.Profiles;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// A Development identity to seed. The values are supplied by configuration so the
/// Playwright suite can sign in deterministically; they are never used outside Development.
/// </summary>
public sealed class DevelopmentSeedUser
{
    /// <summary>The invariant email address and OTP delivery address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>The display name stored in the <c>name</c> attribute.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The unique username used for password sign-in.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The Development password. It must satisfy the configured password policy.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Optional Base32 TOTP secret. When present, a default TOTP device is enrolled so the
    /// sign-in challenge is deterministic for end-to-end tests.
    /// </summary>
    public string? TotpSecret { get; init; }
}

/// <summary>
/// Creates the Development identities the application and the Playwright suite sign in with.
/// With invite-only authentication there is no self-registration, so the users have to exist
/// before the first login. This is a Development-only bootstrap; a real invitation flow is a
/// future change. Every operation is idempotent and repairs partially seeded records.
/// </summary>
public static class DevelopmentUserSeed
{
    private static readonly AttributeCode EmailCode = AttributeCode.Create("email");
    private static readonly AttributeCode NameCode = AttributeCode.Create("name");

    /// <summary>
    /// Ensures every configured user has a profile, an email OTP address, a password, and
    /// (when configured) a default TOTP device. Running the method repeatedly creates no
    /// duplicates and leaves unrelated profile attributes untouched.
    /// </summary>
    /// <param name="services">The service provider from a request-like scope.</param>
    /// <param name="users">The configured Development users.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when every user is present.</returns>
    /// <exception cref="InvalidOperationException">
    /// A configured value is missing or invalid, a username is already taken by another user,
    /// or a profile/authenticator record could not be written.
    /// </exception>
    public static async Task EnsureUsersAsync(
        IServiceProvider services,
        IReadOnlyList<DevelopmentSeedUser> users,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(users);

        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Zaiware.Platform.IdentityServer.DevelopmentUserSeed");

        foreach (var user in users)
        {
            await EnsureUserAsync(services, user, logger, ct);
        }
    }

    private static async Task EnsureUserAsync(
        IServiceProvider services,
        DevelopmentSeedUser configured,
        ILogger logger,
        CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(configured.Email);
        var username = NormalizeUsername(configured.Username);

        if (string.IsNullOrWhiteSpace(configured.Password))
        {
            throw new InvalidOperationException(
                $"Development seed user '{normalizedEmail}' has no password configured.");
        }

        var profiles = services.GetRequiredService<IUserProfileAdmin>();
        var profileSelfService = services.GetRequiredService<IUserProfileSelfService>();
        var authenticatorsAdmin = services.GetRequiredService<IUserAuthenticatorsAdmin>();
        var authenticatorsSelfService = services.GetRequiredService<IUserAuthenticatorsSelfService>();

        var existing = await profiles.TryGetAsync(EmailCode, normalizedEmail, ct);
        var subjectId = existing?.SubjectId;

        if (subjectId is null)
        {
            var schema = await profiles.GetSchemaAsync(ct);
            var attributes = new AttributeValueCollection(schema);
            attributes.Set(EmailCode, normalizedEmail);
            attributes.Set(NameCode, string.IsNullOrWhiteSpace(configured.Name) ? normalizedEmail : configured.Name.Trim());

            if (username.Length > 0)
            {
                await EnsureUsernameIsAvailableAsync(profiles, username, null, ct);
                attributes.Set(ProfileSchema.PreferredUsername, username);
            }

            if (!attributes.TryValidate(out var validated, out var errors))
            {
                throw new InvalidOperationException(
                    $"Development seed user '{normalizedEmail}' has invalid attributes: {string.Join("; ", errors)}");
            }

            var created = await profiles.TryAddAsync(UserSubjectId.New(), validated, ct)
                ?? throw new InvalidOperationException(
                    $"Failed to seed the Development user '{normalizedEmail}': the profile could not be created.");

            subjectId = created.SubjectId;
            logger.LogInformation("Seeded Development user {Email}", normalizedEmail);
        }
        else
        {
            await RepairProfileAsync(
                profileSelfService,
                profiles,
                existing!,
                username,
                configured.Name,
                normalizedEmail,
                ct);
        }

        await EnsureAuthenticatorsAsync(authenticatorsAdmin, subjectId, normalizedEmail, ct);
        await EnsurePasswordAsync(authenticatorsSelfService, subjectId, configured.Password, normalizedEmail, ct);
        await EnsureTotpAsync(authenticatorsSelfService, subjectId, configured.TotpSecret, normalizedEmail, ct);

        logger.LogInformation(
            "Development user {Email} is ready (username: {Username}, TOTP: {Totp})",
            normalizedEmail,
            username,
            string.IsNullOrWhiteSpace(configured.TotpSecret) ? "off" : "on");
    }

    private static async Task RepairProfileAsync(
        IUserProfileSelfService profileSelfService,
        IUserProfileAdmin profiles,
        UserProfile profile,
        string username,
        string? displayName,
        string normalizedEmail,
        CancellationToken ct)
    {
        var needsUsername = username.Length > 0
            && (!profile.Attributes.TryGetValue(ProfileSchema.PreferredUsername.Code, out var current)
                || !current.TryGetValue<string>(out var value)
                || !string.Equals(value, username, StringComparison.Ordinal));

        var needsName = !string.IsNullOrWhiteSpace(displayName)
            && (!profile.Attributes.TryGetValue(NameCode, out var currentName)
                || !currentName.TryGetValue<string>(out var name)
                || !string.Equals(name, displayName.Trim(), StringComparison.Ordinal));

        if (!needsUsername && !needsName)
        {
            return;
        }

        if (needsUsername)
        {
            await EnsureUsernameIsAvailableAsync(profiles, username, profile.SubjectId, ct);
        }

        var schema = await profileSelfService.GetSchemaAsync(ct);
        var attributes = new AttributeValueCollection(schema, profile.Attributes.Values);

        if (needsUsername)
        {
            attributes.Set(ProfileSchema.PreferredUsername, username);
        }

        if (needsName)
        {
            attributes.Set(NameCode, displayName!.Trim());
        }

        if (!attributes.TryValidate(out var validated, out var errors))
        {
            throw new InvalidOperationException(
                $"Development seed user '{normalizedEmail}' could not be repaired: {string.Join("; ", errors)}");
        }

        _ = await profileSelfService.TryUpdateAsync(profile.SubjectId, validated, ct)
            ?? throw new InvalidOperationException(
                $"Failed to repair the Development user '{normalizedEmail}': the profile could not be updated.");
    }

    private static async Task EnsureUsernameIsAvailableAsync(
        IUserProfileAdmin profiles,
        string username,
        UserSubjectId? currentSubjectId,
        CancellationToken ct)
    {
        var owner = await profiles.TryGetAsync(ProfileSchema.PreferredUsername.Code, username, ct);

        if (owner is not null && owner.SubjectId != currentSubjectId)
        {
            throw new InvalidOperationException(
                $"The Development seed username '{username}' is already used by another user.");
        }
    }

    private static async Task EnsureAuthenticatorsAsync(
        IUserAuthenticatorsAdmin authenticators,
        UserSubjectId subjectId,
        string normalizedEmail,
        CancellationToken ct)
    {
        var address = new OtpAddress(OtpChannel.Email, EmailAddress.Create(normalizedEmail));
        var existing = await authenticators.TryGetAsync(subjectId, ct);

        if (existing is null)
        {
            _ = await authenticators.TryAddAsync(subjectId, [address], [], ct)
                ?? throw new InvalidOperationException(
                    $"Failed to seed the Development user '{normalizedEmail}': the authenticator record could not be created.");

            return;
        }

        var hasAddress = existing.OtpAddresses.Any(candidate =>
            candidate.Channel.Equals(OtpChannel.Email)
            && string.Equals(candidate.SubjectId.ToString()?.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase));

        if (!hasAddress && !await authenticators.TryAddOtpAddressesAsync(subjectId, [address], ct))
        {
            throw new InvalidOperationException(
                $"Failed to repair the Development user '{normalizedEmail}': the OTP address could not be added.");
        }
    }

    private static async Task EnsurePasswordAsync(
        IUserAuthenticatorsSelfService authenticators,
        UserSubjectId subjectId,
        string password,
        string normalizedEmail,
        CancellationToken ct)
    {
        var validation = await authenticators.TryValidatePasswordAsync(subjectId, password, ct);

        if (validation is PasswordCreationResult.Failed failed)
        {
            throw new InvalidOperationException(
                $"The configured password for Development user '{normalizedEmail}' is invalid: {string.Join("; ", failed.Errors)}");
        }

        if (validation is not PasswordCreationResult.Success success)
        {
            throw new InvalidOperationException(
                $"The configured password for Development user '{normalizedEmail}' could not be validated.");
        }

        if (!await authenticators.TrySetPasswordAsync(subjectId, success.Password, ct))
        {
            // TrySetPasswordAsync is the create path; an existing credential must be reset
            // through the explicit reset operation so repeated Development starts stay
            // deterministic.
            var current = await authenticators.TryGetAsync(subjectId, ct);

            if (current?.HasPassword != true
                || !await authenticators.TryResetPasswordAsync(subjectId, success.Password, ct))
            {
                throw new InvalidOperationException(
                    $"Failed to seed the Development user '{normalizedEmail}': the password could not be set.");
            }
        }
    }

    private static async Task EnsureTotpAsync(
        IUserAuthenticatorsSelfService authenticators,
        UserSubjectId subjectId,
        string? secret,
        string normalizedEmail,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        var current = await authenticators.TryGetAsync(subjectId, ct);

        if (current?.TotpDeviceNames.Contains(TotpDeviceName.Default) == true)
        {
            return;
        }

        if (!PlainBytesTotpKey.TryDecodeFromBase32(secret, out var key) || key is null)
        {
            throw new InvalidOperationException(
                $"The configured TOTP secret for Development user '{normalizedEmail}' is not valid Base32.");
        }

        PlainBytesTotpKey validatedKey = key.Value;
        var code = PlainTextTotp.Create(GenerateTotpCode(secret, DateTimeOffset.UtcNow));

        if (!await authenticators.TryAddTotpDeviceAsync(subjectId, TotpDeviceName.Default, validatedKey, code, ct))
        {
            throw new InvalidOperationException(
                $"Failed to seed the TOTP device for Development user '{normalizedEmail}'.");
        }
    }

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        // EmailAddress equality is case-sensitive and the OTP authenticator resolves users by
        // OtpAddress equality, so the seed, the login page, and the invite check all normalize
        // the same way: trim + lowercase.
        var normalized = email.Trim().ToLowerInvariant();

        if (!EmailAddress.TryCreate(normalized, out _))
        {
            throw new InvalidOperationException(
                $"The Development seed email '{email}' is not a valid email address.");
        }

        return normalized;
    }

    private static string NormalizeUsername(string username) =>
        string.IsNullOrWhiteSpace(username) ? string.Empty : username.Trim().ToLowerInvariant();

    /// <summary>
    /// Generates the RFC 6238 code for the current 30-second step. The seed only needs this
    /// to enroll a deterministic Development TOTP device; the application itself verifies codes
    /// through <see cref="ITotpAuthenticator"/>.
    /// </summary>
    private static string GenerateTotpCode(string base32Secret, DateTimeOffset now)
    {
        var key = DecodeBase32(base32Secret);
        var counter = now.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var text = input.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(text.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var character in text)
        {
            var value = alphabet.IndexOf(character, StringComparison.Ordinal);

            if (value < 0)
            {
                throw new InvalidOperationException("The TOTP secret contains a non-Base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. bytes];
    }
}
