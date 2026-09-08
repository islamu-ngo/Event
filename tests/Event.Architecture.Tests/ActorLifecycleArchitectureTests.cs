using Explore.Domain;
using Explore.Domain.Interfaces;

namespace Event.Architecture.Tests;

public class ActorLifecycleArchitectureTests
{
    private static readonly Type[] ExistingGlobalSubjectTypes =
    [
        typeof(Actor),
        typeof(User),
        typeof(Organization),
        typeof(Group)
    ];

    [Test]
    public async Task GlobalSubjects_DoNotExposeTenantScope()
    {
        Type[] globalTypes =
        [
            .. ExistingGlobalSubjectTypes,
            RequiredDomainType("ExternalActorSubject"),
            RequiredDomainType("ServicePrincipal"),
            RequiredDomainType("AtprotoIdentity")
        ];

        foreach (var type in globalTypes)
        {
            await Assert.That(typeof(ITenantEntity).IsAssignableFrom(type)).IsFalse();
            await Assert.That(type.GetProperty("TenantId")).IsNull();
        }
    }

    [Test]
    public async Task Actor_OwnsExactlyOneConcreteSubjectForeignKeyDirection()
    {
        string[] ownerForeignKeys =
        [
            nameof(Actor.UserId),
            nameof(Actor.OrganizationId),
            nameof(Actor.GroupId),
            "ExternalActorSubjectId",
            "ServicePrincipalId"
        ];

        foreach (var foreignKey in ownerForeignKeys)
        {
            await Assert.That(typeof(Actor).GetProperty(foreignKey)).IsNotNull();
        }

        foreach (var concreteType in new[]
                 {
                     typeof(User),
                     typeof(Organization),
                     typeof(Group),
                     RequiredDomainType("ExternalActorSubject"),
                     RequiredDomainType("ServicePrincipal")
                 })
        {
            await Assert.That(concreteType.GetProperty("ActorId")).IsNull();
            await Assert.That(concreteType.GetProperty("Actor")).IsNotNull();
        }
    }

    [Test]
    public async Task AtprotoIdentity_IsGlobalDidAuthorityForOneActor()
    {
        var identityType = RequiredDomainType("AtprotoIdentity");

        await Assert.That(identityType.GetProperty("Id")?.PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(identityType.GetProperty("Did")?.PropertyType).IsEqualTo(typeof(string));
        await Assert.That(identityType.GetProperty("ActorId")?.PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(identityType.GetProperty("Actor")?.PropertyType).IsEqualTo(typeof(Actor));
        await Assert.That(typeof(Actor).GetProperty("AtprotoIdentities")).IsNotNull();
        await Assert.That(typeof(ActorPii).GetProperty("Did")).IsNull();
        await Assert.That(typeof(ActorPii).GetProperty("Handle")).IsNull();
    }

    [Test]
    public async Task ConcreteParticipation_OwnsTenantPolicyAndLocalRelationships()
    {
        Type organizationTenant = RequiredDomainType("OrganizationTenant");
        Type groupTenant = RequiredDomainType("GroupTenant");

        foreach (Type participationType in new[] { organizationTenant, groupTenant })
        {
            await Assert.That(typeof(ITenantEntity).IsAssignableFrom(participationType)).IsTrue();
            await Assert.That(participationType.GetProperty("TenantId")).IsNotNull();
            await Assert.That(participationType.GetProperty("ApprovalStatusId")).IsNotNull();
            await Assert.That(participationType.GetProperty("IsOrganizerEligible")).IsNotNull();
            await Assert.That(participationType.GetProperty("IsSuspended")).IsNotNull();
            await Assert.That(participationType.GetProperty("ProfilePictureId")).IsNotNull();
        }

        await Assert.That(typeof(OrganizationMember).GetProperty("OrganizationTenantId")).IsNotNull();
        await Assert.That(typeof(OrganizationMember).GetProperty("OrganizationId")).IsNull();
        await Assert.That(typeof(GroupMember).GetProperty("GroupTenantId")).IsNotNull();
        await Assert.That(typeof(GroupMember).GetProperty("GroupId")).IsNull();
        await Assert.That(typeof(OrganizationSetting).GetProperty("OrganizationTenantId")).IsNotNull();
        await Assert.That(typeof(GroupSetting).GetProperty("GroupTenantId")).IsNotNull();
        await Assert.That(groupTenant.GetProperty("ParentOrganizationTenantId")).IsNotNull();
        await Assert.That(groupTenant.GetProperty("ParentGroupTenantId")).IsNotNull();
    }

    [Test]
    public async Task GenericActorPresenceAndCompositeEventActorOwnership_RemainAbsent()
    {
        await Assert.That(typeof(Actor).Assembly.GetType("Explore.Domain.ActorTenantPresence")).IsNull();
        await Assert.That(typeof(Explore.Domain.Event).GetProperty("ActorId")?.PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(typeof(Explore.Domain.Event).GetProperty("Actor")?.PropertyType).IsEqualTo(typeof(Actor));
        await Assert.That(typeof(Explore.Domain.Event).GetProperty("TenantActorId")).IsNull();
    }

    [Test]
    public async Task ActorProfileAffordances_RemainHalDriven()
    {
        var profile = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            "src",
            "Explore.Blazor.Client",
            "Pages",
            "Actors",
            "ActorProfile.razor"));
        var routes = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            "src",
            "Explore.Blazor.Client",
            "Routes.razor"));

        await Assert.That(profile).Contains("HasHalLink");
        await Assert.That(profile).DoesNotContain("ClaimsPrincipal");
        await Assert.That(profile).DoesNotContain("ClaimTypes");
        await Assert.That(profile).DoesNotContain("IsInRole");
        await Assert.That(profile).DoesNotContain("CurrentUserRoleId");
        await Assert.That(routes).Contains("/actors/:id");
        await Assert.That(routes).Contains("/t/:tenantId/actors/:id");
    }

    private static Type RequiredDomainType(string typeName) =>
        typeof(Actor).Assembly.GetType($"Explore.Domain.{typeName}")
        ?? throw new InvalidOperationException($"Missing domain type Explore.Domain.{typeName}.");
}
