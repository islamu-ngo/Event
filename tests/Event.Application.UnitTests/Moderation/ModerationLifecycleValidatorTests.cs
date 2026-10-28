using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationLifecycleValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly AuthorityScope Target = AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
    private static readonly SubjectReference Subject = SubjectReference.Create(
        ModerationSubjectKind.Human, Guid.CreateVersion7(), Target);

    [Test]
    [Arguments(ModerationOperationKind.RecordResponse)]
    [Arguments(ModerationOperationKind.AcceptFinding)]
    [Arguments(ModerationOperationKind.Approve)]
    [Arguments(ModerationOperationKind.Activate)]
    [Arguments(ModerationOperationKind.Reject)]
    [Arguments(ModerationOperationKind.Withdraw)]
    [Arguments(ModerationOperationKind.Revoke)]
    [Arguments(ModerationOperationKind.Propose)]
    [Arguments(ModerationOperationKind.Reconcile)]
    [Arguments(ModerationOperationKind.Supersede)]
    public async Task Stable_mutation_intent_is_structurally_valid_without_certifying_authority(ModerationOperationKind kind)
    {
        var result = await new ModerationLifecycleValidator().ValidateAsync(new ApplyModerationLifecycleCommand(CreateOperation(kind)));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public async Task Every_mutation_rejects_non_uuidv7_operation_effect_or_decision_identity(int identitySlot, bool wrongVariant)
    {
        var invalidId = wrongVariant
            ? Guid.Parse("019a0000-0000-7000-7000-000000000001")
            : Guid.Parse("019a0000-0000-4000-8000-000000000001");
        foreach (var kind in Enum.GetValues<ModerationOperationKind>().Where(kind => kind != ModerationOperationKind.Evaluate))
        {
            var result = await new ModerationLifecycleValidator().ValidateAsync(
                new ApplyModerationLifecycleCommand(CreateOperation(kind, identitySlot, invalidId)));

            await Assert.That(result.IsValid).IsFalse();
        }
    }

    [Test]
    [Arguments(ModerationOperationKind.AcceptFinding)]
    [Arguments(ModerationOperationKind.Activate)]
    [Arguments(ModerationOperationKind.Revoke)]
    [Arguments(ModerationOperationKind.Reconcile)]
    [Arguments(ModerationOperationKind.Supersede)]
    public async Task Referenced_finding_or_contribution_requires_uuidv7(ModerationOperationKind kind)
    {
        var result = await new ModerationLifecycleValidator().ValidateAsync(
            new ApplyModerationLifecycleCommand(CreateOperation(kind, referenceId: Guid.NewGuid())));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Proposal_case_and_rule_owner_require_uuidv7(bool invalidCase)
    {
        var proposal = CreateProposal(
            invalidCase ? Guid.NewGuid() : Guid.CreateVersion7(),
            invalidCase ? Guid.CreateVersion7() : Guid.NewGuid());
        var result = await new ModerationLifecycleValidator().ValidateAsync(
            new ApplyModerationLifecycleCommand(CreateOperation(ModerationOperationKind.Propose, proposal: proposal)));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Supersession_requires_uuidv7_for_the_old_contribution()
    {
        var result = await new ModerationLifecycleValidator().ValidateAsync(
            new ApplyModerationLifecycleCommand(CreateOperation(ModerationOperationKind.Supersede, oldSanctionId: Guid.NewGuid())));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Evaluation_cannot_enter_the_lifecycle_write_bridge()
    {
        var result = await new ModerationLifecycleValidator().ValidateAsync(
            new ApplyModerationLifecycleCommand(ModerationOperation.Evaluate(Subject, Target, ModerationCapability.Publish)));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Missing_operation_is_invalid()
    {
        var result = await new ModerationLifecycleValidator().ValidateAsync(new ApplyModerationLifecycleCommand(null!));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Proposal_rejects_sentinel_start_end_or_review_instants(int instantSlot)
    {
        var proposal = new ModerationProposal(Guid.CreateVersion7(),
            ModerationRuleReference.Create(Guid.CreateVersion7(), "conduct", 1), [ModerationCapability.Publish],
            instantSlot == 0 ? DateTimeOffset.MinValue : Now,
            instantSlot == 1 ? DateTimeOffset.MaxValue : instantSlot == 2 ? null : Now.AddDays(2),
            instantSlot == 2 ? DateTimeOffset.MaxValue : Now.AddDays(1), 2);
        var result = await new ModerationLifecycleValidator().ValidateAsync(
            new ApplyModerationLifecycleCommand(CreateOperation(ModerationOperationKind.Propose, proposal: proposal)));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Indefinite_proposal_with_review_and_two_approvals_is_structurally_valid()
    {
        var proposal = new ModerationProposal(Guid.CreateVersion7(),
            ModerationRuleReference.Create(Guid.CreateVersion7(), "conduct", 1),
            [ModerationCapability.Publish], Now, null, Now.AddDays(1), 2);
        var result = await new ModerationLifecycleValidator().ValidateAsync(
            new ApplyModerationLifecycleCommand(CreateOperation(ModerationOperationKind.Propose, proposal: proposal)));

        await Assert.That(result.IsValid).IsTrue();
    }

    private static ModerationProposal CreateProposal(Guid caseId, Guid ruleOwnerId) =>
        new(caseId, ModerationRuleReference.Create(ruleOwnerId, "conduct", 1),
            [ModerationCapability.Publish], Now, Now.AddDays(2), Now.AddDays(1), 1);

    private static ModerationOperation CreateOperation(
        ModerationOperationKind kind, int identitySlot = -1, Guid? invalidId = null,
        Guid? referenceId = null, Guid? oldSanctionId = null, ModerationProposal? proposal = null)
    {
        var operationId = identitySlot == 0 ? invalidId!.Value : Guid.CreateVersion7();
        var effectId = identitySlot == 1 ? invalidId!.Value : Guid.CreateVersion7();
        var decisionId = identitySlot == 2 ? invalidId!.Value : Guid.CreateVersion7();
        return kind switch
        {
            ModerationOperationKind.Propose => ModerationOperation.Propose(
                Subject, Target, operationId, effectId, decisionId, 1, 1,
                proposal ?? CreateProposal(Guid.CreateVersion7(), Guid.CreateVersion7())),
            ModerationOperationKind.Reconcile => ModerationOperation.Reconcile(
                Subject, Target, operationId, effectId, decisionId, 1, 1, referenceId ?? Guid.CreateVersion7(), 1),
            ModerationOperationKind.Supersede => ModerationOperation.Supersede(
                Subject, Target, operationId, effectId, decisionId, 1, 1,
                referenceId ?? Guid.CreateVersion7(), oldSanctionId ?? Guid.CreateVersion7(), Target, 1),
            _ => ModerationOperation.Transition(kind, Subject, Target, operationId, effectId, decisionId, 1, 1,
                kind is ModerationOperationKind.AcceptFinding or ModerationOperationKind.Activate or ModerationOperationKind.Revoke
                    ? referenceId ?? Guid.CreateVersion7() : Guid.Empty)
        };
    }
}
