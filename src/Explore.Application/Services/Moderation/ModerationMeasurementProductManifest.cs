using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Domain;

namespace Explore.Application.Services.Moderation;

/// <summary>Canonical product-byte recipe shared by actual deployment observation and the owned launcher.</summary>
public static class ModerationMeasurementProductManifest
{
    public static ImmutableArray<string> AssemblyNames { get; } =
    [
        "Explore.API", "Explore.Application", "Explore.Domain", "Explore.Persistence",
        "Explore.Infrastructure", "Explore.ServiceDefaults", "ISLAMU.Moderation.Application", "ISLAMU.Moderation.Domain"
    ];

    public static string Digest(ImmutableArray<ModerationMeasurementProductArtifact> products)
    {
        if (products.IsDefault || products.Length != AssemblyNames.Length
            || products.Where((product, index) => product is null || product.AssemblyName != AssemblyNames[index]
                || !ModerationMeasurementCatalogue.IsDigest(product.Digest)).Any())
            throw new ArgumentException("A product manifest requires the complete ordered native product-byte recipe.", nameof(products));
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "moderation-products-v1",
            products = products.Select(product => new { assembly = product.AssemblyName, sha256 = product.Digest })
        })));
    }

    /// <summary>
    /// Binds the consumer to pinned native product bytes, including the concrete owner mapping
    /// compiled into the API. Deployment still separately verifies actual service composition.
    /// </summary>
    public static string RuntimeGeneration(string consumerId, ModerationNativeMode mode,
        string inventoryFingerprint, string productArtifactDigest)
    {
        if (!ModerationConsumerInstallation.IsConsumerId(consumerId) || !Enum.IsDefined(mode)
            || !ModerationMeasurementCatalogue.IsDigest(inventoryFingerprint)
            || !ModerationMeasurementCatalogue.IsDigest(productArtifactDigest))
            throw new ArgumentException("Runtime generation requires a native consumer, mode and exact pinned artifacts.");
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "moderation-consumer-runtime-v1",
            consumerId,
            mode,
            inventoryFingerprint,
            productArtifactDigest
        })));
    }
}

public sealed record ModerationMeasurementProductArtifact(string AssemblyName, string Digest);
