using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Application.Configuration;

public enum PrivacyErasureAuthorityTopology
{
    EmbeddedSqlite,
    ExternalDatabase,
    CoLocated
}

public sealed class PrivacyErasureDurabilityOptions
{
    public const string SectionName = "PrivacyErasure:Authority";
    private const string LegacyDurabilityModeKey = "PrivacyErasure:Durability:Mode";
    private const string LegacyDurabilityModeLegacyAlias = "PrivacyErasure__Durability__Mode";
    public PrivacyErasureAuthorityTopology Topology { get; set; } =
        PrivacyErasureAuthorityTopology.EmbeddedSqlite;

    public bool RestoreReplayProtection =>
        Topology != PrivacyErasureAuthorityTopology.CoLocated;

    public static PrivacyErasureDurabilityOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        RejectLegacyDurabilityMode(configuration);
        return new PrivacyErasureDurabilityOptions
        {
            Topology = GetTopology(configuration)
        };
    }

    public static PrivacyErasureAuthorityTopology GetTopology(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string? configuredTopology = configuration[$"{SectionName}:Topology"];
        PrivacyErasureAuthorityTopology topology;
        if (configuredTopology is null
            || string.Equals(
                configuredTopology,
                nameof(PrivacyErasureAuthorityTopology.EmbeddedSqlite),
                StringComparison.OrdinalIgnoreCase))
        {
            topology = PrivacyErasureAuthorityTopology.EmbeddedSqlite;
        }
        else if (string.Equals(
            configuredTopology,
            nameof(PrivacyErasureAuthorityTopology.CoLocated),
            StringComparison.OrdinalIgnoreCase))
        {
            topology = PrivacyErasureAuthorityTopology.CoLocated;
        }
        else if (string.Equals(
            configuredTopology,
            nameof(PrivacyErasureAuthorityTopology.ExternalDatabase),
            StringComparison.OrdinalIgnoreCase))
        {
            topology = PrivacyErasureAuthorityTopology.ExternalDatabase;
        }
        else
        {
            throw InvalidConfiguration(
                $"{SectionName}:Topology must be EmbeddedSqlite, ExternalDatabase, or CoLocated.");
        }

        return topology;
    }

    private static OptionsValidationException InvalidConfiguration(string failure) =>
        new(
            nameof(PrivacyErasureDurabilityOptions),
            typeof(PrivacyErasureDurabilityOptions),
            [failure]);

    private static void RejectLegacyDurabilityMode(IConfiguration configuration)
    {
        bool legacyMode = configuration.GetSection(LegacyDurabilityModeKey).Exists();
        bool legacyModeAlias = configuration.GetSection(LegacyDurabilityModeLegacyAlias).Exists();

        if (legacyMode || legacyModeAlias)
        {
            throw InvalidConfiguration(
                "PrivacyErasure:Durability:Mode was removed. "
                + "Use PrivacyErasure:Authority:Topology (EmbeddedSqlite, CoLocated, ExternalDatabase). "
                + "Existing deployment data and retention metadata are no longer eligible, and operator-led reset-only restore/export/backup is required before changing erasure topologies.");
        }
    }
}
