using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationQualifiedJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new ModerationAuthorityScopeJsonConverter(),
            new ModerationSubjectReferenceJsonConverter(),
            new JsonStringEnumConverter()
        }
    };

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task ScopeRoundTripRetainsExactAncestry(int depth)
    {
        var root = AuthorityScope.TrustDomain(Guid.CreateVersion7());
        var solution = AuthorityScope.Solution(root, Guid.CreateVersion7());
        var instance = AuthorityScope.Instance(solution, Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var community = AuthorityScope.Community(tenant, Guid.CreateVersion7());
        var scopes = new[] { root, solution, instance, tenant, community,
            AuthorityScope.Resource(tenant, Guid.CreateVersion7()),
            AuthorityScope.Resource(community, Guid.CreateVersion7()) };
        var scope = scopes[depth];
        var restored = JsonSerializer.Deserialize<AuthorityScope>(JsonSerializer.Serialize(scope, Options), Options);
        await Assert.That(restored).IsEqualTo(scope);
        await Assert.That(restored!.Coordinates).IsEqualTo(scope.Coordinates);
    }

    [Test]
    [Arguments(ModerationSubjectKind.Human)]
    [Arguments(ModerationSubjectKind.Service)]
    [Arguments(ModerationSubjectKind.Organization)]
    [Arguments(ModerationSubjectKind.Content)]
    public async Task LifecycleRequestRoundTripRetainsSubjectOriginAndTarget(ModerationSubjectKind kind)
    {
        var origin = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var command = new ApproveModerationDecisionCommand
        {
            OperationId = Guid.CreateVersion7(), EffectId = Guid.CreateVersion7(), DecisionId = Guid.CreateVersion7(),
            Subject = SubjectReference.Create(kind, Guid.CreateVersion7(), origin),
            Target = AuthorityScope.Resource(origin, Guid.CreateVersion7()),
            ExpectedRevision = 2, ExpectedPolicyRevision = 3
        };
        var restored = JsonSerializer.Deserialize<ApproveModerationDecisionCommand>(
            JsonSerializer.Serialize(command, Options), Options);
        await Assert.That(restored).IsEqualTo(command);
        await Assert.That(restored!.Subject.Origin).IsEqualTo(origin);
        await Assert.That(restored.Target.Parent).IsEqualTo(origin);
    }

    [Test]
    [Arguments("null")]
    [Arguments("[]")]
    [Arguments("{}")]
    [Arguments("{\"kind\":99,\"id\":\"019a0000-0000-7000-8000-000000000001\"}")]
    [Arguments("{\"kind\":\"TrustDomain\",\"id\":\"invalid\"}")]
    [Arguments("{\"kind\":\"TrustDomain\",\"id\":\"00000000-0000-0000-0000-000000000000\"}")]
    [Arguments("{\"kind\":\"Tenant\",\"id\":\"019a0000-0000-7000-8000-000000000001\"}")]
    [Arguments("{\"kind\":\"TrustDomain\",\"kind\":\"Tenant\",\"id\":\"019a0000-0000-7000-8000-000000000001\"}")]
    [Arguments("{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"id\":\"019a0000-0000-7000-8000-000000000002\"}")]
    [Arguments("{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"parent\":null,\"parent\":null}")]
    [Arguments("{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"authority\":\"approved\"}")]
    [Arguments("{\"kind\":\"Tenant\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"parent\":{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000002\"}}")]
    [Arguments("{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"parent\":{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000002\"}}")]
    public async Task MalformedScopeCannotBypassDomainFactories(string json) =>
        await Assert.That(() => JsonSerializer.Deserialize<AuthorityScope>(json, Options)).Throws<JsonException>();

    [Test]
    [Arguments("null")]
    [Arguments("[]")]
    [Arguments("{}")]
    [Arguments("{\"kind\":99,\"id\":\"019a0000-0000-7000-8000-000000000001\",\"origin\":{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000002\"}}")]
    [Arguments("{\"kind\":\"Human\",\"id\":\"00000000-0000-0000-0000-000000000000\",\"origin\":{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000002\"}}")]
    [Arguments("{\"kind\":\"Human\",\"id\":\"invalid\",\"origin\":{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000002\"}}")]
    [Arguments("{\"kind\":\"Human\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"origin\":null}")]
    [Arguments("{\"kind\":\"Human\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"id\":\"019a0000-0000-7000-8000-000000000003\",\"origin\":{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000002\"}}")]
    [Arguments("{\"kind\":\"Human\",\"id\":\"019a0000-0000-7000-8000-000000000001\",\"origin\":{\"kind\":\"TrustDomain\",\"id\":\"019a0000-0000-7000-8000-000000000002\"},\"permission\":\"approved\"}")]
    public async Task MalformedSubjectCannotBecomeCanonicalIntent(string json) =>
        await Assert.That(() => JsonSerializer.Deserialize<SubjectReference>(json, Options)).Throws<JsonException>();
}
