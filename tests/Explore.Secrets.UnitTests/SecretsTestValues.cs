using System.Security.Cryptography;
using Npgsql;

namespace Explore.Secrets.UnitTests;

internal static class SecretsTestValues
{
    internal static readonly DateTimeOffset UtcNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    internal static string CreateSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    internal static string CreateConnectionString(string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "test",
            Username = "test-user",
            Password = password,
        }.ConnectionString;
}

internal sealed class SecretsFixedTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() =>
        SecretsTestValues.UtcNow;
}
