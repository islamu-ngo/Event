using System.Security.Cryptography;
using System.Text.Json;

namespace Explore.Secrets.Database;

/// <summary>Canonical public database coordinates; credentials and hashes of credentials are never included.</summary>
public static class PrimaryDatabasePublicIdentity
{
    public static string Digest(PrimaryDatabaseConnectionOptions options) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "moderation-database-v1",
            provider = options.Provider.ToString(),
            role = options.Role.ToString(),
            options.Host,
            options.Port,
            options.Database,
            options.Schema,
            tlsMode = options.TlsMode.ToString(),
            options.TrustServerCertificate,
            serverFlavor = options.ServerFlavor?.ToString(),
            serverVersion = options.ServerVersion?.ToString()
        })));
}
