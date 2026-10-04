var builder = DistributedApplication.CreateBuilder(args);

var identityServer = builder
    .AddProject<Projects.Zaiware_Platform_IdentityServer>("identity-server");

builder.Build().Run();
