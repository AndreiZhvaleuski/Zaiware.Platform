using Amazon.S3;
using Amazon.S3.Model;

namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Server-mediated access to the private avatar bucket. Objects are stored under the
/// user's subject key; reads are served through short-lived presigned URLs and never
/// expose the bucket publicly.
/// </summary>
/// <remarks>
/// When <see cref="AvatarStorageOptions.PublicServiceUrl"/> is configured, a second S3
/// client is used only for presigning so the URL the browser receives points at the
/// host-visible RustFS endpoint. PUT/HEAD stay on the internal endpoint.
/// </remarks>
public sealed class AvatarStorage(IAmazonS3 s3, AvatarStorageOptions options) : IAvatarStorage
{
    /// <summary>The maximum accepted avatar size (2 MB).</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>
    /// The request body limit for the avatar handler: the file cap plus multipart/form
    /// overhead. Kestrel rejects larger bodies before the whole form is buffered.
    /// </summary>
    public const long MaxRequestBytes = MaxBytes + (64 * 1024);

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
    };

    private readonly IAmazonS3 _presignClient =
        options.PublicServiceUrl is null ? s3 : options.CreatePublicClient();

    /// <summary>Whether the content type is one of the accepted image formats.</summary>
    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && AllowedContentTypes.Contains(contentType);

    /// <summary>
    /// Uploads (or replaces) the avatar object for the subject. The bucket stays private;
    /// callers must have validated type and size before calling this method.
    /// </summary>
    public async Task UploadAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = options.BucketName,
                Key = objectKey,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
            },
            ct);
    }

    /// <summary>Returns whether an avatar object exists for the subject.</summary>
    public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            _ = await s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = options.BucketName,
                    Key = objectKey,
                },
                ct);

            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a short-lived presigned GET URL. The stored picture value never contains
    /// this URL because it expires.
    /// </summary>
    public string CreatePresignedGetUrl(string objectKey, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var url = _presignClient.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime),
        });

        // The SDK emits https for presigned URLs even when the endpoint is plain http.
        // SigV4 signs the host, not the scheme, so matching the configured endpoint keeps
        // the signature valid for local RustFS over http.
        var endpoint = options.PublicServiceUrl ?? options.ServiceUrl;

        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = string.Concat("http://", url.AsSpan("https://".Length));
        }

        return url;
    }
}
