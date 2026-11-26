namespace Zaiware.Platform.IdentityServer;

/// <summary>Server-mediated access to the private avatar bucket.</summary>
public interface IAvatarStorage
{
    /// <summary>Uploads (or replaces) the avatar object for the subject.</summary>
    Task UploadAsync(string objectKey, Stream content, string contentType, CancellationToken ct);

    /// <summary>Returns whether an avatar object exists for the subject.</summary>
    Task<bool> ExistsAsync(string objectKey, CancellationToken ct);

    /// <summary>Creates a short-lived presigned GET URL.</summary>
    string CreatePresignedGetUrl(string objectKey, TimeSpan lifetime);
}
