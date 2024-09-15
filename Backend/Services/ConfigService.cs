namespace Ehrenmeter.Backend.Services
{
    internal static class ConfigService
    {
        public static string GetConnectionString(string name)
        {
            var connectionString = System.Environment.GetEnvironmentVariable($"ConnectionStrings:{name}", EnvironmentVariableTarget.Process);
            if (string.IsNullOrEmpty(connectionString)) // Azure Functions App Service naming convention
                connectionString = System.Environment.GetEnvironmentVariable($"SQLAZURECONNSTR_{name}", EnvironmentVariableTarget.Process);

            if (connectionString is null)
                throw new Exception($"Couldn't get connection string {name}");

            return connectionString;
        }
    }
}

