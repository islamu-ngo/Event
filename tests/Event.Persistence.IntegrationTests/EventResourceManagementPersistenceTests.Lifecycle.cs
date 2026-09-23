using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceManagementPersistenceTests
{
    [Test]
    public async Task ForeignTenantCreateIdentityCollisionReturnsOnlyConflict()
    {
        var (scope, actor) = await SeedAsync();
        var foreign = EventResourcePersistenceTests.CreateDraft(scope.TenantBId, scope.EventBId);
        await using (var seed = database.CreateContext())
        {
            seed.EventResources.Add(foreign);
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var result = await Workflow(context, scope.TenantAId, actor)
            .CreateAsync(scope.EventAId, foreign.Id, Draft(), default);
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(result.Message).IsNull();
        await using var verify = database.CreateContext();
        var saved = await verify.EventResources.SingleAsync(value => value.Id == foreign.Id);
        await Assert.That(saved.TenantId).IsEqualTo(scope.TenantBId);
        await Assert.That(saved.EventId).IsEqualTo(scope.EventBId);
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == foreign.Id)).IsFalse();
    }

    [Test]
    public async Task RequiredAuditFailureRollsBackMetadataAndPolicyReplacement()
    {
        var (scope, actor) = await SeedAsync();
        var id = Guid.CreateVersion7();
        Guid version;
        await using (var seed = database.CreateContext())
        {
            var workflow = Workflow(seed, scope.TenantAId, actor);
            await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
            version = (await workflow.GetAsync(id, default)).Value!.Version;
        }
        await using (var context = database.CreateContext(new AuditFailure()))
        {
            var workflow = Workflow(context, scope.TenantAId, actor);
            await Assert.That(async () => await workflow.UpdateAsync(id, version,
                Draft() with { Title = "Changed", AudienceRules = [new(EventResourceAudienceKindEnum.EventStaff)] }, default))
                .Throws<DbUpdateException>();
        }
        await using var verify = database.CreateContext();
        var saved = await verify.EventResources.Include(value => value.AudienceRules).SingleAsync(value => value.Id == id);
        await Assert.That(saved.ConcurrencyStamp).IsEqualTo(version);
        await Assert.That(saved.Title).IsEqualTo(Draft().Title);
        await Assert.That(saved.AudienceRules.Single().AudienceKindId).IsEqualTo((int)EventResourceAudienceKindEnum.Public);
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id)).IsEqualTo(1);
    }

    [Test]
    public async Task ArchivedDraftCanBeDeletedWithoutReopeningItsMetadata()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor);
        var id = Guid.CreateVersion7();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        var first = (await workflow.GetAsync(id, default)).Value!;
        await Assert.That((await workflow.ChangeStateAsync(id, first.Version,
            EventResourceManagementAction.Archive, default)).IsSuccess).IsTrue();
        var archived = (await workflow.GetAsync(id, default)).Value!;
        await Assert.That((await workflow.UpdateAsync(id, archived.Version, Draft(), default)).IsSuccess).IsFalse();
        await Assert.That((await workflow.ChangeStateAsync(id, archived.Version,
            EventResourceManagementAction.Delete, default)).IsSuccess).IsTrue();
        await using var verification = database.CreateContext();
        await Assert.That(await verification.EventResources.AnyAsync(row => row.Id == id)).IsFalse();
        await Assert.That(await verification.EventResourceAuditEntries.CountAsync(row => row.EventResourceId == id)).IsEqualTo(3);
    }

    [Test]
    public async Task DeletedIdentityCannotBeRecreatedAndLostDeleteResponseDoesNotDuplicateAudit()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor);
        var id = Guid.CreateVersion7();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsTrue();
        var version = (await workflow.GetAsync(id, default)).Value!.Version;
        await Assert.That((await workflow.ChangeStateAsync(id, version, EventResourceManagementAction.Delete, default)).IsSuccess).IsTrue();
        await Assert.That((await workflow.ChangeStateAsync(id, version, EventResourceManagementAction.Delete, default)).IsSuccess).IsFalse();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id, Draft(), default)).IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.IncludeDeleted().CountAsync(value => value.Id == id)).IsEqualTo(1);
        await Assert.That(await verify.EventResources.AnyAsync(value => value.Id == id)).IsFalse();
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id)).IsEqualTo(2);
    }

    [Test]
    public async Task ScopedModeratorCanOnlyWithdrawWithoutManagementOrAuditAuthority()
    {
        var (scope, actor) = await SeedAsync();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        resource.SetStoredFile(scope.StorageAId, resource.ConcurrencyStamp, actor, Now);
        resource.Publish(new(scope.TenantAId, scope.EventAId, null, EventStatusEnum.Published,
            false, true, null, false, new(new(Now), new(Now.AddHours(1)), null, null)),
            true, resource.ConcurrencyStamp, actor, Now);
        await using (var seed = database.CreateContext())
        {
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.OrganizerActorId = null;
            var member = await seed.TenantUsers.SingleAsync(value => value.TenantId == scope.TenantAId && value.UserId == actor);
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, TenantUserId = member.Id,
                TenantUser = member, RoleId = (int)RoleEnum.TenantAdmin, Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = Now
            });
            seed.EventResources.Add(resource);
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor);
        await Assert.That((await workflow.GetAsync(resource.Id, default)).Value).IsNull();
        await Assert.That((await workflow.GetAuditAsync(resource.Id, 100, default)).Value).IsNull();
        await Assert.That((await workflow.ChangeStateAsync(resource.Id, resource.ConcurrencyStamp,
            EventResourceManagementAction.Unpublish, default)).IsSuccess).IsFalse();
        await Assert.That((await workflow.ChangeStateAsync(resource.Id, resource.ConcurrencyStamp,
            EventResourceManagementAction.Moderate, default)).IsSuccess).IsTrue();
        await using var verify = database.CreateContext();
        var saved = await verify.EventResources.SingleAsync(value => value.Id == resource.Id);
        await Assert.That(saved.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Withdrawn);
        var audit = await verify.EventResourceAuditEntries.SingleAsync(value => value.EventResourceId == resource.Id);
        await Assert.That(audit.Action).IsEqualTo(EventResourceAuditAction.Moderate);
        await Assert.That(audit.Reason).IsEqualTo(EventResourceAuditReason.Moderation);
    }

    [Test]
    public async Task SeededPublishableExternalPayloadStillCannotBePublished()
    {
        var (scope, actor) = await SeedAsync();
        var policy = EventResourceGovernancePolicy.Create(Enum.GetValues<EventResourceDeliveryTypeEnum>(),
            Enum.GetValues<EventResourceAudienceKindEnum>(), [EventResourceGovernancePolicy.PdfMediaType],
            10_485_760, false, ["https://material.example"], 30, 500, long.MaxValue);
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor, policy);
        var id = Guid.CreateVersion7();
        await Assert.That((await workflow.CreateAsync(scope.EventAId, id,
            Draft() with { DeliveryType = EventResourceDeliveryTypeEnum.ExternalLink }, default)).IsSuccess).IsTrue();
        var resource = await context.EventResources.SingleAsync(value => value.Id == id);
        resource.SetExternalDestination(Guid.CreateVersion7().ToString("N"), 1, "https://material.example",
            resource.ConcurrencyStamp, actor, Now);
        var parent = await context.Events.SingleAsync(value => value.Id == scope.EventAId);
        parent.Publish(Now);
        await context.SaveChangesAsync();
        var result = await workflow.ChangeStateAsync(id, resource.ConcurrencyStamp, EventResourceManagementAction.Publish, default);
        await Assert.That(result.FailureCode).IsEqualTo("event_resource_publication_unavailable");
        await using var verify = database.CreateContext();
        await Assert.That((await verify.EventResources.SingleAsync(value => value.Id == id)).PublicationStateId)
            .IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id)).IsEqualTo(1);
    }
}
