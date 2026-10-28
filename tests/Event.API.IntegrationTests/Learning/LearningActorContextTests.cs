using System.Net;
using System.Net.Http.Json;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.EntityFrameworkCore;

namespace Event.API.IntegrationTests.Learning;

[ClassDataSource<LearningHttpFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class LearningActorContextTests(LearningHttpFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryActorMemberLosesSuspendedPreviewButCanClearTheirOwnOrphan(bool group)
    {
        using var member = await fixture.CreateMemberAsync();
        var target = await CreateActorAsync(member.UserId, group);
        string key = $"actor:{target.Actor:D}";
        using var contextResponse = await member.Client.GetAsync($"/api/learning/context?contextKey={key}");
        await Assert.That(contextResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var context = (await contextResponse.Content.ReadFromJsonAsync<LearningContextDto>())!;
        using var startResponse = await member.Client.PostAsJsonAsync("/api/learning/practice/actions",
            new LearningApplyPracticeActionRequest
            {
                ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0, ExpectedPracticeRevision = 0,
                IntentId = Guid.CreateVersion7(), ContextKey = key, RecipeKey = "event-details",
                MaterialVersion = 1, Action = LearningPracticeAction.Start
            });
        await Assert.That(startResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var start = (await startResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await Assert.That(start.State!.OwnedPractices.Single().PreviewAvailable).IsTrue();
        using (var preview = await member.Client.GetAsync($"/api/learning/practice/{start.Practice!.Id:D}"))
            await Assert.That(preview.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await using (var database = fixture.OpenInspection())
        {
            await using var human = new HumanSecuritySourceGate(database);
            await using var heldHuman = await human.AcquireAsync();
            await using var discovery = new EventDiscoveryEnrollmentGate(database);
            await using var heldDiscovery = await discovery.AcquireAsync();
            if (group)
                await database.GroupTenants.Where(row => row.Id == target.Placement)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsSuspended, true));
            else
                await database.OrganizationTenants.Where(row => row.Id == target.Placement)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsSuspended, true));
        }
        using (var denied = await member.Client.GetAsync($"/api/learning/practice/{start.Practice!.Id:D}"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var denied = await member.Client.GetAsync($"/api/learning/context?contextKey={key}"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var owner = await LearningOperationTests.Context(member.Client);
        await Assert.That(owner.OwnedPractices.Single(row => row.Id == start.Practice!.Id).PreviewAvailable).IsFalse();
        using var clearResponse = await member.Client.PostAsJsonAsync("/api/learning/practice/actions",
            new LearningApplyPracticeActionRequest
            {
                ExpectedScopeStamp = owner.ScopeStamp, ExpectedRevision = owner.LearnerRevision,
                ExpectedPracticeRevision = start.Practice!.Revision, PracticeId = start.Practice.Id,
                IntentId = Guid.CreateVersion7(), ContextKey = key, RecipeKey = "event-details",
                MaterialVersion = 1, Action = LearningPracticeAction.Clear
            });
        await Assert.That(clearResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cleared = (await clearResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await Assert.That(cleared.Id).IsEqualTo(start.Practice.Id);
        await Assert.That(cleared.State!.ContextKey).IsEqualTo("self");
        await Assert.That(cleared.State.OwnedPractices).IsEmpty();
    }

    private async Task<(Guid Actor, Guid Placement)> CreateActorAsync(Guid userId, bool group)
    {
        await using var database = fixture.OpenInspection();
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)(group ? ActorTypeEnum.Group : ActorTypeEnum.Organization),
            ActorType = null!, Pii = new ActorPii { DisplayName = "Learning member context" }
        };
        Guid placementId = Guid.CreateVersion7();
        if (group)
        {
            var owner = new Group { Id = Guid.CreateVersion7(), FullName = "Learning group", Actor = actor };
            actor.Group = owner;
            actor.GroupId = owner.Id;
            var placement = new GroupTenant
            {
                Id = placementId, TenantId = fixture.TenantId, Tenant = null!, GroupId = owner.Id,
                Group = owner, ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!
            };
            database.AddRange(actor, owner, placement, new GroupMember
            {
                Id = Guid.CreateVersion7(), GroupTenantId = placementId, GroupTenant = placement,
                TenantId = fixture.TenantId, Tenant = null!, UserId = userId, User = null!,
                RoleId = (int)RoleEnum.GroupMember, Role = null!
            });
        }
        else
        {
            var owner = new Organization
            {
                Id = Guid.CreateVersion7(), Pii = new OrganizationPii { FullName = "Learning organization" }, Actor = actor
            };
            actor.Organization = owner;
            actor.OrganizationId = owner.Id;
            var placement = new OrganizationTenant
            {
                Id = placementId, TenantId = fixture.TenantId, Tenant = null!, OrganizationId = owner.Id,
                Organization = owner, ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!
            };
            database.AddRange(actor, owner, placement, new OrganizationMember
            {
                Id = Guid.CreateVersion7(), OrganizationTenantId = placementId, OrganizationTenant = placement,
                TenantId = fixture.TenantId, Tenant = null!, UserId = userId, User = null!,
                RoleId = (int)RoleEnum.OrgMember, Role = null!
            });
        }
        await using var human = new HumanSecuritySourceGate(database);
        await using var heldHuman = await human.AcquireAsync();
        await using var discovery = new EventDiscoveryEnrollmentGate(database);
        await using var heldDiscovery = await discovery.AcquireAsync();
        await database.SaveChangesAsync();
        return (actor.Id, placementId);
    }
}
