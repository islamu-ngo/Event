using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationLifecycleCommandTests
{
    private static readonly AuthorityScope Target = AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(1)), Id(2)), Id(3)), Id(4));
    private static readonly SubjectReference Subject = SubjectReference.Create(ModerationSubjectKind.Human, Id(5), Target);
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Every_typed_lifecycle_request_is_discovered_as_one_scoped_native_handler()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        for (var variant = 0; variant < 10; variant++)
        {
            var request = Create(variant).GetType();
            var contract = typeof(ICommandHandler<,>).MakeGenericType(request, typeof(ModerationLifecycleResponse));
            await Assert.That(services.Count(descriptor => descriptor.ServiceType == contract)).IsEqualTo(1);
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == contract).Lifetime)
                .IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    [Test]
    [Arguments(0, ModerationOperationKind.Propose)]
    [Arguments(1, ModerationOperationKind.RecordResponse)]
    [Arguments(2, ModerationOperationKind.AcceptFinding)]
    [Arguments(3, ModerationOperationKind.Approve)]
    [Arguments(4, ModerationOperationKind.Activate)]
    [Arguments(5, ModerationOperationKind.Reject)]
    [Arguments(6, ModerationOperationKind.Withdraw)]
    [Arguments(7, ModerationOperationKind.Revoke)]
    [Arguments(8, ModerationOperationKind.Reconcile)]
    [Arguments(9, ModerationOperationKind.Supersede)]
    public async Task Typed_intent_preserves_exact_identity_revisions_and_transition(
        int variant, ModerationOperationKind expectedKind)
    {
        var command = Create(variant);
        var validation = await Validate(command);
        await Assert.That(validation.IsValid).IsTrue();
        var operation = command.ToOperation();
        await Assert.That(operation.Kind).IsEqualTo(expectedKind);
        await Assert.That(operation.OperationId).IsEqualTo(Id(10));
        await Assert.That(operation.EffectId).IsEqualTo(Id(11));
        await Assert.That(operation.DecisionId).IsEqualTo(Id(12));
        await Assert.That(operation.ExpectedRevision).IsEqualTo(6L);
        await Assert.That(operation.ExpectedPolicyRevision).IsEqualTo(8L);
        await Assert.That(operation.Subject).IsEqualTo(Subject);
        await Assert.That(operation.Target).IsEqualTo(Target);
        await Assert.That(operation.Capability).IsNull();
        var reference = variant is 2 or 4 or 7 or 8 or 9 ? Id(13) : Guid.Empty;
        await Assert.That(operation.ReferenceId).IsEqualTo(reference);
        await Assert.That(operation.ExpectedSanctionRevision).IsEqualTo(variant is 8 or 9 ? 4L : 0L);
        await Assert.That(operation.SupersededSanctionId).IsEqualTo(variant == 9 ? Id(14) : Guid.Empty);
        await Assert.That(operation.SupersededScope).IsEqualTo(variant == 9 ? Target : null);
        await Assert.That(operation.Proposal?.CaseId).IsEqualTo(variant == 0 ? Id(15) : (Guid?)null);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    public async Task Invalid_body_coordinates_return_validation_instead_of_throwing(int variant)
    {
        var command = Create(variant);
        await Assert.That((await Validate(command with { OperationId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await Validate(command with { EffectId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await Validate(command with { DecisionId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await Validate(command with { ExpectedRevision = 0 })).IsValid).IsFalse();
        await Assert.That((await Validate(command with { ExpectedPolicyRevision = 0 })).IsValid).IsFalse();
        await Assert.That((await Validate(command with { Subject = null! })).IsValid).IsFalse();
        await Assert.That((await Validate(command with { Target = null! })).IsValid).IsFalse();
        var foreign = AuthorityScope.TrustDomain(Id(30));
        await Assert.That((await Validate(command with { Target = foreign })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Non_uuidv7_or_non_rfc_identity_cannot_enter_any_typed_mutation(int identity)
    {
        var invalid = identity switch
        {
            0 => Guid.NewGuid(),
            1 => Guid.Parse("019a0000-0000-7000-7000-000000000001"),
            _ => Guid.Empty
        };
        for (var variant = 0; variant < 10; variant++)
        {
            var command = Create(variant);
            await Assert.That((await Validate(command with { OperationId = invalid })).IsValid).IsFalse();
            await Assert.That((await Validate(command with { EffectId = invalid })).IsValid).IsFalse();
            await Assert.That((await Validate(command with { DecisionId = invalid })).IsValid).IsFalse();
        }
    }

    [Test]
    public async Task Referenced_findings_and_contributions_require_exact_stable_identities()
    {
        await Assert.That((await Validate(((AcceptModerationFindingCommand)Create(2)) with
        { FindingId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await Validate(((ActivateModerationDecisionCommand)Create(4)) with
        { SanctionId = Guid.NewGuid() })).IsValid).IsFalse();
        await Assert.That((await Validate(((RevokeModerationSanctionCommand)Create(7)) with
        { SanctionId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await Validate(((ReconcileModerationSanctionCommand)Create(8)) with
        { ExpectedSanctionRevision = 0 })).IsValid).IsFalse();
        var supersede = (SupersedeModerationSanctionCommand)Create(9);
        await Assert.That((await Validate(supersede with { SupersededSanctionId = supersede.SanctionId })).IsValid).IsFalse();
        await Assert.That((await Validate(supersede with { SupersededScope = null! })).IsValid).IsFalse();
        await Assert.That((await Validate(supersede with { SupersededScope = AuthorityScope.TrustDomain(Id(30)) })).IsValid).IsFalse();
    }

    [Test]
    public async Task Proposal_cannot_omit_case_terms_or_reduce_broad_scope_quorum()
    {
        var proposal = (ProposeModerationDecisionCommand)Create(0);
        await Assert.That((await Validate(proposal with { Proposal = null! })).IsValid).IsFalse();
        var instance = Target.Parent!;
        var broad = instance.Parent!.Parent!;
        await Assert.That((await Validate(proposal with { Target = broad })).IsValid).IsFalse();
        var wideProposal = new ModerationProposal(Id(15), proposal.Proposal.Rule,
            [ModerationCapability.Publish], Now, Now.AddDays(1), null, 2);
        await Assert.That((await Validate(proposal with { Target = broad, Proposal = wideProposal })).IsValid).IsTrue();
    }

    private static Task<FluentValidation.Results.ValidationResult> Validate(ModerationLifecycleCommand command) =>
        new ModerationLifecycleCommandValidator<ModerationLifecycleCommand>().ValidateAsync(command);

    private static ModerationLifecycleCommand Create(int variant)
    {
        ModerationLifecycleCommand command = variant switch
        {
            0 => new ProposeModerationDecisionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8,
                Proposal = new ModerationProposal(Id(15), ModerationRuleReference.Create(Id(16), "conduct", 3),
                    [ModerationCapability.Publish], Now, Now.AddDays(1), null, 1)
            },
            1 => new RecordModerationDecisionResponseCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8
            },
            2 => new AcceptModerationFindingCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8,
                FindingId = Id(13)
            },
            3 => new ApproveModerationDecisionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8
            },
            4 => new ActivateModerationDecisionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8,
                SanctionId = Id(13)
            },
            5 => new RejectModerationDecisionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8
            },
            6 => new WithdrawModerationDecisionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8
            },
            7 => new RevokeModerationSanctionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8,
                SanctionId = Id(13)
            },
            8 => new ReconcileModerationSanctionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8,
                SanctionId = Id(13),
                ExpectedSanctionRevision = 4
            },
            9 => new SupersedeModerationSanctionCommand
            {
                OperationId = Id(10),
                EffectId = Id(11),
                DecisionId = Id(12),
                Subject = Subject,
                Target = Target,
                ExpectedRevision = 6,
                ExpectedPolicyRevision = 8,
                SanctionId = Id(13),
                SupersededSanctionId = Id(14),
                SupersededScope = Target,
                ExpectedSanctionRevision = 4
            },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        return command;
    }

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
