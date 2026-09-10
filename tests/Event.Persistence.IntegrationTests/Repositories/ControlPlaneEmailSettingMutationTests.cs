
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Handlers.Commands;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class ControlPlaneEmailSettingMutationTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TenantSmtpLockMutation_CompletesAndPublishesOnlyAfterCommit(bool lockSetting)
    {
        string path = Path.Combine(Path.GetTempPath(), $"control-plane-email-lock-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(path);
            await using var context = CreateContext(path);
            Guid actorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
            await SetEmailSettingAsync(context, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
            Guid tenantId = Guid.CreateVersion7();
            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = "Control Plane SMTP tenant",
                Slug = $"smtp-lock-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            using var fixture = new InstanceSettingsCommandFixture(context: context, userId: actorId);
            await ApplyEmailSettingsAsync(context,
                [new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.SmtpHost,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.control-plane.test\"", IsLocked: !lockSetting),
                 new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.FromAddress,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"events@control-plane.test\"")],
                actorUserId: actorId, mutationLock: fixture.MutationLock);
            var repository = new TenantSettingRepository(context, fixture.MutationLock);
            var before = (await repository.GetByTenantAndKey(tenantId, GovernanceSettingKeys.Email.SmtpHost))!;
            long revision = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .Where(row => row.TenantId == tenantId).Select(row => row.DeliveryPolicyRevision).SingleAsync();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var result = lockSetting
                ? await new LockControlPlaneTenantSettingCommandHandler(
                    repository: repository, systemSettingRepository: fixture.SystemSettings,
                    mutationLock: fixture.MutationLock, currentUserService: fixture.CurrentUserService,
                    settingsResolver: fixture.Settings, mediator: fixture.Mediator,
                    emailDeliverySettingsWriter: fixture.EmailDeliverySettingsWriter, unitOfWork: fixture.UnitOfWork,
                    visitorSettings: fixture.VisitorSettings)
                    .Handle(new LockControlPlaneTenantSettingCommand(tenantId: tenantId, key: GovernanceSettingKeys.Email.SmtpHost), cancellation.Token)
                : await new UnlockControlPlaneTenantSettingCommandHandler(
                    repository: repository, systemSettingRepository: fixture.SystemSettings,
                    mutationLock: fixture.MutationLock, currentUserService: fixture.CurrentUserService,
                    settingsResolver: fixture.Settings, mediator: fixture.Mediator,
                    emailDeliverySettingsWriter: fixture.EmailDeliverySettingsWriter, unitOfWork: fixture.UnitOfWork,
                    visitorSettings: fixture.VisitorSettings)
                    .Handle(new UnlockControlPlaneTenantSettingCommand(tenantId: tenantId, key: GovernanceSettingKeys.Email.SmtpHost), cancellation.Token);

            await Assert.That(result.IsSuccess).IsTrue();
            await using var observer = CreateContext(path);
            var persisted = await observer.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == tenantId && row.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
            await Assert.That(persisted.IsLocked).IsEqualTo(lockSetting);
            await Assert.That(persisted.Value).IsEqualTo(before.Value);
            await Assert.That(persisted.CreatedAt).IsEqualTo(before.CreatedAt);
            await Assert.That(persisted.CreatedBy).IsEqualTo(before.CreatedBy);
            await Assert.That(persisted.UpdatedBy).IsEqualTo(actorId);
            await Assert.That(await observer.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .Where(row => row.TenantId == tenantId).Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(revision + 1);
            var notification = fixture.Notifications.Published.Single();
            await Assert.That(notification.Key).IsEqualTo(GovernanceSettingKeys.Email.SmtpHost);
            await Assert.That(notification.TenantId).IsEqualTo(tenantId);
            await Assert.That(notification.ActorUserId).IsEqualTo(actorId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }
}
