using System.Reflection;
using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// An <see cref="IOtpAuthenticator"/> decorator that only lets addresses belonging to an
/// already-created user complete sign-in.
/// </summary>
/// <remarks>
/// The default User Management authenticator resolves an OTP address and creates a new user when
/// no <c>UserAuthenticators</c> record exists. That auto-registration is the exact behavior this
/// service exists to prevent, but the library exposes no seam between "resolve the address" and
/// "create the user": the default authenticator talks straight to an internal repository. This
/// decorator therefore reads the challenge's address from the OTP workflow store, checks that the
/// address is invited, and only then delegates to the captured default authenticator. A rejected
/// challenge never reaches the inner authenticator, so its create branch is never taken.
/// </remarks>
/// <param name="inner">The default User Management OTP authenticator to delegate invited sign-ins to.</param>
/// <param name="profiles">The profile administration service used to look users up by email.</param>
/// <param name="authenticators">The authenticator administration service used to check the OTP address record.</param>
/// <param name="challengeAddresses">Reads the address stored for an OTP token without consuming a verification attempt.</param>
/// <param name="failure">The cached <see cref="OtpAuthenticationResult.Failure"/> singleton; see <see cref="InviteOnlyOtpServiceCollectionExtensions"/>.</param>
/// <param name="logger">The logger.</param>
public sealed class InviteOnlyOtpAuthenticator(
    IOtpAuthenticator inner,
    IUserProfileAdmin profiles,
    IUserAuthenticatorsAdmin authenticators,
    OtpChallengeAddressReader challengeAddresses,
    OtpAuthenticationResult failure,
    ILogger<InviteOnlyOtpAuthenticator> logger) : IOtpAuthenticator
{
    /// <inheritdoc />
    public async Task<OtpAuthenticationResult> TryAuthenticateAsync(
        PlainTextOtp otp,
        OtpToken token,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(otp);
        ArgumentNullException.ThrowIfNull(token);

        var address = await challengeAddresses.TryResolveAsync(token, ct);

        if (address is not null && !await IsInvitedAsync(address, ct))
        {
            logger.LogWarning(
                "Rejected invite-only OTP sign-in for {OtpChannel} address {OtpAddress}: no invited user is registered for it.",
                address.Channel.Value,
                address.SubjectId);

            return failure;
        }

        // An unknown or expired token has no stored address; the inner authenticator returns its
        // own failure for it. An invited address resolves to the pre-seeded user, so the inner
        // authenticator never takes its create-user branch.
        return await inner.TryAuthenticateAsync(otp, token, ct);
    }

    private async Task<bool> IsInvitedAsync(OtpAddress address, CancellationToken ct)
    {
        if (!address.Channel.Equals(OtpChannel.Email) || address.SubjectId is not EmailAddress email)
        {
            return false;
        }

        // EmailAddress comparison is case-sensitive and the default authenticator resolves users by
        // OtpAddress equality, so every producer/consumer normalizes the same way: trim + lowercase.
        var normalized = email.Value.Trim().ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return false;
        }

        var profile = await profiles.TryGetAsync(AttributeCode.Create("email"), normalized, ct);

        if (profile is null)
        {
            return false;
        }

        var existing = await authenticators.TryGetAsync(profile.SubjectId, ct);

        return existing is not null && existing.OtpAddresses.Any(candidate =>
            candidate.Channel.Equals(OtpChannel.Email)
            && string.Equals(
                candidate.SubjectId.ToString()?.Trim(),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Reads the <see cref="OtpAddress"/> stored for an OTP token without verifying (and therefore
/// without consuming) the code.
/// </summary>
/// <remarks>
/// The public User Management surface only exposes verification through <c>IOtpAuthenticator</c>,
/// which burns one of the code's four attempts. Reading the challenge's address directly from the
/// OTP workflow store is a pure read, so invite checks do not reduce the number of attempts a
/// legitimate user gets. The repository and workflow types are internal in the preview package,
/// which is why the members are resolved by reflection once at registration time (see
/// <see cref="InviteOnlyOtpServiceCollectionExtensions"/>).
/// </remarks>
/// <param name="services">The service provider used to resolve the scoped workflow repository.</param>
/// <param name="workflowRepositoryType">The internal <c>OtpWorkflowRepository</c> type.</param>
/// <param name="tryRead">The <c>TryReadAsync(OtpToken, CancellationToken)</c> method on that type.</param>
/// <param name="addressProperty">The internal <c>OtpWorkflow.Address</c> property.</param>
public sealed class OtpChallengeAddressReader(
    IServiceProvider services,
    Type workflowRepositoryType,
    MethodInfo tryRead,
    PropertyInfo addressProperty)
{
    /// <summary>
    /// Resolves the address associated with the specified token, or <see langword="null"/> when the
    /// token is unknown or expired.
    /// </summary>
    /// <param name="token">The OTP token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The stored OTP address, or <see langword="null"/>.</returns>
    public async Task<OtpAddress?> TryResolveAsync(OtpToken token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(token);

        var repository = services.GetRequiredService(workflowRepositoryType);
        var task = (Task)tryRead.Invoke(repository, [token, ct])!;

        await task.ConfigureAwait(false);

        // The repository returns Task<(OtpWorkflow, int)?>; a boxed non-null Nullable<ValueTuple>
        // is a boxed ValueTuple, so Item1 is readable through its public field.
        var result = task.GetType().GetProperty("Result")!.GetValue(task);

        if (result is null)
        {
            return null;
        }

        var workflow = result.GetType().GetField("Item1")!.GetValue(result);

        return workflow is null ? null : (OtpAddress?)addressProperty.GetValue(workflow);
    }
}

/// <summary>
/// Registers invite-only email OTP sign-in by decorating the default User Management
/// <see cref="IOtpAuthenticator"/>. The decorator is the last registration for the service, which
/// is the one resolved by dependency injection.
/// </summary>
public static class InviteOnlyOtpServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the registered <see cref="IOtpAuthenticator"/> with
    /// <see cref="InviteOnlyOtpAuthenticator"/>, which rejects addresses that do not belong to an
    /// existing user.
    /// </summary>
    /// <remarks>
    /// Reflection is required because the preview package exposes neither a way to construct an
    /// <see cref="OtpAuthenticationResult.Failure"/> nor a public seam for resolving a user from an
    /// OTP address before the default authenticator creates one. The default authenticator itself is
    /// an internal type registered by implementation type, so it is captured from the service
    /// collection and activated through <see cref="ActivatorUtilities"/>. Every reflected member is
    /// resolved eagerly here: if a preview upgrade changes these internals, the application fails at
    /// startup with an actionable message instead of silently falling back to auto-registration.
    /// Only two internal members are pinned: <c>OtpWorkflowRepository.TryReadAsync(OtpToken, ...)</c>
    /// and <c>OtpWorkflow.Address</c>.
    /// </remarks>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The Duende preview internals this wiring depends on are no longer present.
    /// </exception>
    public static IServiceCollection AddInviteOnlyOtpLogin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var defaultAuthenticatorDescriptor = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IOtpAuthenticator) && descriptor.ImplementationType is not null);
        var defaultAuthenticatorType = defaultAuthenticatorDescriptor?.ImplementationType;

        var workflowRepositoryType = services
            .LastOrDefault(descriptor => descriptor.ServiceType.Name == "OtpWorkflowRepository")
            ?.ServiceType;

        var tryRead = workflowRepositoryType?
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SingleOrDefault(method =>
                method.Name == "TryReadAsync"
                && method.GetParameters() is [{ ParameterType: var firstParameter }, ..]
                && firstParameter == typeof(OtpToken));

        var workflowType = workflowRepositoryType?.Assembly.GetType(
            "Duende.UserManagement.Authentication.Otp.Internal.OtpWorkflow");

        var addressProperty = workflowType?.GetProperty(
            "Address",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        var failure = typeof(OtpAuthenticationResult.Failure)
            .GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as OtpAuthenticationResult;

        if (defaultAuthenticatorType is null
            || workflowRepositoryType is null
            || tryRead is null
            || addressProperty is null
            || failure is null)
        {
            throw new InvalidOperationException(
                "Duende preview internals changed; invite-only OTP wiring must be updated. " +
                "Expected a default IOtpAuthenticator registration, a registered OtpWorkflowRepository " +
                "with TryReadAsync(OtpToken, CancellationToken), OtpWorkflow.Address, and the " +
                "OtpAuthenticationResult.Failure.Instance singleton.");
        }

        services.AddTransient<IOtpAuthenticator>(provider => new InviteOnlyOtpAuthenticator(
            inner: (IOtpAuthenticator)ActivatorUtilities.CreateInstance(provider, defaultAuthenticatorType),
            profiles: provider.GetRequiredService<IUserProfileAdmin>(),
            authenticators: provider.GetRequiredService<IUserAuthenticatorsAdmin>(),
            challengeAddresses: new OtpChallengeAddressReader(
                provider,
                workflowRepositoryType,
                tryRead,
                addressProperty),
            failure: failure,
            logger: provider.GetRequiredService<ILogger<InviteOnlyOtpAuthenticator>>()));

        return services;
    }
}
