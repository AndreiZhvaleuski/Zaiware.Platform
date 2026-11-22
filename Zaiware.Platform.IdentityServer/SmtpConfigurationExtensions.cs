using System.Data.Common;
using Duende.UserManagement.Authentication.Otp;

namespace Zaiware.Platform.IdentityServer;

public static class SmtpConfigurationExtensions
{
    private const string SmtpConnectionStringName = "Smtp";
    private const string SmtpScheme = "smtp";
    private const int DefaultPort = 1025;

    /// <summary>
    /// Builds the SMTP OTP dispatcher options from the specified configuration.
    /// The endpoint is taken from the <c>ConnectionStrings:Smtp</c> connection string when present,
    /// otherwise from the <c>Smtp</c> configuration section. All checks happen eagerly, so a
    /// deployment with no SMTP configuration fails fast at startup.
    /// </summary>
    /// <param name="configuration">The configuration to enumerate.</param>
    /// <returns>The SMTP OTP dispatcher options.</returns>
    /// <exception cref="InvalidOperationException">The SMTP endpoint or the sender identity is missing.</exception>
    public static SmtpOtpDispatcherOptions GetRequiredSmtpOtpDispatcherOptions(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Smtp");
        var (host, port) = ResolveEndpoint(configuration, section);

        var fromEmail = section["FromEmail"];

        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            throw new InvalidOperationException(
                "SMTP configuration is missing: set Smtp:FromEmail (or Smtp__FromEmail) to the sender email address.");
        }

        var fromName = section["FromName"];

        if (string.IsNullOrWhiteSpace(fromName))
        {
            throw new InvalidOperationException(
                "SMTP configuration is missing: set Smtp:FromName (or Smtp__FromName) to the sender display name.");
        }

        return new SmtpOtpDispatcherOptions
        {
            Host = host,
            Port = port,
            EnableSsl = section.GetValue<bool?>("EnableSsl") ?? false,
            FromEmail = fromEmail,
            FromName = fromName,
            Domain = section["Domain"],
        };
    }

    private static (string Host, int Port) ResolveEndpoint(IConfiguration configuration, IConfigurationSection section)
    {
        var smtpConnectionString = configuration.GetConnectionString(SmtpConnectionStringName);

        if (!string.IsNullOrWhiteSpace(smtpConnectionString))
        {
            return ParseSmtpEndpoint(smtpConnectionString);
        }

        var host = section["Host"];

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "SMTP configuration is missing: configure a ConnectionStrings:Smtp endpoint (e.g. Endpoint=smtp://host:port) or a Smtp:Host/Smtp:Port section.");
        }

        return (host, section.GetValue<int?>("Port") ?? DefaultPort);
    }

    private static (string Host, int Port) ParseSmtpEndpoint(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString,
        };

        if (!builder.TryGetValue("Endpoint", out var endpointValue)
            || endpointValue is not string endpoint
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, SmtpScheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                $"SMTP configuration is invalid: cannot parse the ConnectionStrings:Smtp endpoint '{connectionString}'. Expected format: Endpoint=smtp://host:port.");
        }

        return (uri.Host, uri.Port > 0 ? uri.Port : DefaultPort);
    }
}
