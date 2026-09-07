using Explore.Application.Services;

namespace Event.Application.UnitTests.Features.CustomProperties;

public class CustomPropertyGovernancePolicyTests
{
    private readonly CustomPropertyGovernancePolicy _policy = new();

    [Test]
    public async Task EvaluateDefinition_WithTenantNamespace_NormalizesAndPasses()
    {
        var result = _policy.EvaluateDefinition("Tenant Community", "Prayer Notes");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.NormalizedNamespace).IsEqualTo("tenant.community");
        await Assert.That(result.NormalizedKey).IsEqualTo("prayer_notes");
    }

    [Test]
    public async Task EvaluateDefinition_WithReservedNamespaceWithoutPrivilege_ReturnsError()
    {
        var result = _policy.EvaluateDefinition("platform.islamic", "custom_badge");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Reserved namespaces", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task EvaluateDefinition_WithReservedNamespaceWithPrivilege_AllowsNamespace()
    {
        var result = _policy.EvaluateDefinition("pack.Islamic", "venue_layout", canManageReservedNamespaces: true);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.NormalizedNamespace).IsEqualTo("pack.islamic");
    }

    [Test]
    public async Task EvaluateDefinition_WithReservedLayer2Semantic_ReturnsError()
    {
        var result = _policy.EvaluateDefinition("sector.islamic", "Madhab Id", canManageReservedNamespaces: true);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Layer 2 semantics", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task EvaluateDefinition_WithTenantNamespaceAndLayer2SemanticKey_ReturnsError()
    {
        var result = _policy.EvaluateDefinition("tenant.community", "Madhab Id");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Layer 2 semantics", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task EvaluateDefinition_WithTenantNamespaceAndSessionLayer2SemanticKey_ReturnsError()
    {
        var result = _policy.EvaluateDefinition("tenant.sessions", "Offset Minutes");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("Layer 2 semantics", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task EvaluateDefinition_WithSimilarButNonReservedTenantKey_Passes()
    {
        var result = _policy.EvaluateDefinition("tenant.community", "Prayer Notes");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.NormalizedNamespace).IsEqualTo("tenant.community");
        await Assert.That(result.NormalizedKey).IsEqualTo("prayer_notes");
    }

    [Test]
    public async Task EvaluateDefinition_WithUnsupportedNamespace_ReturnsError()
    {
        var result = _policy.EvaluateDefinition("community", "local_tag");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Contains("supported root", StringComparison.Ordinal))).IsTrue();
    }
}
