var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithPgAdmin()
    .WithRepl();

var identityServerDb = postgres.AddDatabase("IdentityServer", "zaiware-platform-identity-server");

var mailpit = builder.AddMailPit("mailpit", httpPort: 8025, smtpPort: 1025);

var rustfs = builder.AddRustFs("rustfs")
    .WithDataVolume();

var avatarsBucket = rustfs.AddBucket("avatars");

var identityServer = builder
    .AddProject<Projects.Zaiware_Platform_IdentityServer>("identity-server")
    .WithReference(identityServerDb)
    .WithReference(mailpit, "Smtp")
    .WithReference(avatarsBucket, "Avatars")
    .WithEnvironment("DATA_PROTECTION__APPLICATION_NAME", "Zaiware.Platform.IdentityServer")
    .WithVolume("data-protection-keys", "~/data-protection-keys", "DATA_PROTECTION__KEYS_STORE_DIRECTORY")
    .WaitFor(identityServerDb)
    .WaitFor(mailpit)
    .WaitFor(rustfs);

builder.Build().Run();
