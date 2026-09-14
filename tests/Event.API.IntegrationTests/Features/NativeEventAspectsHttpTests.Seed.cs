using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAspectsHttpTests
{
    private static async Task<SeedData> SeedAsync(NativeEventAspectsFactory factory)
    {
        using var scope = factory.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId).WithSlug("event-aspects").Build();
        var foreignTenant = new TenantBuilder().WithSlug("foreign-event-aspects").Build();
        var owner = new UserBuilder().WithEmail($"aspect-owner-{Guid.CreateVersion7():N}@example.test").Build();
        var outsider = new UserBuilder().WithEmail($"aspect-outsider-{Guid.CreateVersion7():N}@example.test").Build();
        var actor = new ActorBuilder().WithUserId(owner.Id).WithDisplayName("Aspect owner").Build();
        var otherActor = new ActorBuilder().WithUserId(outsider.Id).WithDisplayName("Other owner").Build();
        Explore.Domain.Event Parent(Tenant parentTenant, VisibilityTypeEnum visibility, EventStatusEnum status) =>
            new EventBuilder().WithActorId(actor.Id).WithTenantId(parentTenant.Id)
                .WithStatus(status).WithVisibility(visibility).Build();
        var published = Parent(tenant, VisibilityTypeEnum.Public, EventStatusEnum.Published);
        var hidden = Parent(tenant, VisibilityTypeEnum.Private, EventStatusEnum.Published);
        var draft = Parent(tenant, VisibilityTypeEnum.Public, EventStatusEnum.Draft);
        var empty = Parent(tenant, VisibilityTypeEnum.Public, EventStatusEnum.Published);
        var foreign = Parent(foreignTenant, VisibilityTypeEnum.Public, EventStatusEnum.Published);
        db.AddRange(tenant, foreignTenant, owner, outsider, actor, otherActor, published, hidden, draft, empty, foreign);
        foreach (var parentTenant in new[] { tenant, foreignTenant })
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = parentTenant.Id, Tenant = parentTenant,
                UserId = owner.Id, User = owner, ActorId = actor.Id, Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = DateTime.UnixEpoch
            });
        foreach (var parent in new[] { published, hidden, draft, empty, foreign })
            db.EventRoleAssignments.Add(EventRoleAssignment.Create(parent.TenantId, parent.Id, owner.Id,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UnixEpoch, null, owner.Id));
        foreach (var parent in new[] { published, hidden, draft, foreign })
        {
            db.EventIslamicAspects.Add(new EventIslamicAspect
            {
                Id = parent.Id, Event = parent, GenderMode = GenderSegregationMode.Family,
                IncludesQuranRecitation = true
            });
            db.EventTechAspects.Add(new EventTechAspect
            {
                Id = parent.Id, Event = parent, SkillLevel = SkillLevel.AllLevels,
                GithubRepoUrl = "https://code.example.test/private", RequiresLaptop = true
            });
        }
        await db.SaveChangesAsync();
        return new(owner.Id, outsider.Id, actor.Id, otherActor.Id, published.Id, hidden.Id, draft.Id, empty.Id,
            foreign.Id, foreignTenant.Id);
    }

    private sealed record SeedData(Guid OwnerId, Guid OutsiderId, Guid OwnerActorId, Guid OtherActorId,
        Guid PublicId, Guid PrivateId, Guid DraftId, Guid EmptyId, Guid ForeignId, Guid ForeignTenantId);
}
