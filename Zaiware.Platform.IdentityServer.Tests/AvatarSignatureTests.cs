namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class AvatarSignatureTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    private static readonly byte[] Webp =
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x00, 0x00, 0x00, 0x00,
         (byte)'W', (byte)'E', (byte)'B', (byte)'P'];

    [Theory]
    [MemberData(nameof(ValidSignatures))]
    public void IsValid_accepts_supported_formats(byte[] bytes) =>
        Assert.True(AvatarSignature.IsValid(bytes));

    public static IEnumerable<object[]> ValidSignatures() =>
    [
        [Png],
        [Jpeg],
        [Webp],
    ];

    [Fact]
    public void IsValid_rejects_short_wrong_and_spoofed_content()
    {
        Assert.False(AvatarSignature.IsValid([0x89, 0x50]));
        Assert.False(AvatarSignature.IsValid("this is not an image"u8));
        Assert.False(AvatarSignature.IsValid([0xFF, 0xD8]));
        Assert.False(AvatarSignature.IsValid([(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E']));
    }

    [Fact]
    public async Task HasExpectedSignatureAsync_tolerates_one_byte_reads()
    {
        await using var stream = new OneByteAtATimeStream(Png);

        Assert.True(await AvatarSignature.HasExpectedSignatureAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task HasExpectedSignatureAsync_rejects_short_files()
    {
        await using var stream = new MemoryStream([0x89, 0x50, 0x4E]);

        Assert.False(await AvatarSignature.HasExpectedSignatureAsync(stream, CancellationToken.None));
    }

    private sealed class OneByteAtATimeStream(byte[] content) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position >= content.Length || buffer.Length == 0)
            {
                return 0;
            }

            buffer[0] = content[_position++];
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
