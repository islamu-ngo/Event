using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationEventScopeTests
{
    [Test]
    public async Task EventAndResourceWithTheSameIdentifierRemainDistinctSiblings()
    {
        var tenant = Tenant();
        var id = Guid.CreateVersion7();
        var eventScope = AuthorityScope.Event(tenant, id);
        var resourceScope = AuthorityScope.Resource(tenant, id);

        await Assert.That(eventScope).IsNotEqualTo(resourceScope);
        await Assert.That(tenant.Contains(eventScope)).IsTrue();
        await Assert.That(eventScope.Contains(resourceScope)).IsFalse();
        await Assert.That(resourceScope.Contains(eventScope)).IsFalse();
        await Assert.That(eventScope.Coordinates.EventId).IsEqualTo(id);
        await Assert.That(eventScope.Coordinates.ResourceId).IsNull();
    }

    [Test]
    public async Task EventCoordinatesRetainTheExactTenantAncestry()
    {
        var tenant = Tenant();
        var id = Guid.CreateVersion7();
        var eventScope = AuthorityScope.Event(tenant, id);
        var coordinates = eventScope.Coordinates;
        var restored = AuthorityScope.FromCoordinates(coordinates.Kind, coordinates.TrustDomainId,
            coordinates.SolutionId, coordinates.InstanceId, coordinates.TenantId,
            coordinates.CommunityId, coordinates.ResourceId, coordinates.EventId);
        var otherTenant = AuthorityScope.Tenant(tenant.Parent!, Guid.CreateVersion7());

        await Assert.That(restored).IsEqualTo(eventScope);
        await Assert.That(restored.Kind).IsEqualTo(ModerationScopeKind.Event);
        await Assert.That(otherTenant.Contains(restored)).IsFalse();
        await Assert.That(AuthorityScope.Event(otherTenant, id)).IsNotEqualTo(restored);
    }

    [Test]
    [Arguments(ModerationScopeKind.TrustDomain)]
    [Arguments(ModerationScopeKind.Solution)]
    [Arguments(ModerationScopeKind.Instance)]
    [Arguments(ModerationScopeKind.Community)]
    [Arguments(ModerationScopeKind.Resource)]
    [Arguments(ModerationScopeKind.Event)]
    public async Task EventCannotBeAttachedToANonTenantParent(ModerationScopeKind kind)
    {
        var tenant = Tenant();
        var parent = kind switch
        {
            ModerationScopeKind.TrustDomain => tenant.Parent!.Parent!.Parent!,
            ModerationScopeKind.Solution => tenant.Parent!.Parent!,
            ModerationScopeKind.Instance => tenant.Parent!,
            ModerationScopeKind.Community => AuthorityScope.Community(tenant, Guid.CreateVersion7()),
            ModerationScopeKind.Resource => AuthorityScope.Resource(tenant, Guid.CreateVersion7()),
            _ => AuthorityScope.Event(tenant, Guid.CreateVersion7())
        };

        await Assert.That(() => AuthorityScope.Event(parent, Guid.CreateVersion7()))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task EventRestorationRejectsMissingAncestryAndSubstitutedLeafCoordinates(int substitution)
    {
        var coordinates = Tenant().Coordinates;
        var id = Guid.CreateVersion7();

        await Assert.That(() => AuthorityScope.FromCoordinates(
            substitution == 6 ? ModerationScopeKind.Resource : ModerationScopeKind.Event,
            coordinates.TrustDomainId,
            substitution == 4 ? null : coordinates.SolutionId,
            substitution == 5 ? null : coordinates.InstanceId,
            substitution == 3 ? null : coordinates.TenantId,
            substitution == 2 ? Guid.CreateVersion7() : null,
            substitution == 1 ? id : null,
            substitution == 0 ? null : id)).Throws<ArgumentException>();
    }

    [Test]
    public async Task EmptyEventIdentifierCannotBecomeAScope()
    {
        await Assert.That(() => AuthorityScope.Event(Tenant(), Guid.Empty)).Throws<ArgumentException>();
    }

    [Test]
    public async Task EventClassificationDoesNotBroadenOtherReferenceProfileJurisdictions()
    {
        var classification = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG29);
        await Assert.That(ModerationReferenceCatalogue.Revision).IsEqualTo(2L);
        await Assert.That(classification.ScopeKinds.Contains(ModerationScopeKind.Event)).IsTrue();
        await Assert.That(classification.ScopeKinds.Contains(ModerationScopeKind.Resource)).IsTrue();
        await Assert.That(classification.SubjectKinds.SetEquals([ModerationSubjectKind.Content])).IsTrue();
        await Assert.That(ModerationReferenceCatalogue.Profiles.Values
            .Where(profile => profile.Id != ModerationReferenceProfileId.CFG29)
            .Any(profile => profile.ScopeKinds.Contains(ModerationScopeKind.Event))).IsFalse();
    }

    private static AuthorityScope Tenant() =>
        AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
}
