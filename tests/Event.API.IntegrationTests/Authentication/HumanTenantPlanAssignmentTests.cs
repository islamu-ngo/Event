using System.Collections.Immutable;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("commit")]
    [Arguments("credential-after-terminal-read")]
    [Arguments("identity-after-terminal-read")]
    [Arguments("actor-substitution")]
    [Arguments("caught-replay")]
    [Arguments("corrupt-audit")]
    [Arguments("corrupt-lock")]
    public async Task ActualPlanOriginalRetainsAllGroupsLocksAuditAndHumanSettlement(string outcome)
    {
        var observation = new PlanObservation { Replay = outcome == "caught-replay" };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigurePlanObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        var command = await SeedOriginalPlanAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(command.TenantId), CancellationToken.None);
        var notices = new List<SettingChangedNotification>();
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        var locks = new PlanLocks(selected.Services.GetRequiredService<ISettingMutationLock>(), owner);
        bool sawAllEffects = false;
        observation.BeforeComplete = async services =>
        {
            var db = services.GetRequiredService<ExploreDbContext>();
            await Assert.That(notices.Count).IsEqualTo(0);
            var rows = await db.TenantSettingOverrides.AsNoTracking().Where(row => row.TenantId == command.TenantId).ToListAsync();
            await Assert.That(rows.Single(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpPort).Value).IsEqualTo("2525");
            await Assert.That(rows.Single(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpPort).IsLocked).IsTrue();
            await Assert.That(rows.Single(row => row.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode).IsLocked).IsTrue();
            await Assert.That(rows.Single(row => row.SettingKey == GovernanceSettingKeys.Events.UserSubmissionEnabled).IsLocked).IsTrue();
            await Assert.That(rows.Single(row => row.SettingKey == GovernanceSettingKeys.Events.CardClickOpensDetailPage).IsLocked).IsTrue();
            await Assert.That(await db.EmailDispatchTenantControls.Where(row => row.TenantId == command.TenantId)
                .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(1L);
            await Assert.That(await db.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
                .Select(row => row.UpdatedBy).SingleAsync()).IsEqualTo(command.AppliedByUserId);
            sawAllEffects = true;
            if (outcome == "corrupt-audit")
                await db.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedBy, (Guid?)Guid.CreateVersion7()));
            if (outcome == "corrupt-lock")
                await db.TenantSettingOverrides.Where(row => row.TenantId == command.TenantId
                    && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, false));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "credential-after-terminal-read") ExpireMixedCredential(fixture, selected.Services);
            if (outcome == "identity-after-terminal-read") request.Context.User = new ClaimsPrincipal(request.Context.User);
        };
        var handler = ActivatorUtilities.CreateInstance<ApplyInstanceTenantPlanAssignmentCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[new TypedNotifications(owner, notices)], locks);
        if (outcome == "actor-substitution") command = command with { AppliedByUserId = Guid.CreateVersion7() };
        if (outcome == "commit")
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await Assert.That(sawAllEffects).IsEqualTo(outcome is not ("actor-substitution" or "caught-replay"));
        await using var independent = fixture.Native.CreateDatabase();
        await Assert.That(await independent.TenantPlanAssignments.AsNoTracking().Where(row => row.Id == command.AssignmentId)
            .Select(row => row.UpdatedBy).SingleAsync()).IsEqualTo(outcome == "commit" ? command.AppliedByUserId : (Guid?)null);
        await Assert.That(await independent.TenantSettingOverrides.AsNoTracking().CountAsync(row => row.TenantId == command.TenantId
            && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort)).IsEqualTo(outcome == "commit" ? 1 : 0);
        await Assert.That(await independent.EmailDispatchTenantControls.AsNoTracking().CountAsync(row => row.TenantId == command.TenantId))
            .IsEqualTo(outcome == "commit" ? 1 : 0);
        await Assert.That(notices.Select(item => item.Key).ToArray()).IsEquivalentTo(outcome == "commit"
            ? new[] { GovernanceSettingKeys.Email.SmtpPort, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                GovernanceSettingKeys.Events.UserSubmissionEnabled, GovernanceSettingKeys.Events.CardClickOpensDetailPage } : []);
        if (outcome == "commit")
        {
            await Assert.That(string.Join("|", notices.Select(item => item.Key))).IsEqualTo(string.Join("|",
                GovernanceSettingKeys.Email.SmtpPort, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                GovernanceSettingKeys.Events.UserSubmissionEnabled, GovernanceSettingKeys.Events.CardClickOpensDetailPage));
            await Assert.That(locks.AcquiredOutsideTransaction).IsTrue();
            await Assert.That(locks.Groups.Length).IsEqualTo(4);
            await Assert.That(string.Join("|", locks.Groups[0])).IsEqualTo(string.Join("|", Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys));
            await Assert.That(string.Join("|", locks.Groups[1])).IsEqualTo(string.Join("|", EmailDeliverySettingKeys.All));
            await Assert.That(string.Join("|", locks.Groups[2])).IsEqualTo(string.Join("|", PublicationPolicySettingKeys.All));
            await Assert.That(locks.Groups[3]).IsEquivalentTo(new[] { GovernanceSettingKeys.Events.CardClickOpensDetailPage,
                GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes });
        }
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("assignment")]
    [Arguments("version")]
    [Arguments("setting")]
    [Arguments("quota")]
    [Arguments("ceiling")]
    public async Task PlanSelectionDriftBeforeAcquisitionNeverChangesTheObservedFootprint(string changed)
    {
        var observation = new PlanObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigurePlanObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        var command = await SeedOriginalPlanAsync(fixture);
        observation.AfterObservation = async () =>
        {
            await using var other = fixture.Native.CreateDatabase();
            Guid version = await other.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
                .Select(row => row.TenantPlanVersionId).SingleAsync();
            if (changed == "assignment")
                await other.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAt, fixture.Clock.GetUtcNow().UtcDateTime));
            if (changed == "version")
                await other.TenantPlanVersions.Where(row => row.Id == version)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActiveForProvisioning, false));
            if (changed == "setting")
                await other.TenantPlanVersionSettings.Where(row => row.TenantPlanVersionId == version
                    && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort).ExecuteDeleteAsync();
            if (changed == "quota")
                await other.TenantPlanVersionQuotas.Where(row => row.TenantPlanVersionId == version)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Limit, 12L));
            if (changed == "ceiling")
                await other.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "12"));
        };
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(command.TenantId), CancellationToken.None);
        var handler = ActivatorUtilities.CreateInstance<ApplyInstanceTenantPlanAssignmentCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
        if (changed == "ceiling")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        else
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).FailureCode).IsEqualTo("tenant_plan_selection_changed");
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingOverrides.CountAsync(row => row.TenantId == command.TenantId
            && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort)).IsEqualTo(0);
        await Assert.That(await observer.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
            .Select(row => row.UpdatedBy).SingleAsync()).IsNull();
    }

    [Test]
    [Arguments("publication")]
    [Arguments("visitor")]
    [Arguments("visitor-account-required")]
    [Arguments("ordinary-ceiling")]
    [Arguments("quota-ceiling")]
    [Arguments("caught-rejection")]
    public async Task RejectedPlanGroupsRollbackPriorSettingsRevisionsAndAssignmentAudit(string rejection)
    {
        var observation = new PlanObservation { CatchRejection = rejection == "caught-rejection" };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigurePlanObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        var command = await SeedOriginalPlanAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            if (rejection is "publication" or "caught-rejection")
                await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Events.UserSubmissionEnabled)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true));
            if (rejection == "visitor")
                await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true));
            if (rejection == "visitor-account-required")
            {
                Guid version = await seed.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
                    .Select(row => row.TenantPlanVersionId).SingleAsync();
                await seed.TenantPlanVersionSettings.Where(row => row.TenantPlanVersionId == version
                    && row.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.JsonValue, "\"AnonymousOnly\""));
                var actor = await seed.Actors.SingleAsync(row => row.UserId == command.AppliedByUserId);
                var source = new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    TenantId = command.TenantId,
                    Tenant = null!,
                    ActorId = actor.Id,
                    Actor = null!,
                    Title = "Plan visitor invariant",
                    EventStatus = null!,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Local,
                    EventFormat = null!,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                source.ParticipationConfiguration = EventParticipationConfiguration.Create(source.Id, command.TenantId,
                    (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                    (int)IdentityAccessModeEnum.AccountRequired, null, fixture.Clock.GetUtcNow().UtcDateTime);
                seed.Events.Add(source);
                await seed.SaveChangesAsync();
            }
            if (rejection == "ordinary-ceiling")
                await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Events.CardClickOpensDetailPage)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true));
            if (rejection == "quota-ceiling")
                await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "1"));
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(command.TenantId), CancellationToken.None);
        var notices = new List<SettingChangedNotification>();
        var handler = ActivatorUtilities.CreateInstance<ApplyInstanceTenantPlanAssignmentCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)
                [new TypedNotifications(selected.Services.GetRequiredService<ExploreDbContext>(), notices)]);
        if (observation.CatchRejection)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        else
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingOverrides.CountAsync(row => row.TenantId == command.TenantId
            && (row.SettingKey == GovernanceSettingKeys.Email.SmtpPort
                || row.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode
                || row.SettingKey == GovernanceSettingKeys.Events.CardClickOpensDetailPage))).IsEqualTo(0);
        await Assert.That(await observer.EmailDispatchTenantControls.CountAsync(row => row.TenantId == command.TenantId)).IsEqualTo(0);
        await Assert.That(await observer.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId).Select(row => row.UpdatedBy).SingleAsync()).IsNull();
        await Assert.That(notices.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PlanRetainsOriginalInstanceUpdateSupportHorizonDespiteExtension(bool extend)
    {
        var observation = new PlanObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigurePlanObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        var command = await SeedOriginalPlanAsync(fixture);
        var horizon = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid session;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.Enabled, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true");
            var support = SupportAccessSession.Start(command.AppliedByUserId, command.TenantId,
                SupportAccessModeEnum.Write, "maintenance", "Original plan support", "plan-support",
                fixture.Clock.GetUtcNow().AddMinutes(-1), horizon);
            session = support.Id;
            seed.SupportAccessSessions.Add(support);
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = session.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(command.TenantId), CancellationToken.None);
        observation.BeforeComplete = async services =>
        {
            if (extend)
                await services.GetRequiredService<ExploreDbContext>().SupportAccessSessions.Where(row => row.Id == session)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, horizon.AddHours(1)));
        };
        observation.AfterTerminalRead = () => fixture.Clock.Advance(horizon.AddTicks(1) - fixture.Clock.GetUtcNow());
        var handler = ActivatorUtilities.CreateInstance<ApplyInstanceTenantPlanAssignmentCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await using var independent = fixture.Native.CreateDatabase();
        await Assert.That(await independent.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId).Select(row => row.UpdatedBy).SingleAsync()).IsNull();
        await Assert.That(await independent.SupportAccessSessions.Where(row => row.Id == session).Select(row => row.ExpiresAtUtc).SingleAsync()).IsEqualTo(horizon);
    }

    private static async Task<ApplyInstanceTenantPlanAssignmentCommand> SeedOriginalPlanAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        Guid tenant = PlatformDefaults.DefaultTenantId;
        Guid actor = fixture.Binding.LocalSubjectId;
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        foreach (var key in PublicationPolicySettingKeys.All) await SeedSpecializedSettingAsync(seed, key, "true");
        await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
        await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Email.DeliveryEnabled, "false");
        await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, "\"AnonymousOnly\"");
        await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Events.CardClickOpensDetailPage, "true");
        await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "4096");
        string[] keys = [GovernanceSettingKeys.Email.SmtpPort, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            GovernanceSettingKeys.Events.UserSubmissionEnabled, GovernanceSettingKeys.Events.CardClickOpensDetailPage];
        await seed.TenantSettingOverrides.Where(row => row.TenantId == tenant && keys.Contains(row.SettingKey)).ExecuteDeleteAsync();
        await seed.EmailDispatchTenantControls.Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        var plan = new TenantPlan { Id = Guid.CreateVersion7(), Key = $"original-{Guid.CreateVersion7():N}", DisplayName = "Original plan", CreatedAt = now };
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
            CreatedAt = now
        };
        string[] values = ["2525", "\"FullRegistrationAndAuth\"", "false", "false"];
        for (int index = 0; index < keys.Length; index++)
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersionId = version.Id,
                SettingKey = keys[index],
                JsonValue = values[index],
                IsLocked = true,
                CreatedAt = now
            });
        version.Quotas.Add(new TenantPlanVersionQuota
        {
            Id = Guid.CreateVersion7(),
            TenantPlanVersionId = version.Id,
            QuotaKey = TenantPlanQuotaKeys.StorageBytes,
            Limit = 1024,
            CreatedAt = now
        });
        var assignment = new TenantPlanAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            TenantPlanId = plan.Id,
            TenantPlan = plan,
            TenantPlanVersionId = version.Id,
            TenantPlanVersion = version,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = actor,
            AssignedAt = now,
            CreatedAt = now
        };
        seed.AddRange(plan, version, assignment, new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            Tenant = null!,
            SettingKey = GovernanceSettingKeys.Events.UserSubmissionEnabled,
            Value = "true",
            IsLocked = true,
            CreatedAt = now,
            CreatedBy = actor
        });
        await seed.SaveChangesAsync();
        return new(tenant, assignment.Id, actor);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PlanQuotaOnlyAuditsWithoutInventingSettingsWhileEmptyPlanRemainsNoOp(bool quotaOnly)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => services.AddHumanMixedSettingsSources(IdentityDatabaseTopology.Colocated));
        await SeedPlatformAdministratorAsync(fixture);
        var command = await SeedOriginalPlanAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            Guid version = await seed.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
                .Select(row => row.TenantPlanVersionId).SingleAsync();
            await seed.TenantPlanVersionSettings.Where(row => row.TenantPlanVersionId == version).ExecuteDeleteAsync();
            if (!quotaOnly) await seed.TenantPlanVersionQuotas.Where(row => row.TenantPlanVersionId == version).ExecuteDeleteAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(command.TenantId), CancellationToken.None);
        var notices = new List<SettingChangedNotification>();
        var handler = ActivatorUtilities.CreateInstance<ApplyInstanceTenantPlanAssignmentCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)
                [new TypedNotifications(selected.Services.GetRequiredService<ExploreDbContext>(), notices)]);
        await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsTrue();
        await using var independent = fixture.Native.CreateDatabase();
        await Assert.That(await independent.TenantPlanAssignments.Where(row => row.Id == command.AssignmentId)
            .Select(row => row.UpdatedBy).SingleAsync()).IsEqualTo(quotaOnly ? command.AppliedByUserId : (Guid?)null);
        await Assert.That(await independent.TenantSettingOverrides.CountAsync(row => row.TenantId == command.TenantId
            && row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes)).IsEqualTo(0);
        await Assert.That(await independent.EmailDispatchTenantControls.CountAsync(row => row.TenantId == command.TenantId)).IsEqualTo(0);
        await Assert.That(notices.Count).IsEqualTo(0);
    }

    private static void ConfigurePlanObservation(IServiceCollection services, PlanObservation observation)
    {
        services.AddHumanMixedSettingsSources(IdentityDatabaseTopology.Colocated);
        var registration = services.Last(item => item.ServiceType == typeof(IHumanMixedSettingsSourceFactory));
        services.RemoveAll<IHumanMixedSettingsSourceFactory>();
        services.AddScoped<IHumanMixedSettingsSourceFactory>(provider =>
            new PlanSourceFactory((IHumanMixedSettingsSourceFactory)registration.ImplementationFactory!(provider), observation));
        services.AddScoped<TypedSettingsOriginalStore>();
        services.RemoveAll<ITypedSettingsOriginalStore>();
        services.AddScoped<ITypedSettingsOriginalStore>(provider =>
            new PlanStoreObserver(provider.GetRequiredService<TypedSettingsOriginalStore>(), provider, observation));
    }

    private sealed class PlanObservation
    {
        internal Func<Task>? AfterObservation { get; set; }
        internal Func<IServiceProvider, Task>? BeforeComplete { get; set; }
        internal Action? AfterTerminalRead { get; set; }
        internal bool Finalizing { get; set; }
        internal bool TerminalObserved { get; set; }
        internal bool Replay { get; init; }
        internal bool CatchRejection { get; init; }
    }

    private sealed class PlanLocks(ISettingMutationLock inner, ExploreDbContext context) : ISettingMutationLock
    {
        internal string[][] Groups { get; private set; } = [];
        internal bool AcquiredOutsideTransaction { get; private set; }
        public Task<T> ExecuteAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken token = default) =>
            inner.ExecuteAsync(key, operation, token);
        public Task<T> ExecuteManyAsync<T>(IEnumerable<string> keys, Func<CancellationToken, Task<T>> operation, CancellationToken token = default) =>
            inner.ExecuteManyAsync(keys, operation, token);
        public Task<T> ExecuteOrderedGroupsAsync<T>(IEnumerable<IEnumerable<string>> groups,
            Func<CancellationToken, Task<T>> operation, CancellationToken token = default)
        {
            Groups = groups.Select(group => group.ToArray()).ToArray();
            return inner.ExecuteOrderedGroupsAsync(Groups, acquired =>
            {
                AcquiredOutsideTransaction = context.Database.CurrentTransaction is null;
                return operation(acquired);
            }, token);
        }
    }

    private sealed class PlanSourceFactory(IHumanMixedSettingsSourceFactory inner, PlanObservation observation) : IHumanMixedSettingsSourceFactory
    {
        public Task<IHumanMixedSettingsSourceProtocol> CreateAsync(UpdateSettingBatchCommand command, CancellationToken token) => inner.CreateAsync(command, token);
        public async Task<IHumanMixedSettingsSourceProtocol> CreateAsync(TypedSettingsOriginal original, CancellationToken token) =>
            new PlanSourceObserver(await inner.CreateAsync(original, token), observation);
    }

    private sealed class PlanSourceObserver(IHumanMixedSettingsSourceProtocol inner, PlanObservation observation) : IHumanMixedSettingsSourceProtocol
    {
        public HumanCommitSubject Subject => inner.Subject;
        public Explore.Application.Authentication.HumanAuthenticationLifetime Lifetime => inner.Lifetime;
        public HumanSupportForwardingObservation Forwarding => inner.Forwarding;
        public Guid? TenantId => inner.TenantId;
        public string OriginalData => inner.OriginalData;
        public async Task<HumanEmailSourceFacts> ObserveAsync(CancellationToken token)
        {
            var result = await inner.ObserveAsync(token);
            if (observation.AfterObservation is { } after) await after();
            return result;
        }
        public Task<HumanEmailSourceFacts> AcquireAsync(CancellationToken token) => inner.AcquireAsync(token);
        public async Task<HumanEmailSourceFacts> RequireOwnedAsync(CancellationToken token)
        {
            var result = await inner.RequireOwnedAsync(token);
            if (observation.Finalizing && !observation.TerminalObserved)
            {
                observation.TerminalObserved = true;
                observation.AfterTerminalRead?.Invoke();
            }
            return result;
        }
    }

    private sealed class PlanStoreObserver(TypedSettingsOriginalStore inner, IServiceProvider services, PlanObservation observation)
        : ITypedSettingsOriginalStore, ITypedTenantPlanOriginalStore
    {
        public TenantPlanAssignmentSelection SelectPlan(TypedSettingsOriginal original) => inner.SelectPlan(original);
        public async Task<TenantPlanAssignmentApplication> ApplyPlanAsync(TypedSettingsOriginal original, CancellationToken token)
        {
            TenantPlanAssignmentApplication result;
            try { result = await inner.ApplyPlanAsync(original, token); }
            catch (TenantPlanOriginalRejectedException exception) when (observation.CatchRejection) { return new(exception.Response, []); }
            if (observation.Replay) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inner.ApplyPlanAsync(original, token));
            return result;
        }
        public Task ObserveAsync(TypedSettingsOriginal original, IHumanMixedSettingsSource source, CancellationToken token) => inner.ObserveAsync(original, source, token);
        public Task<ImmutableArray<TypedSettingsEffect>> AcquireAndCompileAsync(TypedSettingsOriginal original, CancellationToken token) => inner.AcquireAndCompileAsync(original, token);
        public Task<PublicationPolicyMutationResult> ApplyEventPolicyAsync(TypedSettingsOriginal original, CancellationToken token) => inner.ApplyEventPolicyAsync(original, token);
        public Task<IReadOnlyList<SettingChangedNotification>> ApplyOnboardingAsync(TypedSettingsOriginal original, CancellationToken token) => inner.ApplyOnboardingAsync(original, token);
        public Task<PublicationPolicyMutationResult> ApplyReportingAsync(TypedSettingsOriginal original, CancellationToken token) => inner.ApplyReportingAsync(original, token);
        public Task<IReadOnlyList<SettingChangedNotification>> ApplyGovernanceAsync(TypedSettingsOriginal original, CancellationToken token) => inner.ApplyGovernanceAsync(original, token);
        public void InvalidateGovernanceAfterCommit(TypedSettingsOriginal original) => inner.InvalidateGovernanceAfterCommit(original);
        public async Task RequireCompleteAsync(TypedSettingsOriginal original, CancellationToken token)
        {
            if (observation.BeforeComplete is { } before) await before(services);
            observation.Finalizing = true;
            await inner.RequireCompleteAsync(original, token);
        }
        public Task VerifyAsync(TypedSettingsOriginal original, CancellationToken token) => inner.VerifyAsync(original, token);
        public void RequireObservedData(TypedSettingsOriginal original, string data) => inner.RequireObservedData(original, data);
        public Task RequirePublicationAsync(TypedSettingsPublication publication, CancellationToken token) => inner.RequirePublicationAsync(publication, token);
        public Task<CoordinatedSettingMutationWriteResult> WritePublicationAsync(TypedSettingsPublication publication, CancellationToken token) => inner.WritePublicationAsync(publication, token);
        public void Close(TypedSettingsOriginal original) => inner.Close(original);
    }
}
