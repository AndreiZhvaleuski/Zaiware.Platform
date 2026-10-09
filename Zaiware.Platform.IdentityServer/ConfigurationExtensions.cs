namespace Zaiware.Platform.IdentityServer;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Gets the specified connection string from the specified configuration.
    /// Shorthand for <c>GetSection("ConnectionStrings")[name]</c> with check for 
    /// connection string string to be not null, empty, or consist only of white-space characters.
    /// </summary>
    /// <param name="configuration">The configuration to enumerate.</param>
    /// <param name="name">The connection string key.</param>
    /// <returns>The connection string.</returns>
    /// <exception cref="InvalidOperationException">There is no connection string with <paramref name="name"/>.</exception>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var connectionString = configuration.GetConnectionString(name);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"There is no connection string '{name}' configured.");
        }

        return connectionString;
    }
}
