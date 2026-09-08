using System.Security.Cryptography;
using System.Text;

namespace Explore.Application.Services;

public static class ApiKeyHashing
{
    public static string CreateKeyId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    public static string CreateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string FormatPersistedApiKey(string keyId, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return $"{keyId}.{secret}";
    }

    public static string ComputeHash(string rawApiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawApiKey);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawApiKey));
        return Convert.ToBase64String(bytes);
    }

    public static bool MatchesHash(string rawApiKey, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(rawApiKey) || string.IsNullOrWhiteSpace(expectedHash))
        {
            return false;
        }

        try
        {
            var actualBytes = Convert.FromBase64String(ComputeHash(rawApiKey));
            var expectedBytes = Convert.FromBase64String(expectedHash);
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool TryParsePersistedApiKey(string rawApiKey, out string keyId, out string secret)
    {
        keyId = string.Empty;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(rawApiKey))
        {
            return false;
        }

        var separatorIndex = rawApiKey.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == rawApiKey.Length - 1)
        {
            return false;
        }

        keyId = rawApiKey[..separatorIndex].Trim();
        secret = rawApiKey[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(secret);
    }
}
