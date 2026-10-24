using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationScopeTransportBoundsTests
{
    [Test]
    [Arguments(ModerationScopeKind.TrustDomain, "TrustDomain,Solution,Instance,Tenant,Community,Resource,Event")]
    [Arguments(ModerationScopeKind.Solution, "Solution,Instance,Tenant,Community,Resource,Event")]
    [Arguments(ModerationScopeKind.Instance, "Instance,Tenant,Community,Resource,Event")]
    [Arguments(ModerationScopeKind.Tenant, "Tenant,Community,Resource,Event")]
    [Arguments(ModerationScopeKind.Community, "Community,Resource")]
    [Arguments(ModerationScopeKind.Resource, "Resource")]
    [Arguments(ModerationScopeKind.Event, "Event")]
    public async Task AuthoringBoundsFollowTreeBranchesRatherThanNumericKindOrder(
        ModerationScopeKind kind, string expectedKinds)
    {
        var trust = AuthorityScope.TrustDomain(Guid.CreateVersion7());
        var solution = AuthorityScope.Solution(trust, Guid.CreateVersion7());
        var instance = AuthorityScope.Instance(solution, Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var scope = kind switch
        {
            ModerationScopeKind.TrustDomain => trust,
            ModerationScopeKind.Solution => solution,
            ModerationScopeKind.Instance => instance,
            ModerationScopeKind.Tenant => tenant,
            ModerationScopeKind.Community => AuthorityScope.Community(tenant, Guid.CreateVersion7()),
            ModerationScopeKind.Resource => AuthorityScope.Resource(tenant, Guid.CreateVersion7()),
            _ => AuthorityScope.Event(tenant, Guid.CreateVersion7())
        };
        var bounds = ModerationPolicyPublicationProjection.AuthoringBounds(scope);
        await Assert.That(bounds.Jurisdiction).IsEqualTo(scope);
        await Assert.That(string.Join(",", bounds.ScopeKinds)).IsEqualTo(expectedKinds);
    }

    [Test]
    public async Task NoticeScopeRoundTripKeepsEventDistinctFromResourceWithTheSameIdentifier()
    {
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        var id = Guid.CreateVersion7();
        var snapshot = ModerationNoticeScope.From(AuthorityScope.Event(tenant, id));
        var restored = JsonSerializer.Deserialize<ModerationNoticeScope>(JsonSerializer.Serialize(snapshot));
        await Assert.That(restored).IsEqualTo(snapshot);
        await Assert.That(restored!.Kind).IsEqualTo(ModerationScopeKind.Event);
        await Assert.That(restored.Parent).IsEqualTo(ModerationNoticeScope.From(tenant));
        await Assert.That(restored).IsNotEqualTo(ModerationNoticeScope.From(AuthorityScope.Resource(tenant, id)));
    }
}
