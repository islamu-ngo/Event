using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Application.Features.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class TenantPlanEmailAuthenticationTests
{
    private static Task<SmtpAuthenticatedFixture> CreateFixtureAsync(Notifications notifications,
        Func<string, CancellationToken, Task>? beforeOuterLock = null, SmtpWriteObserver? writeObserver = null) =>
        SmtpAuthenticatedFixture.CreateAsync(configureServices: services =>
        {
            services.AddHumanMixedSettingsSources(IdentityDatabaseTopology.Colocated);
            if (writeObserver is not null)
            {
                var factory = services.Last(descriptor => descriptor.ServiceType == typeof(IHumanMixedSettingsSourceFactory));
                services.RemoveAll<IHumanMixedSettingsSourceFactory>();
                services.AddScoped<IHumanMixedSettingsSourceFactory>(provider =>
                    new ObservedPlanSourceFactory((IHumanMixedSettingsSourceFactory)factory.ImplementationFactory!(provider),
                        provider.GetRequiredService<ExploreDbContext>(), writeObserver));
            }
            services.AddScoped<INotificationHandler<SettingChangedNotification>>(provider =>
                new NotificationObserver(provider.GetRequiredService<ExploreDbContext>(), notifications));
            if (beforeOuterLock is not null)
                services.Replace(ServiceDescriptor.Scoped<ISettingMutationLock>(provider =>
                    new RelationalSettingMutationLock(provider.GetRequiredService<ExploreDbContext>(),
                        provider.GetRequiredService<IUnitOfWork>(), beforeOuterLock)));
        });

    private static async Task<PlanScenario> SeedPlanAsync(SmtpAuthenticatedFixture fixture, bool mixed,
        long? storageQuota = null, bool unsafePublication = false)
    {
        await using var context = fixture.CreateIndependentContext();
        // These are persisted starting rows, not an internal writer impersonating the human.
        var startingOverrides = await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(row => row.TenantId == fixture.TenantId).ToListAsync();
        context.TenantSettingOverrides.RemoveRange(startingOverrides);
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var plan = new TenantPlan
        {
            Id = Guid.CreateVersion7(),
            Key = $"smtp-{Guid.CreateVersion7():N}",
            DisplayName = "SMTP plan",
            CreatedAt = now
        };
        var version = new TenantPlanVersion
        {
            Id = Guid.CreateVersion7(),
            TenantPlan = plan,
            TenantPlanId = plan.Id,
            VersionNumber = 1,
            TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly",
            IsActiveForProvisioning = true,
            CreatedAt = now,
            Settings =
            [
                new() { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Email.SmtpHost,
                    JsonValue = "\"smtp.tenant-plan.test\"", CreatedAt = now },
                new() { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Email.FromAddress,
                    JsonValue = "\"events@tenant-plan.test\"", CreatedAt = now }
            ]
        };
        if (mixed)
        {
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.AiAssistant.Enabled,
                JsonValue = "true",
                CreatedAt = now
            });
            if (unsafePublication)
                version.Settings.Add(new TenantPlanVersionSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = EventReportingIntakeSettingDefinitions.IntakeEnabled.Key,
                    JsonValue = "false",
                    CreatedAt = now
                });
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = EventSettingDefinitions.RequireApproval.Key,
                JsonValue = unsafePublication ? "false" : "true",
                CreatedAt = now
            });
        }
        if (storageQuota.HasValue)
            version.Quotas.Add(new TenantPlanVersionQuota
            {
                Id = Guid.CreateVersion7(),
                QuotaKey = TenantPlanQuotaKeys.StorageBytes,
                Limit = storageQuota.Value,
                CreatedAt = now
            });
        var repository = new TenantPlanRepository(context);
        var assignment = await repository.CreateAssignmentAsync(new TenantPlanAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            TenantPlan = plan,
            TenantPlanId = plan.Id,
            TenantPlanVersion = version,
            TenantPlanVersionId = version.Id,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = fixture.Subject.PrincipalId,
            AssignedAt = now,
            CreatedAt = now
        });
        var persisted = (await repository.GetAssignmentAsync(assignment.Id))!;
        return new(fixture.TenantId, assignment.Id, fixture.Subject.PrincipalId,
            AssignmentSnapshot(persisted), version.Settings.Select(row => (row.SettingKey, row.JsonValue, row.IsLocked)).ToArray(),
            version.Quotas.Select(row => (row.QuotaKey, row.Limit)).ToArray());
    }

    private static async Task AssertAppliedAsync(SmtpAuthenticatedFixture fixture, Notifications notifications,
        PlanScenario scenario, bool mixed)
    {
        await using var observer = fixture.CreateIndependentContext();
        var settings = await SettingsAsync(observer, scenario.TenantId);
        await Assert.That(settings.Single(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost).Value)
            .IsEqualTo("\"smtp.tenant-plan.test\"");
        await Assert.That(settings.Single(row => row.SettingKey == GovernanceSettingKeys.Email.FromAddress).Value)
            .IsEqualTo("\"events@tenant-plan.test\"");
        if (mixed)
        {
            await Assert.That(settings.Single(row => row.SettingKey == GovernanceSettingKeys.AiAssistant.Enabled).Value)
                .IsEqualTo("true");
            await Assert.That(settings.Single(row => row.SettingKey == EventSettingDefinitions.RequireApproval.Key).Value)
                .IsEqualTo("true");
        }
        await Assert.That(settings.Count).IsEqualTo(mixed ? 4 : 2);
        await Assert.That(settings.All(row => (row.UpdatedBy ?? row.CreatedBy) == scenario.ActorId)).IsTrue();
        var assignment = (await new TenantPlanRepository(observer).GetAssignmentAsync(scenario.AssignmentId))!;
        await Assert.That(assignment.TenantPlanVersionId).IsEqualTo(scenario.Assignment.VersionId);
        await Assert.That(assignment.TenantPlanAssignmentStatusId).IsEqualTo(scenario.Assignment.StatusId);
        await Assert.That(assignment.UpdatedAt).IsNotNull();
        await Assert.That(assignment.UpdatedBy).IsEqualTo(scenario.ActorId);
        await AssertVersionUnchangedAsync(observer, scenario);
        await Assert.That(notifications.Published.Select(notification => notification.Key))
            .IsEquivalentTo(scenario.Settings.Select(setting => setting.Key));
        await Assert.That(notifications.Published.All(notification =>
            notification.ActorUserId == scenario.ActorId && notification.TenantId == scenario.TenantId)).IsTrue();
    }

    private static async Task AssertUnchangedAsync(SmtpAuthenticatedFixture fixture, Notifications notifications,
        PlanScenario scenario)
    {
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await SettingsAsync(observer, scenario.TenantId)).IsEmpty();
        await AssertAssignmentUnchangedAsync(observer, scenario);
        await Assert.That(notifications.Published).IsEmpty();
    }

    private static async Task AssertAssignmentUnchangedAsync(ExploreDbContext observer, PlanScenario scenario)
    {
        var assignment = (await new TenantPlanRepository(observer).GetAssignmentAsync(scenario.AssignmentId))!;
        await Assert.That(AssignmentSnapshot(assignment)).IsEqualTo(scenario.Assignment);
        await AssertVersionUnchangedAsync(observer, scenario);
        await Assert.That(await observer.Set<TenantPlanApplicationLog>().AsNoTracking()
            .AnyAsync(row => row.TenantId == scenario.TenantId && row.TenantPlanAssignmentId == scenario.AssignmentId)).IsFalse();
    }

    private static async Task AssertVersionUnchangedAsync(ExploreDbContext observer, PlanScenario scenario)
    {
        var version = (await new TenantPlanRepository(observer).GetVersionAsync(scenario.Assignment.VersionId))!;
        await Assert.That(version.VersionNumber).IsEqualTo(1);
        await Assert.That(version.TenantPlanStatusId).IsEqualTo((int)TenantPlanStatusEnum.Published);
        await Assert.That(version.Settings.Select(row => (row.SettingKey, row.JsonValue, row.IsLocked)))
            .IsEquivalentTo(scenario.Settings);
        await Assert.That(version.Quotas.Select(row => (row.QuotaKey, row.Limit))).IsEquivalentTo(scenario.Quotas);
    }

    private static Task<List<TenantSetting>> SettingsAsync(ExploreDbContext context, Guid tenantId) =>
        TenantSettingRepository.CreateReadOnly(context, new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)))
            .GetAllForTenant(tenantId);

    private static PersistedAssignment AssignmentSnapshot(TenantPlanAssignment row) =>
        new(row.TenantId, row.TenantPlanId, row.TenantPlanVersionId, row.TenantPlanAssignmentStatusId,
            row.AssignedByUserId, row.AssignedAt, row.EndedAt, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    private static PersistedSetting SettingSnapshot(TenantSetting row) =>
        new(row.Id, row.SettingKey, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    private sealed record PersistedAssignment(Guid TenantId, Guid PlanId, Guid VersionId, int StatusId,
        Guid? AssignedBy, DateTime AssignedAt, DateTime? EndedAt, DateTime CreatedAt, Guid? CreatedBy,
        DateTime? UpdatedAt, Guid? UpdatedBy);

    private sealed record PersistedSetting(Guid Id, string Key, string Value, bool IsLocked,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);

    private sealed record PlanScenario(Guid TenantId, Guid AssignmentId, Guid ActorId, PersistedAssignment Assignment,
        (string Key, string Value, bool Locked)[] Settings, (string Key, long Limit)[] Quotas);

    private sealed class Notifications
    {
        internal List<SettingChangedNotification> Published { get; } = [];
    }

    private sealed class NotificationObserver(ExploreDbContext context, Notifications notifications)
        : INotificationHandler<SettingChangedNotification>
    {
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Settings notifications must be published after commit.");
            notifications.Published.Add(notification);
            return Task.CompletedTask;
        }
    }
}
