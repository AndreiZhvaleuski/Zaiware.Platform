var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithPgAdmin()
    .WithRepl();

var identityServerDb = postgres.AddDatabase("IdentityServer", "zaiware-platform-identity-server");

var mailpit = builder.AddMailPit("mailpit", httpPort: 8025, smtpPort: 1025);

// Pin the S3 API to a stable host port so the browser-visible presigned URL can use
// http://localhost:9000 while the IdentityServer itself talks to the resource endpoint.
var rustfs = builder.AddRustFs("rustfs")
    .WithHostPort(9000)
    .WithDataVolume();

var avatarsBucket = rustfs.AddBucket("avatars");

// Browser-facing presigned URL. The S3 host port is pinned to 9000 so this local default
// stays stable; non-local deployments override Avatars:PublicEndpoint (AVATARS__PUBLICENDPOINT).
var avatarsPublicEndpoint = builder.Configuration["Avatars:PublicEndpoint"] ?? "http://localhost:9000";

var identityServer = builder
    .AddDotnetProject(
        "identity-server",
        "../Zaiware.Platform.IdentityServer/Zaiware.Platform.IdentityServer.csproj",
        options => options.LaunchProfileName = "SelfHost")
    .WithReference(identityServerDb)
    .WithReference(mailpit, "Smtp")
    .WithReference(avatarsBucket, "Avatars")
    .WithEnvironment("AVATARS__PUBLICENDPOINT", avatarsPublicEndpoint)
    .WithEnvironment("DATA_PROTECTION__APPLICATION_NAME", "Zaiware.Platform.IdentityServer")
    .WithVolume("data-protection-keys", "~/data-protection-keys", "DATA_PROTECTION__KEYS_STORE_DIRECTORY")
    .WaitFor(identityServerDb)
    .WaitFor(mailpit)
    .WaitFor(rustfs);

builder.Build().Run();
