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
    /// <summary>The S3-compatible endpoint, including the scheme.</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>The S3 access key.</summary>
    public required string AccessKey { get; init; }

    /// <summary>The S3 secret key.</summary>
    public required string SecretKey { get; init; }

    /// <summary>The private bucket that stores avatar objects.</summary>
    public required string BucketName { get; init; }

    /// <summary>The signing region. RustFS accepts the S3 default.</summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>
    /// Parses and validates the injected connection string eagerly so a missing or
    /// malformed object-storage configuration fails at startup.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required value is missing or malformed.</exception>
    public static AvatarStorageOptions FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        var endpoint = Read(builder, "Endpoint");
        var accessKey = Read(builder, "AccessKey");
        var secretKey = Read(builder, "SecretKey");
        var bucket = Read(builder, "Bucket");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Avatar storage configuration is invalid: cannot parse the Endpoint '{endpoint}'.");
        }

        return new AvatarStorageOptions
        {
            ServiceUrl = uri.ToString().TrimEnd('/'),
            AccessKey = accessKey,
            SecretKey = secretKey,
            BucketName = bucket,
        };
    }

    private static string Read(DbConnectionStringBuilder builder, string key) =>
        builder.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException(
                $"Avatar storage configuration is missing '{key}'. Expected Endpoint, AccessKey, SecretKey and Bucket.");

    /// <summary>Creates the path-style S3 client used for RustFS.</summary>
    public IAmazonS3 CreateClient() =>
        new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config
            {
                ServiceURL = ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = Region,
            });
}
