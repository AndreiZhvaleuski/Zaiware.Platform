using Duende.IdentityServer;
using Duende.Storage.PostgreSql;
using Duende.Storage.Schema;
using Microsoft.AspNetCore.DataProtection;
using Zaiware.Platform.IdentityServer;
using Zaiware.Platform.ServiceDefaults.Observability;

var builder = WebApplication.CreateBuilder(args);

_ = builder.AddObservability()
    .Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(Duende.IdentityServer.Telemetry.ServiceName);
    })
    .WithTracing(traces =>
    {
        traces
            .AddSource(IdentityServerConstants.Tracing.Basic)
            .AddSource(IdentityServerConstants.Tracing.Cache)
            .AddSource(IdentityServerConstants.Tracing.Services)
            .AddSource(IdentityServerConstants.Tracing.Stores)
            .AddSource(IdentityServerConstants.Tracing.Validation);
    });

_ = builder.Services
    .AddNpgsqlDataSource(builder.Configuration.GetRequiredConnectionString("IdentityServer"))
    .AddIdentityServer()
    .AddStorage(builder => builder.AddPostgreSql())
    .AddConfigurationStorage()
    .AddOperationalStorage()
    .AddUserManagement(_ => {});

_ = builder.Services
    .AddDataProtection()
    .SetApplicationName(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionApplicationName))
    .PersistKeysToFileSystem(new DirectoryInfo(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionKeysStoreDirectory)));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider
            .GetRequiredService<IStorageInstanceSchema>()
            .MigrateAsync(CancellationToken.None);
    }

    _ = app.UseDeveloperExceptionPage();
}

_ = app.UseIdentityServer();

app.Run();
