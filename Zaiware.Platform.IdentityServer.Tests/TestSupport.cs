using System.Reflection;
using System.Runtime.CompilerServices;
using Duende.IdentityServer.DataExtensions;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Duende.UserManagement.Authentication.Otp;
using Duende.UserManagement.Authentication.Passwords;
using Duende.UserManagement.Authentication.Totp;
using Duende.UserManagement.Profiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Zaiware.Platform.IdentityServer.Tests;

internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class TestTempDataProvider : ITempDataProvider
{
    private IDictionary<string, object> _values = new Dictionary<string, object>();

    public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

    public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
}

internal static class Fakes
{
    public static DefaultHttpContext CreateHttpContext(
        string path = "/",
        string scheme = "https")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Scheme = scheme;
        return context;
    }

    public static UserProfile CreateProfile(UserSubjectId subjectId)
    {
        var profile = (UserProfile)RuntimeHelpers.GetUninitializedObject(typeof(UserProfile));
        Set(profile, "<SubjectId>k__BackingField", subjectId);
        Set(profile, "<Attributes>k__BackingField",
            new Dictionary<AttributeCode, AttributeValue>());
        return profile;
    }

    public static UserAuthenticators CreateAuthenticators(
        UserSubjectId subjectId,
        bool hasPassword = false,
        IReadOnlyCollection<TotpDeviceName>? totpDevices = null,
        int recoveryCodeCount = 0)
    {
        var authenticators = (UserAuthenticators)RuntimeHelpers.GetUninitializedObject(typeof(UserAuthenticators));
        Set(authenticators, "<SubjectId>k__BackingField", subjectId);
        Set(authenticators, "<OtpAddresses>k__BackingField", Array.Empty<Duende.UserManagement.Authentication.Otp.OtpAddress>());
        Set(authenticators, "<ExternalAuthenticatorAddresses>k__BackingField", Array.Empty<Duende.UserManagement.Authentication.External.ExternalAuthenticatorAddress>());
        Set(authenticators, "<TotpDeviceNames>k__BackingField",
            totpDevices ?? Array.Empty<TotpDeviceName>());
        Set(authenticators, "<Passkeys>k__BackingField", Array.Empty<Duende.UserManagement.Authentication.Passkeys.UserPasskey>());
        Set(authenticators, "<RecoveryCodeCount>k__BackingField", recoveryCodeCount);
        Set(authenticators, "<HasPassword>k__BackingField", hasPassword);
        return authenticators;
    }

    public static PasswordAuthenticationResult.Success PasswordSuccess(UserSubjectId subjectId)
    {
        var result = (PasswordAuthenticationResult.Success)RuntimeHelpers.GetUninitializedObject(
            typeof(PasswordAuthenticationResult.Success));
        Set(result, "<UserSubjectId>k__BackingField", subjectId);
        return result;
    }

    public static ValidatedPlainTextPassword ValidatedPassword(string value)
    {
        var password = (ValidatedPlainTextPassword)RuntimeHelpers.GetUninitializedObject(
            typeof(ValidatedPlainTextPassword));
        Set(password, "<Value>k__BackingField", value);
        return password;
    }

    public static SendOtpResult.Sent SentOtp(DateTimeOffset now)
    {
        var sent = (SendOtpResult.Sent)RuntimeHelpers.GetUninitializedObject(typeof(SendOtpResult.Sent));
        Set(sent, "<Token>k__BackingField", OtpToken.Create(Guid.CreateVersion7().ToString()));
        Set(sent, "<ExpiresAfter>k__BackingField", TimeSpan.FromMinutes(5));
        Set(sent, "<ExpiresAtUtc>k__BackingField", now.AddMinutes(5));
        Set(sent, "<SendingBlockedFor>k__BackingField", TimeSpan.FromMinutes(1));
        Set(sent, "<SendingBlockedUntilUtc>k__BackingField", now.AddMinutes(1));
        return sent;
    }

    private static void Set(object target, string fieldName, object value) =>
        target.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
