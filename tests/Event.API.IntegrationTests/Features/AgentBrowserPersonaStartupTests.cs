using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class AgentBrowserPersonaStartupTests
{
    [Test]
    public async Task DuplicateSourcesBootstrapOnceWithoutReviewerGrantsOrChangingExistingListings()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var sources = await database.Events.Include(row => row.Sessions)
                .Where(row => row.Id == AgentBrowserPersonaCatalog.DiscoverySourceId
                    || row.Id == AgentBrowserPersonaCatalog.DuplicateDiscoverySourceId)
                .OrderBy(row => row.PublicCode).ToListAsync();
            await Assert.That(sources.Count).IsEqualTo(2);
            await Assert.That(sources[0].Title).IsEqualTo(sources[1].Title);
            await Assert.That(sources[0].PublicCode).IsNotEqualTo(sources[1].PublicCode);
            await Assert.That(sources[0].Slug).IsNotEqualTo(sources[1].Slug);
            foreach (var source in sources)
            {
                await Assert.That(source.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
                await Assert.That(source.VisibilityTypeId).IsEqualTo((int)VisibilityTypeEnum.Public);
                await Assert.That(source.EventProvenanceTypeId).IsEqualTo((int)EventProvenanceTypeEnum.OrganizerCreated);
                await Assert.That(source.Sessions.Single().StartTime)
                    .IsEqualTo(AgentBrowserPersonaCatalog.DiscoveryOccurrence);
                await Assert.That(await database.EventRoleAssignments.CountAsync(row => row.EventId == source.Id
                    && row.UserId == AgentBrowserPersonaCatalog.Organizer.SubjectId
                    && row.RoleId == (int)RoleEnum.EventOwner)).IsEqualTo(1);
            }
            await Assert.That(await database.EventRoleAssignments.AnyAsync(
                row => row.RoleId == (int)RoleEnum.EventDiscoveryReviewer)).IsFalse();
        }
        Guid unrelated = await fixture.CreateSameTenantUnrelatedEventAsync();
        fixture.RemoveInitializationSecrets();
        await fixture.RunAsync();
        await fixture.AssertReadyAsync();
        await using var replay = fixture.CreateDatabase();
        await Assert.That(await replay.Events.CountAsync()).IsEqualTo(5);
        await Assert.That((await replay.Events.SingleAsync(row => row.Id == unrelated)).Title)
            .IsEqualTo("Another organization's event");
        var original = await replay.Events.SingleAsync(row => row.Id == AgentBrowserPersonaCatalog.EventId);
        await Assert.That(original.Title).IsEqualTo("Agent browser event");
        await Assert.That(original.PublicCode).IsEqualTo("agent0000303");
        await Assert.That(original.Slug).IsEqualTo("agent-browser");
        await Assert.That(await replay.EventRoleAssignments.AnyAsync(
            row => row.RoleId == (int)RoleEnum.EventDiscoveryReviewer)).IsFalse();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("actor")]
    [Arguments("organizer")]
    [Arguments("provenance")]
    [Arguments("reference")]
    [Arguments("session")]
    [Arguments("role")]
    [Arguments("owner")]
    [Arguments("grantor")]
    [Arguments("missing-grant")]
    [Arguments("parent-actor")]
    [Arguments("parent-membership")]
    [Arguments("source-metadata")]
    [Arguments("grant-status")]
    [Arguments("occurrence")]
    public async Task DuplicateFixtureCannotAdoptOrRepairInconsistentSourceAuthority(string fault)
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var source = await database.Events.Include(row => row.Sessions).Include(row => row.Days)
                .SingleAsync(row => row.Id == AgentBrowserPersonaCatalog.DiscoverySourceId);
            var owner = await database.EventRoleAssignments.SingleAsync(row => row.EventId == source.Id);
            switch (fault)
            {
                case "tenant":
                    database.EventRoleAssignments.Remove(owner);
                    await database.SaveChangesAsync();
                    await database.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM islamu_event.events WHERE id = {source.Id}");
                    database.ChangeTracker.Clear();
                    source.TenantId = AgentBrowserPersonaCatalog.NegativeTenantId;
                    source.Tenant = null!;
                    foreach (var session in source.Sessions)
                    {
                        session.TenantId = AgentBrowserPersonaCatalog.NegativeTenantId;
                        session.Tenant = null!;
                    }
                    foreach (var day in source.Days)
                    {
                        day.TenantId = AgentBrowserPersonaCatalog.NegativeTenantId;
                        day.Tenant = null!;
                    }
                    owner.TenantId = AgentBrowserPersonaCatalog.NegativeTenantId;
                    owner.Tenant = null!;
                    database.Events.Add(source);
                    database.EventRoleAssignments.Add(owner);
                    break;
                case "actor": source.ActorId = AgentBrowserPersonaCatalog.NegativeOrganizationActorId; break;
                case "organizer": source.OrganizerActorId = AgentBrowserPersonaCatalog.NegativeOrganizationActorId; break;
                case "provenance": source.EventProvenanceTypeId = (int)EventProvenanceTypeEnum.CommunityReported; break;
                case "reference": source.PublicCode = "foreign00340"; break;
                case "session":
                    var foreignSession = source.Sessions.Single();
                    await database.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM islamu_event.event_sessions WHERE id = {foreignSession.Id}");
                    database.ChangeTracker.Clear();
                    foreignSession.TenantId = AgentBrowserPersonaCatalog.NegativeTenantId;
                    foreignSession.Tenant = null!;
                    foreignSession.EventId = AgentBrowserPersonaCatalog.NegativeEventId;
                    foreignSession.Event = null!;
                    foreignSession.EventDayId = null;
                    foreignSession.EventDay = null;
                    database.EventSessions.Add(foreignSession);
                    break;
                case "role": owner.RoleId = (int)RoleEnum.EventManager; break;
                case "owner": owner.UserId = AgentBrowserPersonaCatalog.Manager.SubjectId; break;
                case "grantor": owner.CreatedBy = AgentBrowserPersonaCatalog.Moderator.SubjectId; break;
                case "missing-grant": database.EventRoleAssignments.Remove(owner); break;
                case "parent-actor":
                    var foreignOrganization = new Organization
                    {
                        Id = Guid.CreateVersion7(),
                        Pii = new OrganizationPii { FullName = "Foreign fixture parent" },
                        CreatedAt = DateTime.UtcNow
                    };
                    database.Organizations.Add(foreignOrganization);
                    (await database.Actors.SingleAsync(row => row.Id == AgentBrowserPersonaCatalog.OrganizationActorId))
                        .OrganizationId = foreignOrganization.Id;
                    break;
                case "parent-membership":
                    (await database.OrganizationMembers.SingleAsync(row => row.Id == AgentBrowserPersonaCatalog.Id(420)))
                        .RoleId = (int)RoleEnum.OrgMember;
                    break;
                case "source-metadata": source.ProvenanceExternalId = "foreign-source"; break;
                case "grant-status": owner.Status = EventRoleAssignmentStatus.Pending; break;
                case "occurrence":
                    var shifted = AgentBrowserPersonaCatalog.DiscoveryOccurrence.AddDays(1);
                    source.Sessions.Single().Reschedule(
                        Explore.Domain.ValueObjects.UtcInstantRange.Create(shifted, shifted.AddHours(2)), "UTC",
                        new Explore.Domain.Services.Scheduling.EventScheduleProjectionCalculator());
                    source.RecalculateScheduleSummaryFromSessions();
                    break;
            }
            await database.SaveChangesAsync();
        }
        fixture.RemoveInitializationSecrets();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var rejected = fixture.CreateDatabase();
        await Assert.That(await rejected.EventRoleAssignments.CountAsync())
            .IsEqualTo(fault == "missing-grant" ? 4 : 5);
        await Assert.That(await rejected.EventRoleAssignments.AnyAsync(
            row => row.RoleId == (int)RoleEnum.EventDiscoveryReviewer)).IsFalse();
    }

    [Test]
    public async Task DuplicateFixtureReplayDoesNotRestoreRevokedOwnerAuthority()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var owner = await database.EventRoleAssignments.SingleAsync(
                row => row.EventId == AgentBrowserPersonaCatalog.DiscoverySourceId);
            owner.Revoke(AgentBrowserPersonaCatalog.Organizer.SubjectId, DateTime.UtcNow);
            await database.SaveChangesAsync();
        }
        fixture.RemoveInitializationSecrets();
        await fixture.RunAsync();
        await using var replay = fixture.CreateDatabase();
        await Assert.That((await replay.EventRoleAssignments.SingleAsync(
            row => row.EventId == AgentBrowserPersonaCatalog.DiscoverySourceId)).Status)
            .IsEqualTo(EventRoleAssignmentStatus.Revoked);
        await Assert.That(await replay.EventRoleAssignments.CountAsync()).IsEqualTo(5);
    }

    [Test]
    public async Task ExplicitModeratorReviewerGrantSurvivesFixtureReplayWithoutBeingDuplicated()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        Guid assignmentId;
        await using (var database = fixture.CreateDatabase())
        {
            DateTime now = DateTime.UtcNow;
            var assignment = EventRoleAssignment.Create(AgentBrowserPersonaCatalog.TenantId,
                AgentBrowserPersonaCatalog.DiscoverySourceId, AgentBrowserPersonaCatalog.Moderator.SubjectId,
                (int)RoleEnum.EventDiscoveryReviewer, EventRoleAssignmentStatus.Active, now, null,
                AgentBrowserPersonaCatalog.Organizer.SubjectId);
            assignmentId = assignment.Id;
            database.EventRoleAssignments.Add(assignment);
            await database.SaveChangesAsync();
        }
        fixture.RemoveInitializationSecrets();
        await fixture.RunAsync();
        await using var replay = fixture.CreateDatabase();
        await Assert.That(await replay.EventRoleAssignments.CountAsync(
            row => row.RoleId == (int)RoleEnum.EventDiscoveryReviewer)).IsEqualTo(1);
        await Assert.That((await replay.EventRoleAssignments.SingleAsync(row => row.Id == assignmentId)).CreatedBy)
            .IsEqualTo(AgentBrowserPersonaCatalog.Organizer.SubjectId);
    }

    [Test]
    [Arguments("marker")]
    [Arguments("receipt")]
    [Arguments("graph")]
    [Arguments("activation")]
    [Arguments("replacement")]
    [Arguments("sources")]
    public async Task InterruptedPersistedBoundariesResumeOnlyTheRecordedLifecycle(string boundary)
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        fixture.InterruptAt(boundary);
        if (boundary == "replacement") await fixture.RunAsync();
        else await Assert.That(() => fixture.RunAsync()).Throws<AgentBrowserPersonaFixture.InjectedBoundaryFailure>();
        await Assert.That(fixture.InterruptionObserved).IsTrue();
        await fixture.RunAsync();
        await fixture.AssertReadyAsync();
    }

    [Test]
    public async Task NativeProvisioningCompletesSixExactBindingsAndReplayDoesNotRegrantOrReset()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await fixture.AssertReadyAsync();
        await fixture.ChangePasswordProfileAndRevokeGrantAsync();
        fixture.RemoveInitializationSecrets();
        await fixture.RunAsync();
        await fixture.AssertReadyAsync();
        await fixture.AssertChangesPreservedAsync();
    }

    [Test]
    public async Task FreshFoundationEnablesTenantHostsWithoutResettingOperatorChangesOnReplay()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var baseDomain = await database.Set<SystemSetting>().SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain);
            var subdomain = await database.Set<SystemSetting>().SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Routing.ResolverSubdomainEnabled);
            var positiveHost = await database.TenantSettingOverrides.SingleAsync(row =>
                row.TenantId == AgentBrowserPersonaCatalog.TenantId
                && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain);
            var negativeHost = await database.TenantSettingOverrides.SingleAsync(row =>
                row.TenantId == AgentBrowserPersonaCatalog.NegativeTenantId
                && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain);
            await Assert.That(baseDomain.Value).IsEqualTo("\"localhost\"");
            await Assert.That(subdomain.Value).IsEqualTo("true");
            await Assert.That(positiveHost.Value).IsEqualTo("\"default\"");
            await Assert.That(negativeHost.Value).IsEqualTo("\"agent-negative\"");
            baseDomain.Value = "\"operator.localhost\"";
            subdomain.Value = "false";
            positiveHost.Value = "\"operator-default\"";
            await database.SaveChangesAsync();
        }

        fixture.RemoveInitializationSecrets();
        await fixture.RunAsync();
        await using var replay = fixture.CreateDatabase();
        await Assert.That((await replay.Set<SystemSetting>().SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain)).Value)
            .IsEqualTo("\"operator.localhost\"");
        await Assert.That((await replay.Set<SystemSetting>().SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Routing.ResolverSubdomainEnabled)).Value)
            .IsEqualTo("false");
        await Assert.That((await replay.TenantSettingOverrides.SingleAsync(row =>
            row.TenantId == AgentBrowserPersonaCatalog.TenantId
            && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain)).Value)
            .IsEqualTo("\"operator-default\"");
    }

    [Test]
    [Arguments("persona")]
    [Arguments("bootstrap")]
    [Arguments("signing")]
    [Arguments("malformed-signing")]
    [Arguments("mismatched-signing")]
    public async Task AllRequiredSecretsAreCheckedBeforeOwnershipMarkerOrGrantWrites(string fault)
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        fixture.BreakSecret(fault);
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That(await database.InstanceBootstrapStates.AnyAsync()).IsFalse();
        await Assert.That(await database.Users.AnyAsync()).IsFalse();
        await Assert.That(await database.PlatformUserRoles.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task PendingMarkerCannotAdoptForeignOrdinaryContent()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        fixture.InterruptAt("marker");
        await Assert.That(() => fixture.RunAsync()).Throws<AgentBrowserPersonaFixture.InjectedBoundaryFailure>();
        await fixture.InsertForeignUserAsync();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That((await database.InstanceBootstrapStates.SingleAsync()).Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(await database.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.PlatformUserRoles.AnyAsync()).IsFalse();
        await Assert.That(await database.Tenants.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ForeignDatabaseWithoutDefaultTenantIsNotAdopted()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.InsertForeignUserAsync();
        await fixture.MigrateAsync();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That(await database.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.InstanceBootstrapStates.AnyAsync()).IsFalse();
        await Assert.That(await database.PlatformUserRoles.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task CompetingSessionLockFailsWithoutMutation()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await using var competingLock = await fixture.AcquireProvisioningLockAsync();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That(await database.InstanceBootstrapStates.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task MissingCompletedActorCannotResurrectCredentialOrGrants()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var actor = await database.Actors.SingleAsync(row => row.UserId == AgentBrowserPersonaCatalog.Attendee.SubjectId);
            actor.IsDeleted = true;
            await database.SaveChangesAsync();
        }
        fixture.RemoveInitializationSecrets();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
    }
}
