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
    public async Task InstanceSettings_UnchangedPolicyDoesNotRequireADeferredNotification()
    {
        var policy = Substitute.For<IPublicationPolicyMutationBoundary>();
        policy.ApplyInstanceInCurrentTransactionAsync(
                Arg.Any<PublicationPolicyInstanceMutationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new PublicationPolicyMutationResult(true, null, "Unchanged.", []));
        var scalar = Substitute.For<ISystemSettingRepository>();
        var boundary = new ConfigurationManifestInstanceSettingMutationBoundary(scalar, policy);

        var result = await boundary.ApplyInCurrentTransactionAsync(
            new ConfigurationManifestInstanceSettingMutationInput(
                [new(EventSettingDefinitions.RequireApproval.Key, "false")],
                Guid.CreateVersion7(), DateTime.UtcNow));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.DeferredNotifications).IsEmpty();
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
