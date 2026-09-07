using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Explore.Infrastructure.Messaging;

internal static class RabbitMqEmailDispatchConnectionFactory
{
    public static async Task<IConnection> CreateConnectionAsync(
        IConfiguration configuration,
        EmailDispatchRabbitMqSettings options,
        string clientProvidedName,
        CancellationToken cancellationToken)
    {
        string connectionString = ResolveConnectionString(configuration, options);
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString, UriKind.Absolute),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = clientProvidedName
        };

        return await factory.CreateConnectionAsync(cancellationToken);
    }

    public static string ResolveConnectionString(
        IConfiguration configuration,
        EmailDispatchRabbitMqSettings options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return options.ConnectionString;
        }

        string? connectionString = configuration.GetConnectionString(options.ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string environmentKey = $"{NormalizeEnvironmentName(options.ConnectionStringName)}_URI";
        connectionString = configuration[environmentKey];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        throw new InvalidOperationException(
            $"RabbitMQ Dispatch Mode is enabled but no connection string was found for '{options.ConnectionStringName}'.");
    }

    private static string NormalizeEnvironmentName(string value) =>
        value.Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
}
