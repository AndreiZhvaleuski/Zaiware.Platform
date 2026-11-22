var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithPgAdmin()
    .WithRepl();

var identityServerDb = postgres.AddDatabase("IdentityServer", "zaiware-platform-identity-server");

var mailpit = builder.AddMailPit("mailpit");

var identityServer = builder
    .AddProject<Projects.Zaiware_Platform_IdentityServer>("identity-server")
    .WithReference(identityServerDb)
    .WithReference(mailpit, "Smtp")
    .WithEnvironment("DATA_PROTECTION__APPLICATION_NAME", "Zaiware.Platform.IdentityServer")
    .WithVolume("data-protection-keys", "~/data-protection-keys", "DATA_PROTECTION__KEYS_STORE_DIRECTORY")
    .WaitFor(identityServerDb)
    .WaitFor(mailpit);

builder.Build().Run();
