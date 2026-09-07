using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;

namespace Event.Application.UnitTests.Features.ConfigurationManifest;

public sealed class ConfigurationManifestCompilerTests
{
    private static readonly Guid OperationId = Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea1");
    private static readonly DateTime OccurredAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Compile_SortsTenantsAndSplitsGuardedSettings()
    {
        ConfigurationManifestV1Alpha2 manifest = CreateManifest(
            CreateTenant(
                "z-community",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [TenantSettingDefinitions.WhiteLabelingEnabled.Key] =
                        ConfigurationManifestTestData.Json("true")
                }),
            CreateTenant(
                "a-community",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [EventSettingDefinitions.RequireApproval.Key] =
                        ConfigurationManifestTestData.Json("true"),
                    [EventSettingDefinitions.UserSubmissionEnabled.Key] =
                        ConfigurationManifestTestData.Json("false")
                }));

        ConfigurationManifestApplyPlan plan = ConfigurationManifestCompiler.Compile(
            ReadResult(manifest),
            OperationId,
            OccurredAt);

        await Assert.That(plan.OperationId).IsEqualTo(OperationId);
        await Assert.That(plan.EffectOutboxId.Version).IsEqualTo(7);
        await Assert.That(plan.Tenants.Select(tenant => tenant.Slug))
            .IsEquivalentTo(["a-community", "z-community"]);
        await Assert.That(plan.Tenants[0].GuardedSettings.Select(setting => setting.Key))
            .IsEquivalentTo(
            [
                EventSettingDefinitions.RequireApproval.Key,
                EventSettingDefinitions.UserSubmissionEnabled.Key
            ]);
        await Assert.That(plan.Tenants[0].UnguardedSettings).IsEmpty();
        await Assert.That(plan.Tenants[1].UnguardedSettings.Select(setting => setting.Key))
            .IsEquivalentTo([TenantSettingDefinitions.WhiteLabelingEnabled.Key]);
        await Assert.That(plan.Tenants.All(tenant => tenant.PlannedTenantId.Version == 7)).IsTrue();
    }

    [Test]
    public async Task Compile_ComposesBaselineAndExplicitBrandingOverlay()
    {
        ConfigurationManifestV1Alpha2 manifest = CreateManifest(
            CreateTenant(
                "primary",
                documents: new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                    StringComparer.Ordinal)
                {
                    [SettingsDocumentKeys.Tenant.Branding] = new()
                    {
                        SchemaVersion = TenantBrandingSettingsDocumentDefaults.SchemaVersion,
                        Payload = ConfigurationManifestTestData.Json(
                            """
                            {
                              "logoUrl": "https://cdn.example.org/logo.svg",
                              "customCssUrl": null
                            }
                            """)
                    }
                }));

        ConfigurationManifestApplyPlan plan = ConfigurationManifestCompiler.Compile(
            ReadResult(manifest),
            OperationId,
            OccurredAt);
        ConfigurationManifestDocumentWrite branding = plan.Tenants[0].BrandingDocument;
        BrandingSettings payload = JsonSerializer.Deserialize<BrandingSettings>(
            branding.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        await Assert.That(payload.DisplayName).IsEqualTo("Primary Community");
        await Assert.That(payload.LogoUrl).IsEqualTo("https://cdn.example.org/logo.svg");
        await Assert.That(payload.FaviconUrl).IsNull();
        await Assert.That(payload.CustomCssUrl).IsNull();
        await Assert.That(branding.SchemaVersion)
            .IsEqualTo(TenantBrandingSettingsDocumentDefaults.SchemaVersion);
        await Assert.That(plan.Tenants[0].ChangedDocumentKeyNames)
            .IsEquivalentTo([SettingsDocumentKeys.Tenant.Branding]);
    }

    [Test]
    public async Task Compile_PreservesCanonicalJsonAndStableProvenance()
    {
        ConfigurationManifestV1Alpha2 manifest = CreateManifest(
            CreateTenant(
                "primary",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [PublicExperienceSettingDefinitions.EventCatalogLabel.Key] =
                        ConfigurationManifestTestData.Json("\"Community Events\"")
                }));

        ConfigurationManifestApplyPlan plan = ConfigurationManifestCompiler.Compile(
            ReadResult(manifest),
            OperationId,
            OccurredAt);

        await Assert.That(plan.Digest).IsEqualTo(new string('a', 64));
        await Assert.That(plan.OccurredAt).IsEqualTo(OccurredAt);
        await Assert.That(plan.Tenants[0].UnguardedSettings[0].JsonValue)
            .IsEqualTo("\"Community Events\"");
        await Assert.That(plan.Tenants[0].ChangedSettingKeyNames)
            .IsEquivalentTo([PublicExperienceSettingDefinitions.EventCatalogLabel.Key]);
    }

    [Test]
    public async Task Compile_RejectsOffModeAndRevalidatesContract()
    {
        ConfigurationManifestReadResult off = ReadResult(
            CreateManifest(CreateTenant("primary")),
            ConfigurationManifestMode.Off);
        await Assert.That(() => ConfigurationManifestCompiler.Compile(off, OperationId, OccurredAt))
            .Throws<ConfigurationManifestCompilationException>();

        ConfigurationManifestV1Alpha2 invalid = CreateManifest(
            CreateTenant(
                "primary",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["analytics.api_key"] = ConfigurationManifestTestData.Json("\"unsafe\"")
                }));
        ConfigurationManifestCompilationException exception = await Assert.That(
                () => ConfigurationManifestCompiler.Compile(
                    ReadResult(invalid),
                    OperationId,
                    OccurredAt))
            .Throws<ConfigurationManifestCompilationException>();

        await Assert.That(exception.FailureCode)
            .IsEqualTo("configuration_manifest_sensitive_key_forbidden");
        await Assert.That(exception.Message).DoesNotContain("unsafe");
    }

    [Test]
    public async Task Compile_ProposedInstancePolicyCreatesUnboundInternalAuthority()
    {
        ConfigurationManifestDocumentV1Alpha2 instancePolicy =
            PaidPolicyDocument(isPaymentsEnabled: true);
        ConfigurationManifestDocumentV1Alpha2 tenantPolicy =
            PaidPolicyDocument(isPaymentsEnabled: false);
        ConfigurationManifestV1Alpha2 manifest = CreateManifest(
            new ConfigurationManifestInstanceV1Alpha2
            {
                Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                Documents =
                    new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal)
                    {
                        [ConfigurationManifestDocumentKeys.InstancePaidEventPolicy] =
                            instancePolicy
                    }
            },
            CreateTenant(
                "primary",
                documents:
                    new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal)
                    {
                        [ConfigurationManifestDocumentKeys.TenantPaidEventPolicy] =
                            tenantPolicy
                    }));

        ConfigurationManifestApplyPlan plan =
            ConfigurationManifestCompiler.Compile(
                ReadResult(manifest),
                OperationId,
                OccurredAt);

        await Assert.That(plan.Instance.PaidEventPolicy).IsNotNull();
        await Assert.That(plan.Instance.PaidEventPolicy!.ProposedRevision)
            .IsNotNull();
        await Assert.That(plan.Instance.PaidEventPolicy.ExpectedActivePolicyVersion)
            .IsNull();
        await Assert.That(plan.Tenants[0].PaidEventPolicy).IsNotNull();
        await Assert.That(plan.Tenants[0].ChangedDocumentKeyNames)
            .Contains(ConfigurationManifestDocumentKeys.TenantPaidEventPolicy);
    }

    [Test]
    public async Task Compile_ApprovedInstanceSettingsProduceTypedPlanAndDeterministicLocks()
    {
        const string instanceKey = "appearance.default_theme_mode";
        ConfigurationManifestV1Alpha2 manifest = CreateManifest(
            new ConfigurationManifestInstanceV1Alpha2
            {
                Settings = new Dictionary<string, JsonElement>(
                    StringComparer.Ordinal)
                {
                    [instanceKey] =
                        ConfigurationManifestTestData.Json("\"system\"")
                },
                Documents =
                    new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal)
            },
            CreateTenant("primary"));

        ConfigurationManifestApplyPlan plan =
            ConfigurationManifestCompiler.Compile(
                ReadResult(manifest),
                OperationId,
                OccurredAt);

        await Assert.That(typeof(ConfigurationManifestInstancePlan))
            .IsNotEqualTo(typeof(ConfigurationManifestTenantPlan));
        await Assert.That(ConfigurationManifestLockKeys.Compile(plan))
            .Contains(instanceKey);
        string[] locks = ConfigurationManifestLockKeys.Compile(plan)
            .ToArray();
        await Assert.That(locks.SequenceEqual(
            locks.Order(StringComparer.Ordinal),
            StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Compile_OmittedFieldsProduceNoResetOrDeletionWrites()
    {
        ConfigurationManifestApplyPlan plan =
            ConfigurationManifestCompiler.Compile(
                ReadResult(CreateManifest(CreateTenant("primary"))),
                OperationId,
                OccurredAt);

        await Assert.That(plan.Tenants[0].GuardedSettings).IsEmpty();
        await Assert.That(plan.Tenants[0].UnguardedSettings).IsEmpty();
        await Assert.That(plan.Tenants[0].ChangedSettingKeyNames).IsEmpty();
        await Assert.That(plan.Tenants[0].ChangedDocumentKeyNames)
            .IsEquivalentTo([SettingsDocumentKeys.Tenant.Branding]);
    }

    [Test]
    public async Task Compile_InstanceSectionDigestIsCanonicalAndTenantIndependent()
    {
        var firstSettings = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal)
        {
            [BrandingSettingDefinitions.DisplayName.Key] =
                ConfigurationManifestTestData.Json("\"ISLAMU\""),
            [AppearanceSettingDefinitions.DefaultThemeMode.Key] =
                ConfigurationManifestTestData.Json("\"system\"")
        };
        var reorderedSettings = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal)
        {
            [AppearanceSettingDefinitions.DefaultThemeMode.Key] =
                ConfigurationManifestTestData.Json("\"system\""),
            [BrandingSettingDefinitions.DisplayName.Key] =
                ConfigurationManifestTestData.Json("\"ISLAMU\"")
        };
        ConfigurationManifestApplyPlan first =
            ConfigurationManifestCompiler.Compile(
                ReadResult(CreateManifest(
                    new ConfigurationManifestInstanceV1Alpha2
                    {
                        Settings = firstSettings,
                        Documents =
                            new Dictionary<
                                string,
                                ConfigurationManifestDocumentV1Alpha2>(
                                StringComparer.Ordinal)
                    },
                    CreateTenant("primary"))),
                OperationId,
                OccurredAt);
        ConfigurationManifestApplyPlan reordered =
            ConfigurationManifestCompiler.Compile(
                ReadResult(CreateManifest(
                    new ConfigurationManifestInstanceV1Alpha2
                    {
                        Settings = reorderedSettings,
                        Documents =
                            new Dictionary<
                                string,
                                ConfigurationManifestDocumentV1Alpha2>(
                                StringComparer.Ordinal)
                    },
                    CreateTenant("secondary"))),
                OperationId,
                OccurredAt);
        reorderedSettings[BrandingSettingDefinitions.DisplayName.Key] =
            ConfigurationManifestTestData.Json("\"Changed\"");
        ConfigurationManifestApplyPlan changed =
            ConfigurationManifestCompiler.Compile(
                ReadResult(CreateManifest(
                    new ConfigurationManifestInstanceV1Alpha2
                    {
                        Settings = reorderedSettings,
                        Documents =
                            new Dictionary<
                                string,
                                ConfigurationManifestDocumentV1Alpha2>(
                                StringComparer.Ordinal)
                    },
                    CreateTenant("secondary"))),
                OperationId,
                OccurredAt);

        await Assert.That(first.InstanceSectionDigest)
            .IsEqualTo(reordered.InstanceSectionDigest);
        await Assert.That(first.InstanceSectionDigest.Length)
            .IsEqualTo(ConfigurationManifestOperation.DigestLength);
        await Assert.That(changed.InstanceSectionDigest)
            .IsNotEqualTo(first.InstanceSectionDigest);
    }

    private static ConfigurationManifestReadResult ReadResult(
        ConfigurationManifestV1Alpha2 manifest,
        ConfigurationManifestMode mode = ConfigurationManifestMode.Bootstrap) =>
        new(manifest, mode, new string('a', 64), ByteLength: 512);

    private static ConfigurationManifestV1Alpha2 CreateManifest(
        params ConfigurationManifestTenantV1Alpha2[] tenants) =>
        CreateManifest(
            new ConfigurationManifestInstanceV1Alpha2
            {
                Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                Documents =
                    new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal)
            },
            tenants);

    private static ConfigurationManifestV1Alpha2 CreateManifest(
        ConfigurationManifestInstanceV1Alpha2 instance,
        params ConfigurationManifestTenantV1Alpha2[] tenants) =>
        new()
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = new ConfigurationManifestMetadataV1Alpha2 { Name = "primary-deployment" },
            Spec = new ConfigurationManifestSpecV1Alpha2
            {
                Instance = instance,
                Tenants = tenants
            }
        };

    private static ConfigurationManifestDocumentV1Alpha2 PaidPolicyDocument(
        bool isPaymentsEnabled) =>
        new()
        {
            SchemaVersion = 1,
            Payload = ConfigurationManifestTestData.Json(
                $$"""
                {
                  "isPaymentsEnabled": {{isPaymentsEnabled.ToString().ToLowerInvariant()}},
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

    private static ConfigurationManifestTenantV1Alpha2 CreateTenant(
        string slug,
        IReadOnlyDictionary<string, JsonElement>? settings = null,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2>? documents = null) =>
        new()
        {
            Metadata = new ConfigurationManifestTenantMetadataV1Alpha2 { Name = slug },
            Spec = new ConfigurationManifestTenantSpecV1Alpha2
            {
                DisplayName = "Primary Community",
                Settings = settings ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                Documents = documents
                    ?? new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal)
            }
        };
}
