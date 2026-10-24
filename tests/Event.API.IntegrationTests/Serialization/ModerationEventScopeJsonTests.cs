using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;

namespace Event.Api.IntegrationTests.Serialization;

public sealed class ModerationEventScopeJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(),
            new ModerationSubjectReferenceJsonConverter(), new JsonStringEnumConverter() }
    };

    [Test]
    public async Task EventRequestRoundTripPreservesTypedTargetAndExactTenantAncestry()
    {
        var tenant = Tenant();
        var scope = AuthorityScope.Event(tenant, Guid.CreateVersion7());
        var command = new ApproveModerationDecisionCommand
        {
            OperationId = Guid.CreateVersion7(),
            EffectId = Guid.CreateVersion7(),
            DecisionId = Guid.CreateVersion7(),
            ExpectedRevision = 2,
            ExpectedPolicyRevision = 3,
            Subject = SubjectReference.Create(ModerationSubjectKind.Content, Guid.CreateVersion7(), scope),
            Target = scope
        };
        var restored = JsonSerializer.Deserialize<ApproveModerationDecisionCommand>(
            JsonSerializer.Serialize(command, Options), Options)!;
        await Assert.That(restored).IsEqualTo(command);
        await Assert.That(restored.Target.Kind).IsEqualTo(ModerationScopeKind.Event);
        await Assert.That(restored.Target.Parent).IsEqualTo(tenant);
        await Assert.That(restored.Target.Coordinates.EventId).IsEqualTo(scope.Id);
        await Assert.That(restored.Target.Coordinates.ResourceId).IsNull();
        await Assert.That(restored.Target).IsNotEqualTo(AuthorityScope.Resource(tenant, scope.Id));
    }

    [Test]
    [Arguments(ModerationScopeKind.Community)]
    [Arguments(ModerationScopeKind.Resource)]
    [Arguments(ModerationScopeKind.Event)]
    [Arguments(ModerationScopeKind.Instance)]
    public async Task EventCannotAcquireANonTenantParent(ModerationScopeKind parentKind)
    {
        var tenant = Tenant();
        var parent = parentKind switch
        {
            ModerationScopeKind.Community => AuthorityScope.Community(tenant, Guid.CreateVersion7()),
            ModerationScopeKind.Resource => AuthorityScope.Resource(tenant, Guid.CreateVersion7()),
            ModerationScopeKind.Event => AuthorityScope.Event(tenant, Guid.CreateVersion7()),
            _ => tenant.Parent!
        };
        var json = new JsonObject
        {
            ["kind"] = "Event",
            ["id"] = Guid.CreateVersion7().ToString("D"),
            ["parent"] = JsonSerializer.SerializeToNode(parent, Options)
        };
        await Assert.That(() => JsonSerializer.Deserialize<AuthorityScope>(json.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    private static AuthorityScope Tenant() => AuthorityScope.Tenant(AuthorityScope.Instance(
        AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
}
