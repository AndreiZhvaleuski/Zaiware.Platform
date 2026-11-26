namespace Zaiware.Platform.IdentityServer.Tests;

public sealed class AvatarStorageOptionsTests
{
    private const string Valid =
        "Endpoint=http://rustfs:9000;AccessKey=key;SecretKey=secret;Bucket=avatars";

    [Fact]
    public void Parses_the_required_connection_string_values()
    {
        var options = AvatarStorageOptions.FromConnectionString(Valid);

        Assert.Equal("http://rustfs:9000", options.ServiceUrl);
        Assert.Equal("key", options.AccessKey);
        Assert.Equal("secret", options.SecretKey);
        Assert.Equal("avatars", options.BucketName);
        Assert.Null(options.PublicServiceUrl);
    }

    [Fact]
    public void Reads_PublicEndpoint_from_the_connection_string()
    {
        var options = AvatarStorageOptions.FromConnectionString(
            Valid + ";PublicEndpoint=http://localhost:9000/");

        Assert.Equal("http://localhost:9000", options.PublicServiceUrl);
    }

    [Fact]
    public void Explicit_public_endpoint_overrides_the_connection_string()
    {
        var options = AvatarStorageOptions.FromConnectionString(
            Valid + ";PublicEndpoint=http://ignored:1111",
            "https://cdn.example.test");

        Assert.Equal("https://cdn.example.test", options.PublicServiceUrl);
    }

    [Theory]
    [InlineData("Endpoint=http://x;AccessKey=k;SecretKey=s")]
    [InlineData("Endpoint=http://x;AccessKey=k;SecretKey=s;Bucket=")]
    public void Missing_required_values_fail_at_parse_time(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => AvatarStorageOptions.FromConnectionString(connectionString));

    [Theory]
    [InlineData("Endpoint=not-a-url;AccessKey=k;SecretKey=s;Bucket=avatars")]
    [InlineData("Endpoint=ftp://x;AccessKey=k;SecretKey=s;Bucket=avatars")]
    [InlineData("Endpoint=http://x;AccessKey=k;SecretKey=s;Bucket=avatars;PublicEndpoint=bad")]
    public void Invalid_endpoints_fail_at_parse_time(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => AvatarStorageOptions.FromConnectionString(connectionString));

    [Fact]
    public void Presigned_urls_use_the_public_endpoint_when_configured()
    {
        var options = AvatarStorageOptions.FromConnectionString(
            Valid + ";PublicEndpoint=http://localhost:9000");
        var storage = new AvatarStorage(options.CreateClient(), options);

        var url = storage.CreatePresignedGetUrl("subject-1", TimeSpan.FromMinutes(5));

        Assert.StartsWith("http://localhost:9000/avatars/subject-1", url, StringComparison.Ordinal);
        Assert.Contains("X-Amz-Signature=", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Presigned_urls_fall_back_to_the_internal_endpoint()
    {
        var options = AvatarStorageOptions.FromConnectionString(Valid);
        var storage = new AvatarStorage(options.CreateClient(), options);

        var url = storage.CreatePresignedGetUrl("subject-1", TimeSpan.FromMinutes(5));

        Assert.StartsWith("http://rustfs:9000/avatars/subject-1", url, StringComparison.Ordinal);
    }
}
