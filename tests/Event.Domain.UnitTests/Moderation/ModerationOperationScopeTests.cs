using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationOperationScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(ModerationScopeKind.TrustDomain)]
    [Arguments(ModerationScopeKind.Solution)]
    [Arguments(ModerationScopeKind.Instance)]
    [Arguments(ModerationScopeKind.Tenant)]
    [Arguments(ModerationScopeKind.Community)]
    [Arguments(ModerationScopeKind.Resource)]
    [Arguments(ModerationScopeKind.Event)]
    public async Task Accepted_operation_carriers_preserve_the_exact_scope_tag(ModerationScopeKind scopeKind)
    {
        var effect = Effect((int)scopeKind);
        var receipt = Receipt(effect, (int)scopeKind);

        await Assert.That(effect.ScopeKind).IsEqualTo((int)scopeKind);
        await Assert.That(receipt.ScopeKind).IsEqualTo((int)scopeKind);
        await Assert.That(receipt.ScopeId).IsEqualTo(effect.ScopeId);
        await Assert.That(receipt.TrustDomainId).IsEqualTo(effect.TrustDomainId);
        await Assert.That(receipt.Id).IsEqualTo(effect.OperationId);
        await Assert.That(receipt.DecisionId).IsEqualTo(effect.DecisionId);
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.LeaseId).IsNull();
        effect.ValidatePersistedState();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(8)]
    [Arguments(int.MaxValue)]
    public async Task Undefined_scope_tags_cannot_create_an_effect_or_accepted_receipt(int scopeKind)
    {
        var effect = Effect((int)ModerationScopeKind.Event);

        await Assert.That(() => Effect(scopeKind)).Throws<ArgumentException>();
        await Assert.That(() => Receipt(effect, scopeKind)).Throws<ArgumentException>();
    }

    private static ModerationEffectIntent Effect(int scopeKind) => ModerationEffectIntent.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, null, null,
        Guid.CreateVersion7(), scopeKind, Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
        1, 1, 1, Now, null, null);

    private static ModerationOperationReceipt Receipt(ModerationEffectIntent effect, int scopeKind) =>
        ModerationOperationReceipt.Complete(effect.OperationId, new string('a', 64), effect.TrustDomainId,
            scopeKind, effect.ScopeId, effect.SubjectKind, effect.SubjectId, Guid.CreateVersion7(),
            new string('b', 64), effect.DecisionId, null, effect.DecisionRevision,
            effect.PolicyRevision, effect.QualificationRevision, Now);
}
