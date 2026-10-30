using System.Security.Cryptography;

namespace Event.Persistence.IntegrationTests.Database;

internal static class SecretsTestValues
{
    internal static string CreateSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
