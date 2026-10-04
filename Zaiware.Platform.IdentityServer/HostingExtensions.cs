using Microsoft.AspNetCore.DataProtection;

namespace Zaiware.Platform.IdentityServer
{
    internal static class HostingExtensions
    {
        public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
        {
            // uncomment if you want to add a UI
            //builder.Services.AddRazorPages();

            _ = builder.Services.AddIdentityServer()
                .AddInMemoryIdentityResources(Config.IdentityResources)
                .AddInMemoryApiScopes(Config.ApiScopes)
                .AddInMemoryClients(Config.Clients)
                .AddLicenseSummary();

            _ = builder.Services
                .AddDataProtection()
                .SetApplicationName(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionApplicationName))
                .PersistKeysToFileSystem(new DirectoryInfo(Environment.GetRequiredEnvironmentVariable(EnvironmentVariablesNames.DataProtectionKeysStoreDirectory)));

            return builder.Build();
        }

        public static WebApplication ConfigurePipeline(this WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                _ = app.UseDeveloperExceptionPage();
            }

            // uncomment if you want to add a UI
            //app.UseStaticFiles();
            //app.UseRouting();

            _ = app.UseIdentityServer();

            // uncomment if you want to add a UI
            //app.UseAuthorization();
            //app.MapRazorPages().RequireAuthorization();

            return app;
        }
    }
}
