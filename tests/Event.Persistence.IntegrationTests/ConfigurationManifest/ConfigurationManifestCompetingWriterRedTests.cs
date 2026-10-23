namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.Tenants;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationManifestCompetingWriterRedTests(
    PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task Bootstrap_HoldsEverySharedAuthorityBeforeSerializableTransaction()
    {
        await fixture.ResetAsync();
        await SeedInstancePolicyAsync();
        await using ExploreDbContext manifestContext = fixture.CreateDbContext();
        await using ExploreDbContext competitorContext = fixture.CreateDbContext();
        var lockEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acquiredOrder = new List<string>();
        bool transactionDuringLockAcquisition = false;
        bool transactionDuringObservation = false;
        IsolationLevel observedIsolation = IsolationLevel.Unspecified;
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);
        handler.BeforeLock = (key, actualOwner, _) =>
        {
            acquiredOrder.Add(key);
            transactionDuringLockAcquisition |= actualOwner.Database.CurrentTransaction is not null;
            return Task.CompletedTask;
        };
        handler.AfterObservation = async (actualOwner, _, token) =>
        {
            transactionDuringObservation = actualOwner.Database.CurrentTransaction is not null;
            observedIsolation = actualOwner.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel;
            lockEntered.TrySetResult();
            await releaseLock.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        };

        Task<Explore.Application.Responses.BaseCommandResponse<Guid>>
            manifestTask = handler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport
                        .FullAuthoritySource(
                            new string(
                                'd',
                                ConfigurationManifestOperation
                                    .DigestLength),
                            "shared-authority")),
                CancellationToken.None);
        await lockEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        ImmutableArray<string> captured = acquiredOrder.ToImmutableArray();
        string tenantPaidPolicyKey = captured.Single(key =>
            key.StartsWith(
                "paid-event-policy:tenant:",
                StringComparison.Ordinal));
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            ConfigurationManifestLockKeys.InstanceManifest,
            AppearanceSettingDefinitions.DefaultThemeMode.Key,
            PaidEventPolicyMutationLockKeys.Instance,
            PaidEventPolicyMutationLockKeys.ForTenant(
                Guid.Parse(tenantPaidPolicyKey[
                    "paid-event-policy:tenant:".Length..])),
            TenantMutationLockKeys.ForSlug("shared-authority"),
            PublicExperienceSettingDefinitions.EventCatalogLabel.Key,
            EventSettingDefinitions.RequireApproval.Key
        };
        expected.UnionWith(PublicationPolicySettingKeys.All);
        expected.UnionWith(TenantBrandingGovernanceMutationLockKeys.All);
        var blocked = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            foreach (string key in expected.Order(StringComparer.Ordinal))
            {
                blocked[key] =
                    !await TryAcquireCompetingLockAsync(
                        competitorContext,
                        key);
            }
        }
        finally
        {
            releaseLock.TrySetResult();
        }

        Explore.Application.Responses.BaseCommandResponse<Guid> result =
            await manifestTask.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(transactionDuringLockAcquisition).IsFalse();
        await Assert.That(transactionDuringObservation)
            .IsTrue();
        await Assert.That(observedIsolation)
            .IsEqualTo(IsolationLevel.Serializable);
        await Assert.That(captured.ToHashSet(StringComparer.Ordinal)
            .SetEquals(expected)).IsTrue();
        await Assert.That(blocked.Values.All(value => value)).IsTrue();

        string[] instanceResources =
        [
            AppearanceSettingDefinitions.DefaultThemeMode.Key,
            PaidEventPolicyMutationLockKeys.Instance,
            .. TenantBrandingGovernanceMutationLockKeys.All,
            .. PublicationPolicySettingKeys.All
        ];
        string[] tenantResources =
        [
            PublicExperienceSettingDefinitions.EventCatalogLabel.Key,
            TenantMutationLockKeys.ForSlug("shared-authority"),
            tenantPaidPolicyKey
        ];
        int latestInstanceLock = instanceResources
            .Max(key => captured.IndexOf(key));
        int earliestTenantLock = tenantResources
            .Min(key => captured.IndexOf(key));
        await Assert.That(captured[0])
            .IsEqualTo(ConfigurationManifestLockKeys.InstanceManifest);
        await Assert.That(latestInstanceLock < earliestTenantLock).IsTrue();
    }

    [Test]
    public async Task Bootstrap_CommitsInstanceAndTenantStateAsOneUnit()
    {
        await fixture.ResetAsync();
        await SeedInstancePolicyAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        await using var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(fixture);

        Explore.Application.Responses.BaseCommandResponse<Guid> result =
            await handler.ExecuteAsync(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport
                        .FullAuthoritySource(
                            new string(
                                'e',
                                ConfigurationManifestOperation
                                    .DigestLength),
                            "whole-state")),
                CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await using ExploreDbContext verification = fixture.CreateDbContext();
        SystemSetting? instanceSetting = await verification.SystemSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(setting =>
                setting.SettingKey
                    == AppearanceSettingDefinitions.DefaultThemeMode.Key);
        Tenant? tenant = await verification.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Slug == "whole-state");
        await Assert.That(instanceSetting).IsNotNull();
        await Assert.That(instanceSetting?.Value).IsEqualTo("\"dark\"");
        await Assert.That(tenant).IsNotNull();
        if (tenant is null)
        {
            return;
        }

        await Assert.That(await verification.TenantSettingOverrides
                .IgnoreQueryFilters()
                .AnyAsync(setting =>
                    setting.TenantId == tenant.Id
                    && setting.SettingKey
                        == PublicExperienceSettingDefinitions
                            .EventCatalogLabel.Key))
            .IsTrue();
        await Assert.That(await verification.TenantSettingsDocuments
                .IgnoreQueryFilters()
                .AnyAsync(document => document.TenantId == tenant.Id))
            .IsTrue();
        await Assert.That(await verification.PaidEventPolicyVersions
                .IgnoreQueryFilters()
                .CountAsync(policy =>
                    policy.TenantId == tenant.Id
                    && policy.IsActive))
            .IsEqualTo(1);
        PaidEventPolicyVersion instancePolicy = await verification
            .PaidEventPolicyVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(policy =>
                policy.TenantId == null
                && policy.IsActive);
        await Assert.That(instancePolicy.VersionNumber).IsEqualTo(2);
        ConfigurationManifestOperation operation = await verification
            .ConfigurationManifestOperations
            .AsNoTracking()
            .SingleAsync(candidate =>
                candidate.Status
                    == ConfigurationManifestOperationStatus.Applied);
        await Assert.That(operation.InstanceSectionDigest?.Length)
            .IsEqualTo(ConfigurationManifestOperation.DigestLength);
        await Assert.That(operation.BootstrapGeneration).IsEqualTo(1);
        await Assert.That(operation.InstanceChangedSettingKeyNames)
            .Contains(AppearanceSettingDefinitions.DefaultThemeMode.Key);
        await Assert.That(operation.InstanceChangedDocumentKeyNames)
            .Contains(
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy);
        await Assert.That(await verification.OutboxMessages.CountAsync(message =>
                message.AggregateId == operation.Id
                && message.Status == OutboxMessageStatus.Completed))
            .IsEqualTo(1);
    }

    private async Task SeedInstancePolicyAsync()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        await new PaidEventPolicyRepository(context).AddAsync(
            PaidEventPolicyVersion.CreateDefaultInstance(),
            CancellationToken.None);
        await context.SaveChangesAsync();
    }

    internal static async Task<bool> TryAcquireCompetingLockAsync(
        ExploreDbContext context,
        string normalizedKey)
    {
        await context.Database.OpenConnectionAsync();
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
        await using DbCommand command =
            context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT pg_try_advisory_xact_lock(@lock_key)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value =
            RelationalSettingMutationLock.ComputeStableLockKey(normalizedKey);
        command.Parameters.Add(parameter);
        object? value = await command.ExecuteScalarAsync();
        await transaction.RollbackAsync();
        return value is true;
    }

}
