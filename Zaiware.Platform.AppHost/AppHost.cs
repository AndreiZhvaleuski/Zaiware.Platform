var builder = DistributedApplication.CreateBuilder(args);

var identityServer = builder
    .AddProject<Projects.Zaiware_Platform_IdentityServer>("identity-server")
    .WithEnvironment("DATA_PROTECTION__APPLICATION_NAME", "Zaiware.Platform.IdentityServer")
    .WithVolume("data-protection-keys", "~/data-protection-keys", "DATA_PROTECTION__KEYS_STORE_DIRECTORY");

builder.Build().Run();
