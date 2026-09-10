
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryDisableImpactTests
{
    [Test]
    public async Task InstanceDisable_IncludesInheritedAndMisconfiguredScopesButPreservesIndependentEnablement()
    {
        string path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            await UnlockDelegationAsync(context);
            Guid inherited = await AddTenantAsync(context);
            Guid sharedExplicit = await AddTenantAsync(context);
            Guid independent = await AddTenantAsync(context);
            Guid inheritedByo = await AddTenantAsync(context);
            Guid misconfigured = await AddTenantAsync(context);
            Guid disabled = await AddTenantAsync(context);
            await SetTenantSettingsAsync(context, sharedExplicit,
                (GovernanceSettingKeys.Email.DeliveryEnabled, "true"));
            await SetTenantSettingsAsync(context, independent,
                (GovernanceSettingKeys.Email.DeliveryEnabled, "true"),
                (GovernanceSettingKeys.Email.SmtpHost, "\"smtp.independent.test\""),
                (GovernanceSettingKeys.Email.FromAddress, "\"events@independent.test\""));
            await SetTenantSettingsAsync(context, inheritedByo,
                (GovernanceSettingKeys.Email.SmtpHost, "\"smtp.inherited.test\""),
                (GovernanceSettingKeys.Email.FromAddress, "\"events@inherited.test\""));
            await SetTenantSettingsAsync(context, misconfigured,
                (GovernanceSettingKeys.Email.SmtpHost, "\"smtp.incomplete.test\""));
            await SetTenantSettingsAsync(context, disabled,
                (GovernanceSettingKeys.Email.DeliveryEnabled, "false"));
            context.ChangeTracker.Clear();

            var processorBefore = await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync();
            var controlsBefore = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().ToDictionaryAsync(row => row.TenantId, row => row.DeliveryPolicyRevision);
            var overridesBefore = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Value, row.IsLocked }).ToListAsync();
            await context.Database.OpenConnectionAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA query_only = ON");
            try
            {
                var snapshot = await new EmailDeliveryDisableImpactReader(context).ReadAsync(null);

                await Assert.That(snapshot).IsNotNull();
                await Assert.That(snapshot!.CanDisable).IsTrue();
                await Assert.That(snapshot.Revision).IsEqualTo(processorBefore.DeliveryPolicyRevision);
                EmailDeliveryAffectedScope[] expected = [new(TenantId: null, Revision: processorBefore.DeliveryPolicyRevision),
                    .. new[] { inherited, sharedExplicit, inheritedByo, misconfigured }.Order()
                        .Select(id => new EmailDeliveryAffectedScope(TenantId: id, Revision: controlsBefore.GetValueOrDefault(id)))];
                await Assert.That(snapshot.AffectedScopes.SequenceEqual(expected)).IsTrue();
                await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
                await transaction.CommitAsync();
            }
            finally
            {
                await context.Database.ExecuteSqlRawAsync("PRAGMA query_only = OFF");
            }

            await using var observer = CreateContext(path);
            await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(
                setting => setting.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value).IsEqualTo("true");
            await Assert.That((await observer.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).DeliveryPolicyRevision)
                .IsEqualTo(processorBefore.DeliveryPolicyRevision);
            var controlsAfter = await observer.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().ToDictionaryAsync(row => row.TenantId, row => row.DeliveryPolicyRevision);
            await Assert.That(controlsAfter.OrderBy(row => row.Key).SequenceEqual(controlsBefore.OrderBy(row => row.Key))).IsTrue();
            var overridesAfter = await observer.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Value, row.IsLocked }).ToListAsync();
            await Assert.That(overridesAfter.SequenceEqual(overridesBefore)).IsTrue();
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TenantDisable_RespectsInstanceAndDelegationLocks(bool delegationLock)
    {
        string path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            await UnlockDelegationAsync(context);
            Guid tenantId = await AddTenantAsync(context);
            await SetTenantSettingsAsync(context, tenantId, (GovernanceSettingKeys.Email.DeliveryEnabled, "true"));
            await ApplyEmailSettingsAsync(context,
                [new(TenantId: null,
                    Key: delegationLock ? GovernanceSettingKeys.TenantDelegation.LockSmtp : GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: !delegationLock)]);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var snapshot = await new EmailDeliveryDisableImpactReader(context).ReadAsync(tenantId);
            await Assert.That(snapshot!.IsLocked).IsTrue();
            await Assert.That(snapshot.CanDisable).IsFalse();
            await Assert.That(snapshot.AffectedScopes.IsEmpty).IsTrue();
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task TenantDisable_OnlyAffectsSelectedTenantAndUsesZeroForAbsentControl()
    {
        string path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            await UnlockDelegationAsync(context);
            Guid selected = await AddTenantAsync(context);
            await AddTenantAsync(context);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var reader = new EmailDeliveryDisableImpactReader(context);
            var snapshot = await reader.ReadAsync(selected);
            await Assert.That(snapshot!.Revision).IsEqualTo(0L);
            await Assert.That(snapshot.CanDisable).IsTrue();
            await Assert.That(snapshot.AffectedScopes.SequenceEqual([new EmailDeliveryAffectedScope(TenantId: selected, Revision: 0)])).IsTrue();
            await Assert.That(await reader.ReadAsync(Guid.CreateVersion7())).IsNull();
            await Assert.That(async () => await reader.ReadAsync(Guid.Empty)).Throws<ArgumentException>();
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ScopeOwnLock_OnlyLocksDescendantsAndStillAllowsDisable(bool tenantScope)
    {
        string path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            await UnlockDelegationAsync(context);
            Guid tenantId = await AddTenantAsync(context);
            if (tenantScope)
                await ApplyEmailSettingsAsync(context,
                    [new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: true)]);
            else
                await ApplyEmailSettingsAsync(context,
                    [new(TenantId: null, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: true)]);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var snapshot = await new EmailDeliveryDisableImpactReader(context).ReadAsync(tenantScope ? tenantId : null);
            await Assert.That(snapshot!.IsLocked).IsFalse();
            await Assert.That(snapshot.CanDisable).IsTrue();
            await Assert.That(snapshot.AffectedScopes.Any(scope => scope.TenantId == tenantId)).IsTrue();
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task InstancePreview_ReflectsCommittedMembershipChangesWithoutAnInstanceRevisionChange()
    {
        string path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var observer = CreateContext(path);
            EmailDeliveryDisableImpactSnapshot before;
            await using (var transaction = await observer.Database.BeginTransactionAsync())
            {
                before = (await new EmailDeliveryDisableImpactReader(observer).ReadAsync(null))!;
                await transaction.CommitAsync();
            }

            await using var writer = CreateContext(path);
            Guid createdTenant = await AddTenantAsync(writer);
            await using (var transaction = await observer.Database.BeginTransactionAsync())
            {
                var afterCreation = (await new EmailDeliveryDisableImpactReader(observer).ReadAsync(null))!;
                await Assert.That(afterCreation.Revision).IsEqualTo(before.Revision);
                await Assert.That(afterCreation.AffectedScopes.Length).IsEqualTo(before.AffectedScopes.Length + 1);
                await Assert.That(afterCreation.AffectedScopes.Contains(
                    new EmailDeliveryAffectedScope(TenantId: createdTenant, Revision: 0))).IsTrue();
                await transaction.CommitAsync();
            }

            await writer.Tenants.Where(tenant => tenant.Id == createdTenant).ExecuteDeleteAsync();
            await using (var transaction = await observer.Database.BeginTransactionAsync())
            {
                var afterRemoval = (await new EmailDeliveryDisableImpactReader(observer).ReadAsync(null))!;
                await Assert.That(afterRemoval.Revision).IsEqualTo(before.Revision);
                await Assert.That(afterRemoval.AffectedScopes.SequenceEqual(before.AffectedScopes)).IsTrue();
            }
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task Preview_RequiresCallerTransaction()
    {
        string path = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            await Assert.That(async () => await new EmailDeliveryDisableImpactReader(context).ReadAsync(null))
                .Throws<InvalidOperationException>();
        }
        finally { DeleteDatabase(path); }
    }

    private static async Task<Guid> AddTenantAsync(ExploreDbContext context)
    {
        Guid id = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Email impact tenant",
            Slug = $"impact-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static Task SetTenantSettingsAsync(ExploreDbContext context, Guid tenantId,
        params (string Key, string Value)[] settings) => settings.Length == 1
            ? SetEmailSettingAsync(context, settings[0].Key, settings[0].Value, tenantId: tenantId)
            : ApplyEmailSettingsAsync(context,
                [.. settings.Select(setting => new EmailDeliverySettingMutation(TenantId: tenantId,
                    Key: setting.Key, Kind: EmailDeliverySettingMutationKind.SetValue, Value: setting.Value))]);

    private static Task UnlockDelegationAsync(ExploreDbContext context) =>
        SetEmailSettingAsync(context, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"email-disable-impact-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }
}
