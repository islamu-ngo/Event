using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Registration;

public sealed class PromotionCodeDigestService(
    ISecretResolver secretResolver,
    IOptions<PromotionCodeLookupOptions> options) : IPromotionCodeDigestService
{
    private const int MinimumKeyBytes = 32;

    public string NormalizeCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return code.Trim().ToUpperInvariant();
    }

    public Task<PromotionCodeDigest> ComputeActiveAsync(
        Guid tenantId,
        Guid eventId,
        string code,
        CancellationToken cancellationToken = default)
    {
        int version = options.Value.ActiveKeyVersion;
        if (version < 1)
        {
            throw new InvalidOperationException("Promotion code lookup key version is not configured.");
        }

        return ComputeAsync(tenantId, eventId, code, version, cancellationToken);
    }

    public async Task<IReadOnlyList<PromotionCodeDigest>> ComputeCandidatesAsync(
        Guid tenantId,
        Guid eventId,
        string code,
        IReadOnlyCollection<int> persistedKeyVersions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persistedKeyVersions);

        if (persistedKeyVersions.Any(version => version < 1))
        {
            throw new InvalidOperationException("Promotion code lookup key version is invalid.");
        }

        var digests = new List<PromotionCodeDigest>();
        foreach (int version in persistedKeyVersions.Distinct().Order())
        {
            digests.Add(await ComputeAsync(tenantId, eventId, code, version, cancellationToken).ConfigureAwait(false));
        }

        return digests;
    }

    public bool Matches(string candidateDigest, string expectedDigest)
    {
        if (string.IsNullOrWhiteSpace(candidateDigest) || string.IsNullOrWhiteSpace(expectedDigest))
        {
            return false;
        }

        byte[] candidate;
        byte[] expected;
        try
        {
            candidate = Convert.FromBase64String(candidateDigest);
            expected = Convert.FromBase64String(expectedDigest);
        }
        catch (FormatException)
        {
            return false;
        }

        return candidate.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(candidate, expected);
    }

    private async Task<PromotionCodeDigest> ComputeAsync(
        Guid tenantId,
        Guid eventId,
        string code,
        int keyVersion,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and event identifiers are required.");
        }

        SecretResolutionResult secret = await secretResolver.ResolveQualifiedAsync(
            SecretDefinitionRegistry.Keys.Promotions.CodeLookupHmacKey,
            SecretScope.Instance,
            scopeId: null,
            QualifierForVersion(keyVersion),
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(secret?.Value))
        {
            throw new InvalidOperationException("Promotion code lookup key is unavailable.");
        }

        byte[] key = DecodeKey(secret.Value);
        string material = string.Create(
            CultureInfo.InvariantCulture,
            $"{tenantId:N}:{eventId:N}:{NormalizeCode(code)}");
        byte[] digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(material));
        return new PromotionCodeDigest(keyVersion, Convert.ToBase64String(digest));
    }

    internal static string QualifierForVersion(int version)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Promotion code lookup key version must be positive.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"v{version}");
    }

    private static byte[] DecodeKey(string value)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Promotion code lookup key is invalid.", exception);
        }

        return key.Length >= MinimumKeyBytes
            ? key
            : throw new InvalidOperationException("Promotion code lookup key is invalid.");
    }
}
