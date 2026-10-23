
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliverySettingsWriterTests
{
    [Test]
    public async Task ExplicitInternalDisablePreservesAtomicPolicyWithoutCreatingAHumanActor()
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            await Assert.That(await context.Users.CountAsync()).IsEqualTo(0);
            long revision = await context.EmailDispatchProcessorStates.AsNoTracking()
                .Select(state => state.DeliveryPolicyRevision).SingleAsync();
            var writer = CreateInternalEmailSettingsWriter(context);
            await Assert.That((await writer.ApplyInternalAsync(
                [Set(null, GovernanceSettingKeys.Email.DeliveryEnabled, "false")], null)).Status)
                .IsEqualTo(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
            await Assert.That((await writer.DisableInternalAsync(null, null)).Status)
                .IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(await context.Users.CountAsync()).IsEqualTo(0);
            await Assert.That((await context.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value).IsEqualTo("false");
            var state = await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync();
            await Assert.That(state.DeliveryPolicyRevision).IsEqualTo(revision + 1);
            await Assert.That(state.OptionalSuppressedThroughRevision).IsEqualTo(state.DeliveryPolicyRevision);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task HarmlessBatch_PreservesExistingMetadataAndWritesFinalValues()
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var before = await context.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
            var result = await Writer(context).ApplyInternalAsync(
                [Set(null, GovernanceSettingKeys.Email.SmtpHost, "\"smtp.changed.test\""),
                    Set(null, GovernanceSettingKeys.Email.FromAddress, "\"events@changed.test\"")], actorUserId: null);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(result.Changes.Length).IsEqualTo(2);
            var after = await context.SystemSettings.AsNoTracking().SingleAsync(row => row.Id == before.Id);
            await Assert.That(after.Value).IsEqualTo("\"smtp.changed.test\"");
            await Assert.That(after.CreatedAt).IsEqualTo(before.CreatedAt);
            await Assert.That(after.Description).IsEqualTo(before.Description);
            await Assert.That(after.Category).IsEqualTo(before.Category);
            await Assert.That(after.IsLocked).IsEqualTo(before.IsLocked);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task UnconfirmedDisable_RejectedBeforeWriteEvenWhenCallerCommits()
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var writer = Writer(context, mutationLock);
            long revision = await context.EmailDispatchProcessorStates.AsNoTracking().Select(row => row.DeliveryPolicyRevision).SingleAsync();
            var outcome = await mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => unitOfWork.ExecuteSerializableAsync(
                    async transactionToken =>
                    {
                        var result = await writer.ApplyInternalAsync([Set(null, GovernanceSettingKeys.Email.DeliveryEnabled, "false")],
                            actorUserId: null, transactionToken);
                        await context.SaveChangesAsync(transactionToken);
                        return result;
                    }, token));

            await Assert.That(outcome.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
            await using var observer = CreateContext(path);
            await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)).Value).IsEqualTo("true");
            await Assert.That(await observer.EmailDispatchProcessorStates.AsNoTracking().Select(row => row.DeliveryPolicyRevision).SingleAsync())
                .IsEqualTo(revision);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IndirectDisable_RejectsHostResetAndDelegationLock(bool delegationLock)
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var writer = Writer(context);
            await DisableInstanceAsync(context);
            Guid tenantId = await AddIndependentTenantAsync(context, writer);
            EmailDeliverySettingMutation mutation = delegationLock
                ? Set(null, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true")
                : new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.SmtpHost, Kind: EmailDeliverySettingMutationKind.Remove);
            var outcome = await writer.ApplyInternalAsync([mutation], actorUserId: null);
            await Assert.That(outcome.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
            await Assert.That(outcome.Changes.IsEmpty).IsTrue();
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task CompleteMultiScopeBatch_AllowsOwnershipResetWhenFinalInstanceStateIsEnabled()
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var writer = Writer(context);
            await DisableInstanceAsync(context);
            Guid tenantId = await AddIndependentTenantAsync(context, writer);
            var outcome = await writer.ApplyInternalAsync(
                [new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.SmtpHost, Kind: EmailDeliverySettingMutationKind.Remove),
                    Set(null, GovernanceSettingKeys.Email.DeliveryEnabled, "true")], actorUserId: null);
            await Assert.That(outcome.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(outcome.Changes.Length).IsEqualTo(2);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task MixedInstanceAndIndependentTenantWrites_AdvanceBothRevisionsExactlyOnce()
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var writer = Writer(context);
            Guid tenantId = await AddIndependentTenantAsync(context, writer);
            long instanceBefore = await context.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => row.DeliveryPolicyRevision).SingleAsync();
            long tenantBefore = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking().Where(row => row.TenantId == tenantId).Select(row => row.DeliveryPolicyRevision).SingleAsync();
            var outcome = await writer.ApplyInternalAsync(
                [Set(null, GovernanceSettingKeys.Email.FromName, "\"Instance sender\""),
                    Set(tenantId, GovernanceSettingKeys.Email.FromName, "\"Tenant sender\"")], actorUserId: null);
            await Assert.That(outcome.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(await context.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(instanceBefore + 1);
            await Assert.That(await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking().Where(row => row.TenantId == tenantId).Select(row => row.DeliveryPolicyRevision).SingleAsync())
                .IsEqualTo(tenantBefore + 1);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TenantEnableResetAndInstanceEnableLock_CannotDisableIndependentTenant(bool instanceLock)
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var writer = Writer(context);
            await DisableInstanceAsync(context);
            Guid tenantId = await AddIndependentTenantAsync(context, writer);
            var mutation = instanceLock
                ? new EmailDeliverySettingMutation(TenantId: null, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: true)
                : new EmailDeliverySettingMutation(TenantId: tenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.Remove);
            var result = await writer.ApplyInternalAsync([mutation], actorUserId: null);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task CallerOwnedTransaction_WithoutFullDeclaredSmtpLeaseCannotWrite()
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var writer = Writer(context, mutationLock);
            await Assert.That(async () => await mutationLock.ExecuteOrderedGroupsAsync(
                [[GovernanceSettingKeys.Email.DeliveryEnabled]], token => unitOfWork.ExecuteSerializableAsync(
                    transactionToken => writer.ApplyInternalAsync([Set(null, GovernanceSettingKeys.Email.FromName, "\"rejected\"")],
                        actorUserId: null, transactionToken), token))).Throws<InvalidOperationException>();
            await Assert.That(await context.SystemSettings.AsNoTracking().AnyAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.FromName && row.Value == "\"rejected\"")).IsFalse();
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task InvalidLaterMutation_LeavesEarlierMutationUntrackedAndUnwritten()
    {
        string path = NewPath();
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            var before = await context.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
            var result = await Writer(context).ApplyInternalAsync(
                [Set(null, GovernanceSettingKeys.Email.SmtpHost, "\"smtp.rejected.test\""),
                    Set(null, GovernanceSettingKeys.Email.DeliveryEnabled, "not-json")], actorUserId: null);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.InvalidMutation);
            await context.SaveChangesAsync();
            await Assert.That((await context.SystemSettings.AsNoTracking().SingleAsync(row => row.Id == before.Id)).Value).IsEqualTo(before.Value);
        }
        finally { DeleteDatabase(path); }
    }

    private static EmailDeliverySettingMutation Set(Guid? tenantId, string key, string value) =>
        new(TenantId: tenantId, Key: key, Kind: EmailDeliverySettingMutationKind.SetValue, Value: value);

    private static InternalEmailDeliverySettingsWriter Writer(
        ExploreDbContext context, RelationalSettingMutationLock? mutationLock = null) =>
        CreateInternalEmailSettingsWriter(context, mutationLock);

    private static async Task DisableInstanceAsync(ExploreDbContext context)
    {
        var result = await Writer(context).DisableInternalAsync(null, null);
        await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
    }

    private static async Task<Guid> AddIndependentTenantAsync(ExploreDbContext context, InternalEmailDeliverySettingsWriter writer)
    {
        Guid tenantId = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            FullName = "Atomic SMTP tenant",
            Slug = $"smtp-writer-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var outcome = await writer.ApplyInternalAsync(
            [Set(null, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false"),
                Set(tenantId, GovernanceSettingKeys.Email.DeliveryEnabled, "true"),
                Set(tenantId, GovernanceSettingKeys.Email.SmtpHost, "\"smtp.independent.test\""),
                Set(tenantId, GovernanceSettingKeys.Email.FromAddress, "\"events@independent.test\"")], actorUserId: null);
        await Assert.That(outcome.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
        return tenantId;
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"email-settings-writer-{Guid.CreateVersion7():N}.db");
    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }
}
