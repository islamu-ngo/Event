
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema.ProviderPrimitives;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryPolicyRevisionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisableThenEnable_AdvancesDurableRevisionAndMonotoneSuppression(bool tenantPolicy)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedScenarioAsync(databasePath);
            await using var writer = CreateContext(databasePath);
            await using var observer = CreateContext(databasePath);
            var before = await ReadPolicyAsync(observer, tenantPolicy ? scenario.TenantId : null);
            foreach (bool enabled in new[] { false, true })
            {
                DateTime lower = await RelationalDatabaseClock.GetUtcNowAsync(observer, CancellationToken.None);
                await SetEnabledAsync(writer, scenario, tenantPolicy, enabled);
                DateTime upper = await RelationalDatabaseClock.GetUtcNowAsync(observer, CancellationToken.None);
                var after = await ReadPolicyAsync(observer, tenantPolicy ? scenario.TenantId : null);

                await Assert.That(after.Revision).IsEqualTo(before.Revision + 1);
                await Assert.That(after.SuppressedThroughRevision).IsEqualTo(enabled ? before.Revision : after.Revision);
                await Assert.That(after.Cutoff).IsNotNull();
                await Assert.That(after.Cutoff!.Value).IsGreaterThanOrEqualTo(lower);
                await Assert.That(after.Cutoff.Value).IsLessThanOrEqualTo(upper);
                if (before.Cutoff.HasValue)
                    await Assert.That(after.Cutoff.Value).IsGreaterThanOrEqualTo(before.Cutoff.Value);
                await AssertOperatorStatePreservedAsync(observer, scenario);
                before = after;
            }
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisabledEditsAndReenable_PreserveTheExactSuppressedRevisionBoundary(bool tenantPolicy)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedScenarioAsync(databasePath);
            await using var writer = CreateContext(databasePath);
            await using var observer = CreateContext(databasePath);
            Guid? tenantId = tenantPolicy ? scenario.TenantId : null;
            await SetEnabledAsync(writer, scenario, tenantPolicy, enabled: false);
            var disabled = await ReadPolicyAsync(observer, tenantId);
            await Assert.That(disabled.SuppressedThroughRevision).IsEqualTo(disabled.Revision);

            await SetSenderNameAsync(writer, scenario, tenantPolicy, "\"Disabled sender edit\"");
            var editedDisabled = await ReadPolicyAsync(observer, tenantId);
            await Assert.That(editedDisabled.Revision).IsEqualTo(disabled.Revision + 1);
            await Assert.That(editedDisabled.SuppressedThroughRevision).IsEqualTo(editedDisabled.Revision);

            await SetEnabledAsync(writer, scenario, tenantPolicy, enabled: true);
            var reenabled = await ReadPolicyAsync(observer, tenantId);
            await Assert.That(reenabled.Revision).IsEqualTo(editedDisabled.Revision + 1);
            await Assert.That(reenabled.SuppressedThroughRevision).IsEqualTo(editedDisabled.Revision);

            await SetSenderNameAsync(writer, scenario, tenantPolicy, "\"Enabled sender edit\"");
            var editedEnabled = await ReadPolicyAsync(observer, tenantId);
            await Assert.That(editedEnabled.Revision).IsEqualTo(reenabled.Revision + 1);
            await Assert.That(editedEnabled.SuppressedThroughRevision).IsEqualTo(reenabled.SuppressedThroughRevision);
            await Assert.That(editedEnabled.Cutoff).IsEqualTo(reenabled.Cutoff);
            await AssertOperatorStatePreservedAsync(observer, scenario);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task TenantMutation_AdvancesOnlyExactTenantRevision()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedScenarioAsync(databasePath);
            await using var writer = CreateContext(databasePath);
            await using var observer = CreateContext(databasePath);
            var instanceBefore = await ReadPolicyAsync(observer, null);
            var tenantBefore = await ReadPolicyAsync(observer, scenario.TenantId);
            var otherBefore = await ReadPolicyAsync(observer, scenario.OtherTenantId);
            await Assert.That(otherBefore.Revision).IsEqualTo(0L);
            await Assert.That(otherBefore.SuppressedThroughRevision).IsNull();
            await Assert.That(instanceBefore.SuppressedThroughRevision).IsEqualTo(0L);

            await SetEmailSettingAsync(context: writer, key: GovernanceSettingKeys.Email.FromName,
                value: "\"Tenant sender\"", tenantId: scenario.TenantId, actorUserId: scenario.ActorId);

            var tenantAfter = await ReadPolicyAsync(observer, scenario.TenantId);
            await Assert.That(tenantAfter.Revision).IsEqualTo(tenantBefore.Revision + 1);
            await Assert.That(tenantAfter.Cutoff).IsEqualTo(tenantBefore.Cutoff);
            await Assert.That(tenantAfter.SuppressedThroughRevision).IsEqualTo(tenantBefore.SuppressedThroughRevision);
            await Assert.That(await ReadPolicyAsync(observer, null)).IsEqualTo(instanceBefore);
            await Assert.That(await ReadPolicyAsync(observer, scenario.OtherTenantId)).IsEqualTo(otherBefore);
            await AssertOperatorStatePreservedAsync(observer, scenario);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task InstanceDisable_DoesNotChangeIndependentlyEnabledTenantPolicy()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedScenarioAsync(databasePath);
            await using var writer = CreateContext(databasePath);
            await using var observer = CreateContext(databasePath);
            var before = await ReadPolicyAsync(observer, scenario.TenantId);
            var instanceBefore = await ReadPolicyAsync(observer, null);

            await SetEnabledAsync(writer, scenario, tenantPolicy: false, enabled: false);

            await Assert.That(await ReadPolicyAsync(observer, scenario.TenantId)).IsEqualTo(before);
            var instanceAfter = await ReadPolicyAsync(observer, null);
            await Assert.That(instanceAfter.Revision).IsEqualTo(instanceBefore.Revision + 1);
            await Assert.That(instanceAfter.Cutoff).IsNotNull();
            await Assert.That(instanceAfter.SuppressedThroughRevision).IsEqualTo(instanceAfter.Revision);
            var tenantEnabled = await TenantRepository(observer).GetByTenantAndKey(
                scenario.TenantId, GovernanceSettingKeys.Email.DeliveryEnabled);
            await Assert.That(tenantEnabled!.Value).IsEqualTo("true");
            await AssertOperatorStatePreservedAsync(observer, scenario);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RevisionWriteFailure_RollsBackSettingRevisionAndSuppressionTogether(bool tenantPolicy)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedScenarioAsync(databasePath);
            await using var writer = CreateContext(databasePath);
            await using var observer = CreateContext(databasePath);
            var before = await ReadPolicyAsync(observer, tenantPolicy ? scenario.TenantId : null);
            var otherBefore = await ReadPolicyAsync(observer, scenario.OtherTenantId);
            var entityType = writer.Model.FindEntityType(tenantPolicy
                ? typeof(EmailDispatchTenantControl) : typeof(EmailDispatchProcessorState))!;
            string table = writer.GetService<ISqlGenerationHelper>().DelimitIdentifier(entityType.GetTableName()!);
            string trigger = $"CREATE TRIGGER reject_policy_revision AFTER UPDATE ON {table} BEGIN SELECT RAISE(ABORT, 'policy_revision_rejected'); END";
            await writer.Database.ExecuteSqlRawAsync(trigger);

            bool databaseRejected = false;
            try { await SetEnabledAsync(writer, scenario, tenantPolicy, enabled: false); }
            catch (Exception exception) when (exception is SqliteException or DbUpdateException)
            {
                databaseRejected = true;
            }

            await Assert.That(databaseRejected).IsTrue();
            await Assert.That(await ReadPolicyAsync(observer, tenantPolicy ? scenario.TenantId : null)).IsEqualTo(before);
            await Assert.That(await ReadPolicyAsync(observer, scenario.OtherTenantId)).IsEqualTo(otherBefore);
            string? enabledValue = tenantPolicy
                ? (await TenantRepository(observer).GetByTenantAndKey(scenario.TenantId, GovernanceSettingKeys.Email.DeliveryEnabled))?.Value
                : (await SystemRepository(observer).GetByKey(GovernanceSettingKeys.Email.DeliveryEnabled))?.Value;
            await Assert.That(enabledValue).IsEqualTo("true");
            await AssertOperatorStatePreservedAsync(observer, scenario);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false, -1L)]
    [Arguments(false, long.MaxValue)]
    [Arguments(true, -1L)]
    [Arguments(true, long.MaxValue)]
    public async Task InvalidSuppressionRevision_IsRejectedWithoutChangingPolicy(bool tenantPolicy, long watermark)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedScenarioAsync(databasePath);
            await using var writer = CreateContext(databasePath);
            await using var observer = CreateContext(databasePath);
            Guid? tenantId = tenantPolicy ? scenario.TenantId : null;
            var before = await ReadPolicyAsync(observer, tenantId);
            if (tenantPolicy)
            {
                var control = await writer.EmailDispatchTenantControls
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .SingleAsync(row => row.TenantId == scenario.TenantId);
                control.OptionalSuppressedThroughRevision = watermark;
            }
            else
            {
                var processor = await writer.EmailDispatchProcessorStates.SingleAsync(
                    row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode);
                processor.OptionalSuppressedThroughRevision = watermark;
            }

            await Assert.That(async () => await writer.SaveChangesAsync()).Throws<DbUpdateException>();
            await Assert.That(await ReadPolicyAsync(observer, tenantId)).IsEqualTo(before);
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static async Task<Scenario> SeedScenarioAsync(string databasePath)
    {
        await CreateDatabaseAsync(databasePath);
        await using var context = CreateContext(databasePath);
        var actorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
        await SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
            value: "false", actorUserId: actorId);
        var tenantIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        foreach (var tenantId in tenantIds)
            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = "Durable policy tenant",
                Slug = $"revision-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        await ApplyEmailSettingsAsync(context: context, mutations:
            [
                new(TenantId: tenantIds[0], Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: false),
                new(TenantId: tenantIds[0], Key: GovernanceSettingKeys.Email.SmtpHost,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.independent.test\"", IsLocked: false),
                new(TenantId: tenantIds[0], Key: GovernanceSettingKeys.Email.FromAddress,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"events@independent.test\"", IsLocked: false)
            ], actorUserId: actorId);

        DateTime now = await RelationalDatabaseClock.GetUtcNowAsync(context, CancellationToken.None);
        var instance = await context.EmailDispatchProcessorStates.SingleOrDefaultAsync(state => state.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode);
        if (instance is null)
        {
            instance = new EmailDispatchProcessorState { Id = Guid.CreateVersion7(), ProcessorCode = EmailDispatchOutboxRepository.SmtpProcessorCode, UpdatedAt = now };
            context.EmailDispatchProcessorStates.Add(instance);
        }
        instance.IsPaused = true;
        instance.PauseReason = "operator-maintenance";
        instance.PausedAt = now;
        instance.GlobalSmtpRateLimitPerMinuteOverride = 23;
        instance.OptionalRemindersDeferred = true;
        instance.SmtpAvailableTokens = 7;
        instance.SmtpRefillAt = now;
        foreach (var tenantId in tenantIds)
        {
            var control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleOrDefaultAsync(state => state.TenantId == tenantId);
            if (control is null)
            {
                control = new EmailDispatchTenantControl { Id = Guid.CreateVersion7(), TenantId = tenantId, CreatedAt = now };
                context.EmailDispatchTenantControls.Add(control);
            }
            control.IsPaused = true;
            control.PauseReason = "tenant-maintenance";
            control.PausedAt = now;
            control.SmtpAvailableTokens = 3;
            control.SmtpRefillAt = now;
        }
        await context.SaveChangesAsync();
        return new(TenantId: tenantIds[0], OtherTenantId: tenantIds[1], ActorId: actorId, OperatorTimestamp: now);
    }

    private static Task SetEnabledAsync(ExploreDbContext context, Scenario scenario, bool tenantPolicy, bool enabled) =>
        enabled
            ? SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.DeliveryEnabled,
                value: "true", tenantId: tenantPolicy ? scenario.TenantId : null, actorUserId: scenario.ActorId)
            : ConfirmEmailDisableAsync(context: context, tenantId: tenantPolicy ? scenario.TenantId : null,
                actorUserId: scenario.ActorId);

    private static Task SetSenderNameAsync(ExploreDbContext context, Scenario scenario, bool tenantPolicy, string value) =>
        SetEmailSettingAsync(context: context, key: GovernanceSettingKeys.Email.FromName,
            value: value, tenantId: tenantPolicy ? scenario.TenantId : null, actorUserId: scenario.ActorId);

    private static async Task<(long Revision, DateTime? Cutoff, long? SuppressedThroughRevision)> ReadPolicyAsync(ExploreDbContext context, Guid? tenantId)
    {
        if (tenantId.HasValue)
        {
            var control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(state => state.TenantId == tenantId.Value);
            return (control.DeliveryPolicyRevision, control.OptionalSuppressedThroughUtc, control.OptionalSuppressedThroughRevision);
        }
        var instance = await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync(state => state.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode);
        return (instance.DeliveryPolicyRevision, instance.OptionalSuppressedThroughUtc, instance.OptionalSuppressedThroughRevision);
    }

    private static async Task AssertOperatorStatePreservedAsync(ExploreDbContext context, Scenario scenario)
    {
        var instance = await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync(state => state.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode);
        await Assert.That(instance.IsPaused).IsTrue();
        await Assert.That(instance.PauseReason).IsEqualTo("operator-maintenance");
        await Assert.That(instance.PausedAt).IsEqualTo(scenario.OperatorTimestamp);
        await Assert.That(instance.GlobalSmtpRateLimitPerMinuteOverride).IsEqualTo(23);
        await Assert.That(instance.OptionalRemindersDeferred).IsTrue();
        await Assert.That(instance.SmtpAvailableTokens).IsEqualTo(7);
        await Assert.That(instance.SmtpRefillAt).IsEqualTo(scenario.OperatorTimestamp);
        foreach (var tenantId in new[] { scenario.TenantId, scenario.OtherTenantId })
        {
            var control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(state => state.TenantId == tenantId);
            await Assert.That(control.IsPaused).IsTrue();
            await Assert.That(control.PauseReason).IsEqualTo("tenant-maintenance");
            await Assert.That(control.PausedAt).IsEqualTo(scenario.OperatorTimestamp);
            await Assert.That(control.SmtpAvailableTokens).IsEqualTo(3);
            await Assert.That(control.SmtpRefillAt).IsEqualTo(scenario.OperatorTimestamp);
        }
    }

    private static SystemSettingRepository SystemRepository(ExploreDbContext context) =>
        new(context, new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

    private static TenantSettingRepository TenantRepository(ExploreDbContext context) =>
        new(context, new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"email-policy-revision-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }

    private sealed record Scenario(Guid TenantId, Guid OtherTenantId, Guid ActorId, DateTime OperatorTimestamp);
}
