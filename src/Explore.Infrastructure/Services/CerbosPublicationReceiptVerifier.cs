using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Explore.Infrastructure.Services;

/// <summary>
/// A signed operator statement about a completed publication, not a measurement of the live PDP.
/// The signing key belongs to the operator and is never supplied to Event or its deployment runner.
/// </summary>
public sealed record CerbosPublicationReceipt(
    string SchemaVersion,
    string Revision,
    string ContentHash,
    string Environment,
    string PdpEndpoint,
    DateTimeOffset PublishedAt,
    DateTimeOffset ExpiresAt,
    bool Succeeded);

public sealed class CerbosPublicationReceiptVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    /// <summary>
    /// Verifies the exact supplied bytes against an independently configured public key.
    /// Expected deployment values must not be taken from the receipt.
    /// </summary>
    public bool Verify(
        ReadOnlySpan<byte> document,
        ReadOnlySpan<byte> signature,
        string publicKeyPem,
        string revision,
        string contentHash,
        string environment,
        string pdpEndpoint,
        DateTimeOffset now)
    {
        if (document.Length is 0 or > 8192 || signature.Length is 0 or > 1024
            || string.IsNullOrWhiteSpace(publicKeyPem)
            || publicKeyPem.Length > 16384
            || !publicKeyPem.Trim().StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal)
            || !publicKeyPem.Trim().EndsWith("-----END PUBLIC KEY-----", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(contentHash)
            || string.IsNullOrWhiteSpace(environment) || string.IsNullOrWhiteSpace(pdpEndpoint))
            return false;

        try
        {
            using var key = RSA.Create();
            key.ImportFromPem(publicKeyPem);
            if (key.KeySize < 3072
                || !key.VerifyData(document, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                return false;

            using var parsed = JsonDocument.Parse(document.ToArray());
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in parsed.RootElement.EnumerateObject())
                if (!names.Add(property.Name))
                    return false;

            var receipt = JsonSerializer.Deserialize<CerbosPublicationReceipt>(document, JsonOptions);
            return receipt is not null
                && receipt.SchemaVersion == "cerbos-publication.v1"
                && receipt.Succeeded
                && receipt.Revision == revision
                && receipt.ContentHash == contentHash
                && receipt.Environment == environment
                && receipt.PdpEndpoint == pdpEndpoint
                && receipt.PublishedAt <= now
                && receipt.ExpiresAt > now
                && receipt.ExpiresAt > receipt.PublishedAt
                && receipt.ExpiresAt - receipt.PublishedAt <= TimeSpan.FromHours(24);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or JsonException)
        {
            return false;
        }
    }
}
