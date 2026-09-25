namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain.Settings.Definitions;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using NSubstitute;

public sealed class ConfigurationManifestEventResourceRoutingTests
{
    [Test]
    public async Task Import_RoutesResourceSettingsOnlyThroughCoordinatedWriter()
    {
        var ordinaryValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var resourceValues = new Dictionary<string, string>(StringComparer.Ordinal);
        IConfigurationManifestInstanceSettingMutationBoundary instanceSettings =
            Substitute.For<IConfigurationManifestInstanceSettingMutationBoundary>();
        instanceSettings.ApplyInCurrentTransactionAsync(
                Arg.Any<ConfigurationManifestInstanceSettingMutationInput>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                foreach (var mutation in call.Arg<ConfigurationManifestInstanceSettingMutationInput>().Mutations)
                {
                    EventResourceSettingMutationGuard.RejectGenericMutation(mutation.Key);
                    ordinaryValues[mutation.Key] = mutation.SerializedValue;
                }
                return new ConfigurationManifestInstanceSettingMutationResult(true, null, "Applied.", []);
            });
        IEventResourceSettingsWriter resourceWriter =
            Substitute.For<IEventResourceSettingsWriter>();
        var notification = new SettingChangedNotification(
            EventResourceSettingDefinitions.MaxActiveResources.Key,
            "500",
            "100",
            SettingSource.SystemDefault,
            tenantId: null,
            actorUserId: Guid.CreateVersion7(),
            DateTime.UtcNow);
        resourceWriter.ApplyAsync(
                Arg.Any<ImmutableArray<EventResourceSettingMutation>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                foreach (var mutation in call.Arg<ImmutableArray<EventResourceSettingMutation>>())
                {
                    if (mutation.TenantId.HasValue || !EventResourceSettingMutationGuard.Handles(mutation.Key)
                        || mutation.Kind != EventResourceSettingMutationKind.SetValue || mutation.Value is null)
                        return new EventResourceSettingsWriteResult(false, "event_resource_policy_invalid", []);
                    resourceValues[mutation.Key] = mutation.Value;
                }
                return new EventResourceSettingsWriteResult(true, null, [notification]);
            });
        ConfigurationImportSectionApplier applier = CreateApplier(
            instanceSettings,
            resourceWriter);
        ConfigurationManifestV1Alpha2 manifest = ConfigurationManifestTestData.Valid(
            instanceSettings: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [EventResourceSettingDefinitions.MaxActiveResources.Key] =
                    ConfigurationManifestTestData.Json("100"),
                [AppearanceSettingDefinitions.DefaultThemeMode.Key] =
                    ConfigurationManifestTestData.Json("\"system\"")
            });
        byte[] artifact = ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(
            manifest);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(StringComparer.Ordinal),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };

        ImmutableArray<SettingChangedNotification> deferred = await applier.ApplyAsync(
            ConfigurationImportTarget.ForInstance(),
            artifact,
            request,
            Guid.CreateVersion7(),
            DateTime.UtcNow,
            new string('a', 64),
            new ConfigurationImportArtifactParser(),
            CancellationToken.None);

        await Assert.That(EventResourceGovernancePolicyValues.Parse(resourceValues, long.MaxValue).MaxActiveResources)
            .IsEqualTo(100);
        await Assert.That(JsonSerializer.Deserialize<string>(ordinaryValues[AppearanceSettingDefinitions.DefaultThemeMode.Key]))
            .IsEqualTo("system");
        await Assert.That(deferred).IsEquivalentTo([notification]);
    }

    [Test]
    public async Task Import_ResourceMutationLocksTheCompleteFamily()
    {
        ConfigurationImportSectionApplier applier = CreateApplier(
            Substitute.For<IConfigurationManifestInstanceSettingMutationBoundary>(),
            Substitute.For<IEventResourceSettingsWriter>());
        ConfigurationManifestV1Alpha2 manifest = ConfigurationManifestTestData.Valid(
            instanceSettings: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [EventResourceSettingDefinitions.MaxActiveResources.Key] =
                    ConfigurationManifestTestData.Json("100")
            });
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(StringComparer.Ordinal),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };

        IReadOnlyList<IReadOnlyList<string>> groups =
            await applier.CompileLockGroupsAsync(
                ConfigurationImportTarget.ForInstance(),
                ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest),
                request,
                new ConfigurationImportArtifactParser(),
                CancellationToken.None);

        string[] locks = groups.SelectMany(group => group).ToArray();
        await Assert.That(EventResourceSettingMutationGuard.Keys.All(key =>
            locks.Contains(key, StringComparer.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ImportOutbox_PreservesDeferredNotificationsForPostCommitDelivery()
    {
        var notification = new SettingChangedNotification(
            EventResourceSettingDefinitions.MaxActiveResources.Key,
            "500",
            "100",
            SettingSource.SystemDefault,
            tenantId: null,
            actorUserId: Guid.CreateVersion7(),
            DateTime.UtcNow);

        var message = ConfigurationImportEffectOutbox.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTime.UtcNow,
            [notification]);
        ImmutableArray<SettingChangedNotification> restored =
            ConfigurationImportEffectOutbox.ReadNotifications(message.Payload);

        await Assert.That(restored).IsEquivalentTo([notification]);
    }

    private static ConfigurationImportSectionApplier CreateApplier(
        IConfigurationManifestInstanceSettingMutationBoundary instanceSettings,
        IEventResourceSettingsWriter resourceWriter) => new(
        instanceSettings,
        Substitute.For<IConfigurationManifestTenantSettingMutationBoundary>(),
        Substitute.For<IConfigurationImportTenantIdentityMutationBoundary>(),
        Substitute.For<IPublicationPolicyMutationBoundary>(),
        resourceWriter,
        Substitute.For<IPaidEventPolicyMutationBoundary>(),
        Substitute.For<IPaidEventPolicyRepository>(),
        Substitute.For<ITenantRepository>(),
        Substitute.For<ITenantSettingsDocumentRepository>(),
        Substitute.For<ILegalDocumentRepository>());
}
