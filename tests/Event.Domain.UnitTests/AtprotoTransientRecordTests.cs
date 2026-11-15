using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

public sealed class AtprotoTransientRecordTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const long Expiry = 2_000_000_000_000;

    [Test]
    [Arguments(AtprotoTransientPurpose.OAuthState)]
    [Arguments(AtprotoTransientPurpose.TenantHandoff)]
    public async Task TargetedState_PreservesExactTenantAndProtectedBindings(AtprotoTransientPurpose purpose)
    {
        var tenant = Guid.Parse("019a5920-3f00-7000-8000-000000000001");

        var state = AtprotoTransientRecord.Create(purpose, Digest,
            AtprotoAuthenticationFlowTarget.Targeted(tenant), "opaque-browser-flow-provider-binding", Expiry);

        await Assert.That(state.TenantId).IsEqualTo((Guid?)tenant);
        await Assert.That(state.FlowKind).IsEqualTo((AtprotoAuthenticationFlowKind?)AtprotoAuthenticationFlowKind.Targeted);
        await Assert.That(state.Purpose).IsEqualTo(purpose);
        await Assert.That(state.TokenDigest).IsEqualTo(Digest);
        await Assert.That(state.ProtectedPayload).IsEqualTo("opaque-browser-flow-provider-binding");
        await Assert.That(state.ExpiresAtUnixMilliseconds).IsEqualTo(Expiry);
    }

    [Test]
    public async Task UnscopedOAuthState_PreservesExplicitKindWithoutTenant()
    {
        var state = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest,
            AtprotoAuthenticationFlowTarget.UnscopedVerification(), "opaque-browser-flow-provider-binding", Expiry);

        await Assert.That(state.TenantId).IsNull();
        await Assert.That(state.FlowKind).IsEqualTo(
            (AtprotoAuthenticationFlowKind?)AtprotoAuthenticationFlowKind.UnscopedVerification);
        await Assert.That(state.Purpose).IsEqualTo(AtprotoTransientPurpose.OAuthState);
        await Assert.That(state.TokenDigest).IsEqualTo(Digest);
        await Assert.That(state.ProtectedPayload).IsEqualTo("opaque-browser-flow-provider-binding");
        await Assert.That(state.ExpiresAtUnixMilliseconds).IsEqualTo(Expiry);
    }

    [Test]
    public async Task UnscopedVerification_CannotCreateTenantHandoff()
    {
        await Assert.That(() => AtprotoTransientRecord.Create(AtprotoTransientPurpose.TenantHandoff, Digest,
            AtprotoAuthenticationFlowTarget.UnscopedVerification(), "opaque", Expiry))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    [Arguments(99)]
    public async Task AuthenticationCreation_RejectsNonAuthenticationPurposes(int purpose)
    {
        var target = AtprotoAuthenticationFlowTarget.Targeted(
            Guid.Parse("019a5920-3f00-7000-8000-000000000001"));

        await Assert.That(() => AtprotoTransientRecord.Create((AtprotoTransientPurpose)purpose,
            Digest, target, "opaque", Expiry)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task HealthProbe_HasNeitherTenantNorAuthenticationKind()
    {
        var probe = AtprotoTransientRecord.CreateHealthProbe(Digest, "synthetic-probe", Expiry);

        await Assert.That(probe.Purpose).IsEqualTo(AtprotoTransientPurpose.HealthProbe);
        await Assert.That(probe.TenantId).IsNull();
        await Assert.That(probe.FlowKind).IsNull();
    }

    [Test]
    public async Task UnscopedOAuthState_StillRequiresCanonicalDigestAndBoundedPayload()
    {
        var target = AtprotoAuthenticationFlowTarget.UnscopedVerification();
        var payloadAtLimit = new string('\u00e9', 32768);

        var state = AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState, Digest, target, payloadAtLimit, Expiry);

        await Assert.That(state.ProtectedPayload).IsEqualTo(payloadAtLimit);
        await Assert.That(() => AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState, Digest.ToUpperInvariant(), target, "opaque", Expiry))
            .Throws<ArgumentException>();
        await Assert.That(() => AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState, Digest, target, payloadAtLimit + "a", Expiry))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    public async Task UnscopedOAuthState_RejectsNonpositiveExpiry(long expiry)
    {
        await Assert.That(() => AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest,
            AtprotoAuthenticationFlowTarget.UnscopedVerification(), "opaque", expiry))
            .Throws<ArgumentOutOfRangeException>();
    }
}
