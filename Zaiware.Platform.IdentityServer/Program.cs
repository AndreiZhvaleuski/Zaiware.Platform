using Duende.IdentityServer;
using Duende.Storage.PostgreSql;
using Duende.Storage.Schema;
using Microsoft.AspNetCore.DataProtection;
using Zaiware.Platform.IdentityServer;

var builder = WebApplication.CreateBuilder(args);

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
