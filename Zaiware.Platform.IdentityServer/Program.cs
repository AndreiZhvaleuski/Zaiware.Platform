using Duende.IdentityServer;
using Duende.Storage.PostgreSql;
using Duende.Storage.Schema;
using Duende.UserManagement;
using Duende.UserManagement.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Zaiware.Platform.IdentityServer;
using Zaiware.Platform.IdentityServer.Middleware;
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
var avatarOptions = AvatarStorageOptions.FromConnectionString(
    builder.Configuration.GetRequiredConnectionString("Avatars"),
    builder.Configuration["Avatars:PublicEndpoint"]);

builder.Services.AddSingleton(avatarOptions);
builder.Services.AddSingleton(avatarOptions.CreateClient());
builder.Services.AddSingleton<IAvatarStorage, AvatarStorage>();

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
        .Authentication(authentication =>
        {
            if (builder.Environment.IsDevelopment())
            {
                // Keep the throttling mechanism in place but make repeated end-to-end
                // attempts practical; production keeps the Duende defaults.
                authentication.Configure(options =>
                {
                    options.Throttling.MaxFailedAttempts = 10;
                    options.Throttling.ThrottleDuration = TimeSpan.FromSeconds(10);
                    options.Throttling.MaxAttemptsPerWindow = 50;
                    options.Throttling.VelocityWindow = TimeSpan.FromSeconds(30);
                    options.Throttling.VelocityThrottleDuration = TimeSpan.FromSeconds(1);
                    options.Throttling.EscalatingThrottleDurations = [];
                });
            }

            authentication.UseSmtpOtpDispatcher(options =>
            {
                options.Host = smtpOptions.Host;
                options.Port = smtpOptions.Port;
                options.EnableSsl = smtpOptions.EnableSsl;
                options.FromEmail = smtpOptions.FromEmail;
                options.FromName = smtpOptions.FromName;
                options.Domain = smtpOptions.Domain;
            });
        }))
    .AddInMemoryDataExtensionSchemas([ProfileSchema.UserProfile]);

builder.Services.AddInviteOnlyOtpLogin();

_ = builder.Services
    .AddDataProtection()
    .SetApplicationName(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionApplicationName))
    .PersistKeysToFileSystem(new DirectoryInfo(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionKeysStoreDirectory)));

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(
    builder.Configuration.GetSection(OtpSendRateLimitOptions.SectionName).Get<OtpSendRateLimitOptions>()
    ?? new OtpSendRateLimitOptions());
builder.Services.AddSingleton<IOtpSendRateLimiter, OtpSendRateLimiter>();
builder.Services.AddSingleton<ISecondFactorStateCookie, SecondFactorStateCookie>();
builder.Services.AddScoped<IUserSessionSignIn, UserSessionSignIn>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider
            .GetRequiredService<IStorageInstanceSchema>()
            .MigrateAsync(CancellationToken.None);
    }

    var seedUsers = app.Configuration.GetSection("SeedUsers").Get<DevelopmentSeedUser[]>() ?? [];

    if (seedUsers.Length == 0)
    {
        app.Logger.LogWarning("SeedUsers is not configured; skipping Development user seed");
    }
    else
    {
        using (var scope = app.Services.CreateScope())
        {
            await DevelopmentUserSeed.EnsureUsersAsync(
                scope.ServiceProvider,
                seedUsers,
                CancellationToken.None);
        }
    }

    _ = app.UseDeveloperExceptionPage();
}

// Security headers (including the per-request CSP nonce) apply to every response.
app.UseSecurityHeaders();

// Sign-in and account responses must never be restored from browser history:
// they can contain one-time codes, recovery codes, or signed-in state.
app.UseAuthPageNoStore();

// Fingerprinted static assets are immutable; the fingerprint changes with the content.
app.UseImmutableStaticAssetCaching();

_ = app.UseIdentityServer();

_ = app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();

app.Run();
