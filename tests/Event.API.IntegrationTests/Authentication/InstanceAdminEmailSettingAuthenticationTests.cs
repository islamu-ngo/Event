using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class InstanceAdminEmailSettingAuthenticationTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TenantSmtpLockMutation_CompletesAndPublishesOnlyAfterCommit(bool lockSetting)
    {
        var notifications = new List<SettingChangedNotification>();
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
            instanceAdministrator: true, tenantAdministrator: false, configureServices: services =>
                services.AddScoped<INotificationHandler<SettingChangedNotification>>(provider =>
                    new CommittedNotifications(provider.GetRequiredService<ExploreDbContext>(), notifications)));
        await using (var setup = fixture.CreateIndependentContext())
        {
            var unit = new EfCoreUnitOfWork(setup);
            var writer = new InternalEmailDeliverySettingsWriter(setup,
                new RelationalSettingMutationLock(setup, unit), unit);
            var result = await writer.ApplyInternalAsync(
                [new(fixture.TenantId, GovernanceSettingKeys.Email.SmtpHost,
                    EmailDeliverySettingMutationKind.SetValue, "\"smtp.control-plane.test\"", !lockSetting),
                 new(fixture.TenantId, GovernanceSettingKeys.Email.FromAddress,
                    EmailDeliverySettingMutationKind.SetValue, "\"events@control-plane.test\"")],
                fixture.Subject.PrincipalId);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
        }
        await using var observer = fixture.CreateIndependentContext();
        var before = await observer.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .SingleAsync(row => row.TenantId == fixture.TenantId && row.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
        long revision = await observer.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .Where(row => row.TenantId == fixture.TenantId).Select(row => row.DeliveryPolicyRevision).SingleAsync();
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var result = lockSetting
                ? await services.GetRequiredService<ICommandHandler<LockInstanceTenantSettingCommand,
                    BaseCommandResponse<Guid>>>().ExecuteAsync(new(fixture.TenantId, GovernanceSettingKeys.Email.SmtpHost),
                        CancellationToken.None)
                : await services.GetRequiredService<ICommandHandler<UnlockInstanceTenantSettingCommand,
                    BaseCommandResponse<Guid>>>().ExecuteAsync(new(fixture.TenantId, GovernanceSettingKeys.Email.SmtpHost),
                        CancellationToken.None);
            await Assert.That(result.IsSuccess).IsTrue();
        });
        var persisted = await observer.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .SingleAsync(row => row.TenantId == fixture.TenantId && row.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
        await Assert.That(persisted.IsLocked).IsEqualTo(lockSetting);
        await Assert.That(persisted.Value).IsEqualTo(before.Value);
        await Assert.That(persisted.CreatedAt).IsEqualTo(before.CreatedAt);
        await Assert.That(persisted.CreatedBy).IsEqualTo(before.CreatedBy);
        await Assert.That(persisted.UpdatedBy).IsEqualTo(fixture.Subject.PrincipalId);
        await Assert.That(await observer.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .Where(row => row.TenantId == fixture.TenantId).Select(row => row.DeliveryPolicyRevision).SingleAsync())
            .IsEqualTo(revision + 1);
        var notification = notifications.Single();
        await Assert.That(notification.Key).IsEqualTo(GovernanceSettingKeys.Email.SmtpHost);
        await Assert.That(notification.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(notification.ActorUserId).IsEqualTo(fixture.Subject.PrincipalId);
    }

    private sealed class CommittedNotifications(
        ExploreDbContext context, List<SettingChangedNotification> notifications)
        : INotificationHandler<SettingChangedNotification>
    {
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Settings notifications must follow the selected owner's commit.");
            notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
