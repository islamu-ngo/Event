using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.PaidEventPolicies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Settings;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

internal static class ConfigurationManifestApplicationTestSupport
{
    public static ApplyConfigurationManifestCommandHandler CreateHandler(
        ExploreDbContext context,
        IConfigurationManifestPreflight preflight,
        IConfigurationManifestOperationRepository operationRepository,
        IConfigurationManifestFailureRecorder failureRecorder,
        bool useRealPolicyBoundary = false,
        ITenantCreationService? tenantCreationService = null,
        IPublisher? effectPublisher = null,
        ISettingMutationLock? mutationLock = null)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        ISettingMutationLock lockBoundary = mutationLock
            ?? new RelationalSettingMutationLock(context, unitOfWork);
        IPublicationPolicyMutationBoundary policyBoundary = useRealPolicyBoundary
            ? new PublicationPolicyMutationBoundary(
                lockBoundary,
                new CoordinatedSettingMutationRepository(context))
            : Substitute.For<IPublicationPolicyMutationBoundary>();
        if (!useRealPolicyBoundary)
        {
            var success = new PublicationPolicyMutationResult(
                Success: true,
                FailureCode: null,
                Message: "Updated.",
                DeferredNotifications: []);
            policyBoundary.ApplyTenantInCurrentTransactionAsync(
                    Arg.Any<PublicationPolicyTenantMutationRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(success);
            policyBoundary.ApplyInstanceInCurrentTransactionAsync(
                    Arg.Any<PublicationPolicyInstanceMutationRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(success);
        }

        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var typedSettingsDocumentResolver = Substitute.For<ITypedSettingsDocumentResolver>();
        IPublisher publisher = effectPublisher ?? new NullPublisher();
        var effectDispatcher = new ConfigurationManifestEffectDispatcher(
            operationRepository,
            settingsResolver,
            typedSettingsDocumentResolver,
            publisher);
        return new ApplyConfigurationManifestCommandHandler(
            preflight,
            lockBoundary,
            unitOfWork,
            tenantCreationService ?? new TenantCreationService(
                    new TenantRepository(context),
                    new TenantSettingsDocumentRepository(context)),
            policyBoundary,
            new PaidEventPolicyMutationBoundary(
                new PaidEventPolicyRepository(context),
                unitOfWork,
                lockBoundary),
            new ConfigurationManifestInstanceSettingMutationBoundary(
                new SettingUpsertService(
                    new SystemSettingRepository(context, lockBoundary),
                    Substitute.For<IMediator>(),
                    policyBoundary,
                    Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture.CreateEmailSettingsWriter(context, lockBoundary)),
                policyBoundary),
            new ConfigurationManifestTenantSettingMutationBoundary(
                new TenantSettingRepository(context, lockBoundary)),
            operationRepository,
            failureRecorder,
            new ConfigurationManifestEffectDelivery(
                new OutboxRepository(context),
                effectDispatcher),
            NullLogger<ApplyConfigurationManifestCommandHandler>.Instance);
    }

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

    public sealed class ExistencePreflight(
        ITenantRepository tenantRepository,
        IPaidEventPolicyRepository? paidEventPolicies = null,
        int? forcedExpectedPaidPolicyVersion = null)
        : IConfigurationManifestPreflight
    {
        public async Task<ConfigurationManifestPreflightResult> EvaluateAsync(
            ConfigurationManifestApplyPlan plan,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Tenant> existing = await tenantRepository.GetBySlugsAsNoTrackingAsync(
                plan.Tenants.Select(tenant => tenant.Slug).ToArray(),
                cancellationToken);
            IReadOnlyDictionary<string, Tenant> bySlug = existing.ToDictionary(
                tenant => tenant.Slug,
                StringComparer.Ordinal);
            if (plan.Instance.PaidEventPolicy is not null
                && plan.Instance.PaidEventPolicy.ExpectedActivePolicyVersion is null)
            {
                PaidEventPolicyVersion? current = paidEventPolicies is null
                    ? null
                    : await paidEventPolicies.GetActiveInstanceAsync(
                        cancellationToken);
                if (current is null)
                {
                    return new ConfigurationManifestPreflightResult(
                        plan,
                        [],
                        [
                            new ConfigurationManifestPreflightError(
                                ManifestIndex: -1,
                                ConfigurationManifestDocumentKeys
                                    .InstancePaidEventPolicy,
                                ConfigurationManifestApplicationFailureCodes
                                    .PaidPolicyUnavailable,
                                "An active instance paid-event policy is required.")
                        ]);
                }

                plan = plan with
                {
                    Instance = plan.Instance with
                    {
                        PaidEventPolicy =
                            plan.Instance.PaidEventPolicy with
                            {
                                ExpectedActivePolicyVersion =
                                forcedExpectedPaidPolicyVersion
                                ?? current.VersionNumber
                            }
                    }
                };
            }

            return new ConfigurationManifestPreflightResult(
                plan,
                plan.Tenants.Select(tenant => bySlug.TryGetValue(tenant.Slug, out Tenant? found)
                        ? new ConfigurationManifestPreflightTenant(
                            tenant,
                            ConfigurationManifestTenantDisposition.SkippedExisting,
                            found.Id)
                        : new ConfigurationManifestPreflightTenant(
                            tenant,
                            ConfigurationManifestTenantDisposition.Create,
                            tenant.PlannedTenantId))
                    .ToImmutableArray(),
                []);
        }
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

    private sealed class NullPublisher : IPublisher
    {
        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task Publish(
            object notification,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
