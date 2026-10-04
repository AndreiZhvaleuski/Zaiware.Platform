namespace Zaiware.Platform.IdentityServer;

public static class EnvironmentExtensions
{
    extension(Environment)
    {
        /// <summary>
        /// Retrieves the value of an environment variable from the current process
        /// </summary>
        /// <param name="variable">The name of the environment variable.</param>
        /// <returns>The value of the environment variable specified by <paramref name="variable"/>.</returns>
        /// <exception cref="InvalidOperationException">There is no environment variable with the the name <paramref name="variable"/>.</exception>
        public static string GetRequiredEnvironmentVariable(string variable)
        {
            return Environment.GetEnvironmentVariable(variable)
                ?? throw new InvalidOperationException($"No environment variable with name '{variable}' has been found.");
        }
    } 
}
