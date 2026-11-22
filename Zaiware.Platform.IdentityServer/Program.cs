using Duende.IdentityServer;
using Duende.Storage.PostgreSql;
using Duende.Storage.Schema;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
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

var smtpOptions = builder.Configuration.GetRequiredSmtpOtpDispatcherOptions();

_ = builder.Services
    .AddNpgsqlDataSource(builder.Configuration.GetRequiredConnectionString("IdentityServer"))
    .AddIdentityServer(options =>
    {
        options.UserInteraction.LoginUrl = "/Account/Login";
        options.UserInteraction.LogoutUrl = "/Account/Logout";
    })
    .AddStorage(builder => builder.AddPostgreSql())
    .AddConfigurationStorage()
    .AddOperationalStorage()
    .AddUserManagement(userManagement => userManagement
        .Authentication(authentication => authentication.UseSmtpOtpDispatcher(options =>
        {
            options.Host = smtpOptions.Host;
            options.Port = smtpOptions.Port;
            options.EnableSsl = smtpOptions.EnableSsl;
            options.FromEmail = smtpOptions.FromEmail;
            options.FromName = smtpOptions.FromName;
            options.Domain = smtpOptions.Domain;
        })));

builder.Services.AddInviteOnlyOtpLogin();

_ = builder.Services
    .AddDataProtection()
    .SetApplicationName(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionApplicationName))
    .PersistKeysToFileSystem(new DirectoryInfo(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionKeysStoreDirectory)));

builder.Services.AddRazorPages();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider
            .GetRequiredService<IStorageInstanceSchema>()
            .MigrateAsync(CancellationToken.None);
    }

    var seedSection = app.Configuration.GetSection("SeedUser");
    var seedEmail = seedSection["Email"];

    if (string.IsNullOrWhiteSpace(seedEmail) || !EmailAddress.TryCreate(seedEmail, out var seedAddress))
    {
        app.Logger.LogWarning("SeedUser:Email not configured; skipping Development user seed");
    }
    else
    {
        var seedName = seedSection["Name"];

        using (var scope = app.Services.CreateScope())
        {
            await DevelopmentUserSeed.EnsureInvitedUserAsync(
                scope.ServiceProvider,
                seedAddress.Value,
                string.IsNullOrWhiteSpace(seedName) ? "Demo User" : seedName,
                CancellationToken.None);
        }
    }

    _ = app.UseDeveloperExceptionPage();
}

_ = app.UseIdentityServer();

_ = app.UseStaticFiles();
_ = app.UseAuthorization();
app.MapRazorPages();

app.Run();
