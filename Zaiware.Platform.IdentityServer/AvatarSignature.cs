namespace Zaiware.Platform.IdentityServer;

/// <summary>
/// Detects PNG, JPEG, and WebP magic bytes. The read uses <c>ReadAtLeastAsync</c> so a
/// stream that returns fewer bytes per read than requested cannot falsely reject a valid
/// file. Signature checks complement the content-type allow-list; the bytes are still
/// stored privately and served only through authenticated routes.
/// </summary>
public static class AvatarSignature
{
    /// <summary>The number of leading bytes needed to recognize every supported format.</summary>
    public const int RequiredBytes = 12;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the leading bytes and returns whether they match a supported image.</summary>
    public static async Task<bool> HasExpectedSignatureAsync(Stream stream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new byte[RequiredBytes];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return IsValid(buffer.AsSpan(0, read));
    }

    /// <summary>Returns whether the span starts with a PNG, JPEG, or WebP signature.</summary>
    public static bool IsValid(ReadOnlySpan<byte> bytes)
    {
        var isPng = bytes.Length >= PngSignature.Length
            && bytes[..PngSignature.Length].SequenceEqual(PngSignature);
        var isJpeg = bytes.Length >= 3
            && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var isWebp = bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';

        return isPng || isJpeg || isWebp;
    }
}
