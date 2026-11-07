using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class AtprotoAuthenticationFlowTargetTests
{
    [Test]
    public async Task Targeted_PreservesExactTenant()
    {
        var tenantId = Guid.Parse("019a5920-3f00-7000-8000-000000000001");

        var target = AtprotoAuthenticationFlowTarget.Targeted(tenantId);

        await Assert.That(target.Kind).IsEqualTo(AtprotoAuthenticationFlowKind.Targeted);
        await Assert.That(target.TenantId).IsEqualTo((Guid?)tenantId);
        await Assert.That(target).IsEqualTo(
            new AtprotoAuthenticationFlowTarget(AtprotoAuthenticationFlowKind.Targeted, tenantId));
    }

    [Test]
    public async Task Targeted_RejectsEmptyTenant()
    {
        await Assert.That(() => AtprotoAuthenticationFlowTarget.Targeted(Guid.Empty))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UnscopedVerification_HasExplicitKindAndNoTenant()
    {
        var target = AtprotoAuthenticationFlowTarget.UnscopedVerification();

        await Assert.That(target.Kind).IsEqualTo(AtprotoAuthenticationFlowKind.UnscopedVerification);
        await Assert.That(target.TenantId).IsNull();
        await Assert.That(target).IsEqualTo(
            new AtprotoAuthenticationFlowTarget(AtprotoAuthenticationFlowKind.UnscopedVerification, null));
    }

    [Test]
    [Arguments(AtprotoAuthenticationFlowKind.Targeted, null)]
    [Arguments(AtprotoAuthenticationFlowKind.Targeted, "00000000-0000-0000-0000-000000000000")]
    [Arguments(AtprotoAuthenticationFlowKind.UnscopedVerification, "00000000-0000-0000-0000-000000000000")]
    [Arguments(AtprotoAuthenticationFlowKind.UnscopedVerification, "019a5920-3f00-7000-8000-000000000001")]
    public async Task Constructor_RejectsKindAndTenantMismatch(
        AtprotoAuthenticationFlowKind kind, string? tenant)
    {
        Guid? tenantId = tenant is null ? null : Guid.Parse(tenant);

        await Assert.That(() => new AtprotoAuthenticationFlowTarget(kind, tenantId))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(3)]
    public async Task Constructor_RejectsUndefinedKindInsteadOfInferringUnscoped(int kind)
    {
        await Assert.That(() => new AtprotoAuthenticationFlowTarget((AtprotoAuthenticationFlowKind)kind, null))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Equality_DistinguishesExactTenantsAndUnscopedVerification()
    {
        var first = AtprotoAuthenticationFlowTarget.Targeted(
            Guid.Parse("019a5920-3f00-7000-8000-000000000001"));
        var second = AtprotoAuthenticationFlowTarget.Targeted(
            Guid.Parse("019a5920-3f00-7000-8000-000000000002"));
        var unscoped = AtprotoAuthenticationFlowTarget.UnscopedVerification();

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(first).IsNotEqualTo(unscoped);
        await Assert.That(second).IsNotEqualTo(unscoped);
    }
}
