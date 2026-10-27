using Explore.Application.Contracts.Scheduling;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationReconciliationTargetTests
{
    [Test]
    public async Task Typed_reconciliation_keeps_the_same_durable_cause_and_expected_generations()
    {
        var target = Target();
        var operation = target.Operation();
        var command = target.Command();
        await Assert.That(command.ToOperation()).IsEqualTo(operation);
        await Assert.That(command.OperationId).IsEqualTo(operation.OperationId);
        await Assert.That(command.EffectId).IsEqualTo(operation.EffectId);
        await Assert.That(command.DecisionId).IsEqualTo(target.DecisionId);
        await Assert.That(command.SanctionId).IsEqualTo(target.SanctionId);
        await Assert.That(command.ExpectedRevision).IsEqualTo(6L);
        await Assert.That(command.ExpectedSanctionRevision).IsEqualTo(2L);
        await Assert.That(command.ExpectedPolicyRevision).IsEqualTo(3L);
        await Assert.That(command.Subject).IsEqualTo(target.Subject);
        await Assert.That(command.Target).IsEqualTo(target.Scope);
    }

    [Test]
    public async Task Repeated_discovery_preserves_exact_operation_and_distinct_effect_identity()
    {
        var target = Target();
        var first = target.Operation();
        var retry = (target with { }).Operation();
        await Assert.That(retry).IsEqualTo(first);
        await Assert.That(first.OperationId == first.EffectId).IsFalse();
        await Assert.That(first.OperationId.Version).IsEqualTo(7);
        await Assert.That(first.EffectId.Version).IsEqualTo(7);
        await Assert.That(first.ExpectedRevision).IsEqualTo(6);
        await Assert.That(first.ExpectedSanctionRevision).IsEqualTo(2);
        await Assert.That(first.ExpectedPolicyRevision).IsEqualTo(3);
    }

    [Test]
    [Arguments("decision")]
    [Arguments("sanction")]
    [Arguments("policy")]
    [Arguments("deadline")]
    [Arguments("tenant")]
    [Arguments("host")]
    [Arguments("service")]
    public async Task Changed_authoritative_transition_cannot_reuse_an_old_receipt(string change)
    {
        var original = Target();
        var changed = change switch
        {
            "decision" => original with { DecisionRevision = 7 },
            "sanction" => original with { SanctionRevision = 3 },
            "policy" => original with { PolicyRevision = 4 },
            "deadline" => original with { Deadline = original.Deadline.AddHours(1) },
            "tenant" => original with { TenantId = Guid.CreateVersion7() },
            "host" => original with { HostBindingId = Guid.CreateVersion7() },
            "service" => original with { MaintenanceServiceId = Guid.CreateVersion7() },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await Assert.That(changed.Operation().OperationId == original.Operation().OperationId).IsFalse();
        await Assert.That(changed.Operation().EffectId == original.Operation().EffectId).IsFalse();
    }

    private static ModerationReconciliationTarget Target()
    {
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        return new(Guid.CreateVersion7(), tenant.Id, Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance), tenant,
            Guid.CreateVersion7(), 6, Guid.CreateVersion7(), 2, Guid.CreateVersion7(), 3,
            new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    }
}
