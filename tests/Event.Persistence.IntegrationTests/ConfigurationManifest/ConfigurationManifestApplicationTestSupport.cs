using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Notifications;
using Microsoft.EntityFrameworkCore;
using Event.Persistence.IntegrationTests.Fixtures;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

internal static class ConfigurationManifestApplicationTestSupport
{
    public static ConfigurationManifestBootstrapTestHost CreateHandler(
        TestDbContextFactory factory, INotificationHandler<SettingChangedNotification>? effectConsumer = null) =>
        ConfigurationManifestBootstrapTestHost.ForSqlite(factory.CreateDbContext, effectConsumer);

    public static ConfigurationManifestBootstrapTestHost CreateHandler(
        PostgreSqlContainerFixture fixture) =>
        ConfigurationManifestBootstrapTestHost.ForPostgres(fixture);

    public static ConfigurationManifestBootstrapTestHost CreateHandler(
        EventResourcePersistenceTests.TestDatabase database) =>
        ConfigurationManifestBootstrapTestHost.ForSqlite(database.CreateIndependentContext);

    public static ConfigurationManifestReadResult Source(params string[] slugs)
        => CreateSource(
            includeGuardedSetting: false,
            new string('c', ConfigurationManifestOperation.DigestLength),
            "Community Events",
            includePaidPolicy: false,
            includeInstanceState: false,
            slugs);

    public static ConfigurationManifestReadResult GuardedSource(params string[] slugs)
        => CreateSource(
            includeGuardedSetting: true,
            new string('c', ConfigurationManifestOperation.DigestLength),
            "Community Events",
            includePaidPolicy: false,
            includeInstanceState: false,
            slugs);

    public static ConfigurationManifestReadResult DifferentDigestSource(
        string digest,
        string catalogLabel,
        params string[] slugs) =>
        CreateSource(
            includeGuardedSetting: false,
            digest,
            catalogLabel,
            includePaidPolicy: false,
            includeInstanceState: false,
            slugs);

    public static ConfigurationManifestReadResult PaidPolicySource(
        string digest,
        params string[] slugs) =>
        CreateSource(
            includeGuardedSetting: false,
            digest,
            "Community Events",
            includePaidPolicy: true,
            includeInstanceState: false,
            slugs);

    public static ConfigurationManifestReadResult FullAuthoritySource(
        string digest,
        params string[] slugs) =>
        CreateSource(
            includeGuardedSetting: true,
            digest,
            "Community Events",
            includePaidPolicy: true,
            includeInstanceState: true,
            slugs);

    private static ConfigurationManifestReadResult CreateSource(
        bool includeGuardedSetting,
        string digest,
        string catalogLabel,
        bool includePaidPolicy,
        bool includeInstanceState,
        params string[] slugs)
    {
        ConfigurationManifestTenantV1Alpha2[] tenants = slugs
            .Select(slug =>
            {
                var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [PublicExperienceSettingDefinitions.EventCatalogLabel.Key] =
                        Json(JsonSerializer.Serialize(catalogLabel))
                };
                if (includeGuardedSetting)
                {
                    settings[EventSettingDefinitions.RequireApproval.Key] = Json("true");
                }

                var documents =
                    new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal);
                if (includePaidPolicy)
                {
                    documents[ConfigurationManifestDocumentKeys.TenantPaidEventPolicy] =
                        new ConfigurationManifestDocumentV1Alpha2
                        {
                            SchemaVersion = 1,
                            Payload = Json(
                                """
                                {
                                  "isPaymentsEnabled": false,
                                  "allowedOrganizerKindIds": [2],
                                  "requiresLocalVerification": false,
                                  "allowedCurrencyCodes": ["USD"],
                                  "defaultCurrencyCode": "USD",
                                  "refundProtectionIds": [1, 2, 3, 4, 5, 6, 7],
                                  "currencyRiskLimits": [],
                                  "requiresFirstPaidEventReview": false,
                                  "farFutureReviewThresholdDays": null
                                }
                                """)
                        };
                }

                return new ConfigurationManifestTenantV1Alpha2
                {
                    Metadata = new ConfigurationManifestTenantMetadataV1Alpha2
                    {
                        Name = slug
                    },
                    Spec = new ConfigurationManifestTenantSpecV1Alpha2
                    {
                        DisplayName = $"{slug} community",
                        Settings = settings,
                        Documents = documents
                    }
                };
            })
            .ToArray();
        var manifest = new ConfigurationManifestV1Alpha2
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = new ConfigurationManifestMetadataV1Alpha2
            {
                Name = "deployment"
            },
            Spec = new ConfigurationManifestSpecV1Alpha2
            {
                Instance = new ConfigurationManifestInstanceV1Alpha2
                {
                    Settings = includeInstanceState
                        ? new Dictionary<string, JsonElement>(
                            StringComparer.Ordinal)
                        {
                            [AppearanceSettingDefinitions.DefaultThemeMode.Key] =
                                Json("\"dark\"")
                        }
                        : new Dictionary<string, JsonElement>(
                            StringComparer.Ordinal),
                    Documents = includeInstanceState
                        ? new Dictionary<
                            string,
                            ConfigurationManifestDocumentV1Alpha2>(
                            StringComparer.Ordinal)
                        {
                            [ConfigurationManifestDocumentKeys
                                .InstancePaidEventPolicy] = new()
                                {
                                    SchemaVersion = 1,
                                    Payload = Json(
                                    """
                                    {
                                      "isPaymentsEnabled": false,
                                      "allowedOrganizerKindIds": [2],
                                      "requiresLocalVerification": false,
                                      "allowedCurrencyCodes": ["USD"],
                                      "defaultCurrencyCode": "USD",
                                      "refundProtectionIds": [1, 2, 3, 4, 5, 6, 7],
                                      "currencyRiskLimits": [],
                                      "requiresFirstPaidEventReview": false,
                                      "farFutureReviewThresholdDays": null
                                    }
                                    """)
                                }
                        }
                        : new Dictionary<
                            string,
                            ConfigurationManifestDocumentV1Alpha2>(
                            StringComparer.Ordinal)
                },
                Tenants = tenants
            }
        };
        return new ConfigurationManifestReadResult(
            manifest,
            ConfigurationManifestMode.Bootstrap,
            digest,
            ByteLength: 512);
    }

    public sealed class TestDbContextFactory(DbContextOptions<ExploreDbContext> options)
        : IDbContextFactory<ExploreDbContext>
    {
        public ExploreDbContext CreateDbContext()
        {
            var context = new ExploreDbContext(options);
            context.EnableTenantFilterBypass("Configuration manifest application persistence test.");
            return context;
        }
    }

    private static JsonElement Json(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

}
