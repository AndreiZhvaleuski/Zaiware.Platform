using System.Data.Common;
using Amazon.Runtime;
using Amazon.S3;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Avatar object storage settings, parsed from the Aspire-injected <c>Avatars</c>
/// connection string (<c>Endpoint=http://host:port;AccessKey=...;SecretKey=...;Bucket=avatars</c>).
/// </summary>
public sealed class AvatarStorageOptions
{
    /// <summary>The S3-compatible endpoint used by the server for PUT/HEAD, including the scheme.</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>
    /// The optional host-visible endpoint used only to presign GET URLs. Container stacks
    /// reach RustFS at an internal name while the browser needs a host-published address,
    /// and the SigV4 signature is bound to the host in the URL.
    /// </summary>
    public string? PublicServiceUrl { get; init; }

    /// <summary>The S3 access key.</summary>
    public required string AccessKey { get; init; }

    /// <summary>The S3 secret key.</summary>
    public required string SecretKey { get; init; }

    /// <summary>The private bucket that stores avatar objects.</summary>
    public required string BucketName { get; init; }

    /// <summary>The signing region. RustFS accepts the S3 default.</summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>
    /// The scheme, host, and port the browser is sent to for presigned avatar reads. This is
    /// the public endpoint when configured, otherwise the service endpoint (which is also
    /// what the presigning client falls back to). The CSP uses this single origin so avatar
    /// redirects are not blocked without allowing arbitrary image hosts.
    /// </summary>
    public string PublicOrigin =>
        new Uri(PublicServiceUrl ?? ServiceUrl).GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// Parses and validates the injected connection string eagerly so a missing or
    /// malformed object-storage configuration fails at startup. <paramref name="publicEndpoint"/>
    /// (from <c>Avatars:PublicEndpoint</c>) overrides the optional connection-string value.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required value is missing or malformed.</exception>
    public static AvatarStorageOptions FromConnectionString(
        string connectionString,
        string? publicEndpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        var endpoint = Read(builder, "Endpoint");
        var accessKey = Read(builder, "AccessKey");
        var secretKey = Read(builder, "SecretKey");
        var bucket = Read(builder, "Bucket");

        var serviceUri = ParseEndpoint(endpoint, "Endpoint");

        var configuredPublicEndpoint = publicEndpoint;
        if (string.IsNullOrWhiteSpace(configuredPublicEndpoint)
            && builder.TryGetValue("PublicEndpoint", out var connectionStringPublic)
            && connectionStringPublic is string text
            && !string.IsNullOrWhiteSpace(text))
        {
            configuredPublicEndpoint = text;
        }

        var publicServiceUrl = string.IsNullOrWhiteSpace(configuredPublicEndpoint)
            ? null
            : ParseEndpoint(configuredPublicEndpoint, "PublicEndpoint").ToString().TrimEnd('/');

        return new AvatarStorageOptions
        {
            ServiceUrl = serviceUri.ToString().TrimEnd('/'),
            PublicServiceUrl = publicServiceUrl,
            AccessKey = accessKey,
            SecretKey = secretKey,
            BucketName = bucket,
        };
    }

    private static Uri ParseEndpoint(string endpoint, string name)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Avatar storage configuration is invalid: cannot parse the {name} '{endpoint}'.");
        }

        return uri;
    }

    private static string Read(DbConnectionStringBuilder builder, string key) =>
        builder.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException(
                $"Avatar storage configuration is missing '{key}'. Expected Endpoint, AccessKey, SecretKey and Bucket.");

    /// <summary>Creates the path-style S3 client used for RustFS PUT/HEAD requests.</summary>
    public IAmazonS3 CreateClient() => CreateClient(ServiceUrl);

    /// <summary>Creates the second client used only for browser-facing presigned URLs.</summary>
    public IAmazonS3 CreatePublicClient() => CreateClient(PublicServiceUrl ?? ServiceUrl);

    private IAmazonS3 CreateClient(string serviceUrl) =>
        new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = Region,
            });
}
