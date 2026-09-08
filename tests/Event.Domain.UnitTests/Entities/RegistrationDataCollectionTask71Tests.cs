using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationDataCollectionTask71Tests
{
    private static readonly DateTime Now = new(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("0198a2b0-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("0198a2b0-0000-7000-8000-000000000002");

    [Test]
    public async Task RequirementAndChannelOrdinals_ArePositiveAndUniqueWithinOwner()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement first = RegistrationRequirement.Create(
            Id(70), workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);
        RegistrationRequirement duplicateRequirementOrdinal = RegistrationRequirement.Create(
            Id(71), workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);
        RegistrationChannel firstChannel = RegistrationChannel.Create(Id(72), first, 1, true, null, Now);
        RegistrationChannel duplicateChannelOrdinal = RegistrationChannel.Create(Id(73), first, 1, true, null, Now);

        workflow.AddRequirement(first);
        first.AddChannel(firstChannel);

        await Assert.That(first.Ordinal).IsEqualTo(1);
        await Assert.That(firstChannel.Ordinal).IsEqualTo(1);
        await Assert.That(() => workflow.AddRequirement(duplicateRequirementOrdinal)).Throws<ArgumentException>();
        await Assert.That(() => first.AddChannel(duplicateChannelOrdinal)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Id(74), workflow, 0, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RegistrationChannel.Create(Id(75), first, 0, true, null, Now))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RequiredRequirement_UsesAnyChannel_AndWorkflowUsesAllRequirements()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement first = Requirement(workflow, 10);
        RegistrationRequirement second = Requirement(workflow, 20);
        RegistrationChannel firstNative = Native(first, 11);
        RegistrationChannel firstProvider = Provider(first, 12);
        RegistrationChannel secondNative = Native(second, 21);
        workflow.AddRequirement(first);
        workflow.AddRequirement(second);

        RegistrationWorkflowEvaluation blocked = workflow.Evaluate(
            Subject(workflow),
            [Completion(firstNative, false), Completion(firstProvider, true), Completion(secondNative, false)],
            []);
        RegistrationWorkflowEvaluation complete = workflow.Evaluate(
            Subject(workflow),
            [Completion(firstNative, false), Completion(firstProvider, true), Completion(secondNative, true)],
            []);

        await Assert.That(blocked.CanFinalize).IsFalse();
        await Assert.That(complete.CanFinalize).IsTrue();
        await Assert.That(complete.Requirements[0].Outcome).IsEqualTo(RegistrationRequirementEvaluationOutcome.Satisfied);
    }

    [Test]
    public async Task OptionalSkip_IsPureOutcome_WhileRequiredSkipIsRejected()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement optional = Requirement(
            workflow, 10, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration);
        RegistrationRequirement required = Requirement(workflow, 20);
        workflow.AddRequirement(optional);
        workflow.AddRequirement(required);

        RegistrationRequirementEvaluation skipped = optional.Evaluate(Subject(workflow), [], skippedByRegistrant: true);

        await Assert.That(skipped.Outcome).IsEqualTo(RegistrationRequirementEvaluationOutcome.SkippedByRegistrant);
        await Assert.That(skipped.BlocksRegistration).IsFalse();
        await Assert.That(() => required.Evaluate(Subject(workflow), [], skippedByRegistrant: true))
            .Throws<InvalidOperationException>();
        await Assert.That(optional.CanSkip).IsTrue();
    }

    [Test]
    public async Task NonBlockingCriticalities_AndNonApplicableRequirements_NeverBlock()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement informational = Requirement(
            workflow, 10, RegistrationRequirementCriticalityEnum.Informational, true,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect);
        RegistrationRequirement postRegistration = Requirement(
            workflow, 20, RegistrationRequirementCriticalityEnum.PostRegistration, true,
            RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect);
        RegistrationRequirement ticketSpecific = Requirement(
            workflow, 30, subject: RegistrationRequirementSubjectTypeEnum.SpecificTicketType,
            appliesToSubjectId: Guid.Parse("0198a2b0-0000-7000-8000-000000000099"));

        await Assert.That(informational.Evaluate(Subject(workflow), [], false).BlocksRegistration).IsFalse();
        await Assert.That(postRegistration.Evaluate(Subject(workflow), [], false).BlocksRegistration).IsFalse();
        await Assert.That(ticketSpecific.Evaluate(Subject(workflow), [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.NotApplicable);
    }

    [Test]
    public async Task SubjectApplicability_CoversAllSixTypedRules()
    {
        RegistrationWorkflow workflow = Workflow();
        Guid ticketTypeId = Id(91);
        Guid participantId = Id(92);
        Guid sessionId = Id(93);
        RegistrationRequirement allOrders = Requirement(workflow, 10);
        RegistrationRequirement ticket = Requirement(
            workflow, 20, subject: RegistrationRequirementSubjectTypeEnum.SpecificTicketType,
            appliesToSubjectId: ticketTypeId);
        RegistrationRequirement participant = Requirement(
            workflow, 30, subject: RegistrationRequirementSubjectTypeEnum.EveryParticipant);
        RegistrationRequirement lead = Requirement(
            workflow, 40, subject: RegistrationRequirementSubjectTypeEnum.LeadBookerOnly);
        RegistrationRequirement child = Requirement(
            workflow, 50, subject: RegistrationRequirementSubjectTypeEnum.ChildParticipants);
        RegistrationRequirement session = Requirement(
            workflow, 60, subject: RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection,
            appliesToSubjectId: sessionId);
        RegistrationRequirementSubjectContext matching = RegistrationRequirementSubjectContext.Create(
            TenantId, workflow.Id, ticketTypeId, participantId, true, true, sessionId);
        RegistrationRequirementSubjectContext empty = Subject(workflow);

        await Assert.That(allOrders.Evaluate(empty, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.Blocking);
        await Assert.That(ticket.Evaluate(matching, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.Blocking);
        await Assert.That(participant.Evaluate(matching, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.Blocking);
        await Assert.That(lead.Evaluate(matching, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.Blocking);
        await Assert.That(child.Evaluate(matching, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.Blocking);
        await Assert.That(session.Evaluate(matching, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.Blocking);
        await Assert.That(ticket.Evaluate(empty, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.NotApplicable);
        await Assert.That(participant.Evaluate(empty, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.NotApplicable);
        await Assert.That(lead.Evaluate(empty, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.NotApplicable);
        await Assert.That(child.Evaluate(empty, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.NotApplicable);
        await Assert.That(session.Evaluate(empty, [], false).Outcome)
            .IsEqualTo(RegistrationRequirementEvaluationOutcome.NotApplicable);
    }

    [Test]
    public async Task CompletionOnly_RequiresVerifiedEvidence_AndNoneCannotSatisfyRequiredData()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement completionOnly = Requirement(
            workflow, 10, sync: RegistrationAnswerSyncModeEnum.COMPLETION_ONLY);
        RegistrationChannel completionChannel = Native(completionOnly, 11);
        RegistrationRequirement none = Requirement(workflow, 20, sync: RegistrationAnswerSyncModeEnum.NONE);
        RegistrationChannel noneChannel = Native(none, 21);

        await Assert.That(completionOnly.Evaluate(
            Subject(workflow), [Completion(completionChannel, true, verified: false)], false).IsSatisfied).IsFalse();
        await Assert.That(completionOnly.Evaluate(
            Subject(workflow), [Completion(completionChannel, true, verified: true)], false).IsSatisfied).IsTrue();
        await Assert.That(none.Evaluate(
            Subject(workflow), [Completion(noneChannel, true, verified: true)], false).IsSatisfied).IsFalse();
    }

    [Test]
    public async Task Evaluation_RejectsDuplicateAndCrossBoundaryInputs()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement requirement = Requirement(workflow, 10);
        RegistrationChannel channel = Native(requirement, 11);
        RegistrationChannelCompletion completion = Completion(channel, true);

        await Assert.That(() => requirement.Evaluate(Subject(workflow), [completion, completion], false))
            .Throws<ArgumentException>();
        await Assert.That(() => requirement.Evaluate(
            RegistrationRequirementSubjectContext.Create(
                Guid.Parse("0198a2b0-0000-7000-8000-000000000098"), workflow.Id),
            [completion], false)).Throws<ArgumentException>();
        await Assert.That(() => requirement.Evaluate(
            Subject(workflow),
            [completion with { RegistrationRequirementId = Guid.Parse("0198a2b0-0000-7000-8000-000000000097") }],
            false)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Factories_RejectMalformedIdentityEnumsAndCombinations()
    {
        RegistrationWorkflow workflow = Workflow();

        await Assert.That(() => RegistrationWorkflow.Create(
            Guid.Empty, TenantId, EventId, "ticket-registration", Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationWorkflow.Create(
            Id(1), Guid.Empty, EventId, "ticket-registration", Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationWorkflow.Create(
            Id(1), TenantId, Guid.Empty, "ticket-registration", Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationWorkflow.Create(
            Id(1), TenantId, EventId, " ", Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Guid.Empty, workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Id(2), workflow, 1, (RegistrationRequirementCriticalityEnum)999, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Id(2), workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            (RegistrationRequirementCompletionEffectEnum)999,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Id(2), workflow, 1, RegistrationRequirementCriticalityEnum.Required, true,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Id(2), workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Id(2), workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            (RegistrationAnswerSyncModeEnum)999,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirement.Create(
            Id(2), workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            (RegistrationRequirementSubjectTypeEnum)999, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirementSubjectContext.Create(Guid.Empty, workflow.Id))
            .Throws<ArgumentException>();
        await Assert.That(() => RegistrationRequirementSubjectContext.Create(TenantId, Guid.Empty))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Channel_RepresentsNativeWithoutBinding_AndRejectsInvalidShapes()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement requirement = Requirement(workflow, 10);
        RegistrationChannel native = RegistrationChannel.Create(Id(11), requirement, 1, isNative: true, null, Now);
        Guid providerBindingId = Id(12);
        RegistrationChannel provider = RegistrationChannel.Create(Id(12), requirement, 2, isNative: false, providerBindingId, Now);

        await Assert.That(native.RegistrationProviderBindingId).IsNull();
        await Assert.That(provider.RegistrationProviderBindingId).IsEqualTo(providerBindingId);
        await Assert.That(() => RegistrationChannel.Create(Guid.Empty, requirement, 1, true, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationChannel.Create(Id(13), requirement, 1, true, providerBindingId, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationChannel.Create(Id(14), requirement, 1, false, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationChannel.Create(Id(15), requirement, 1, false, Guid.Empty, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task AggregateMutators_RejectDuplicatesAndCrossTenantChildren()
    {
        RegistrationWorkflow workflow = Workflow();
        RegistrationRequirement requirement = Requirement(workflow, 10);
        workflow.AddRequirement(requirement);
        RegistrationChannel channel = Native(requirement, 11);

        await Assert.That(() => workflow.AddRequirement(requirement)).Throws<ArgumentException>();
        await Assert.That(() => workflow.AddRequirement(Requirement(Workflow(Id(99)), 20))).Throws<ArgumentException>();
        await Assert.That(() => requirement.AddChannel(channel)).Throws<ArgumentException>();
        await Assert.That(() => requirement.AddChannel(Native(Requirement(Workflow(Id(98)), 30), 31)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task LookupEnums_UseStableIntegerIdentifiers()
    {
        await Assert.That(Enum.GetValues<RegistrationRequirementCriticalityEnum>().Select(value => (int)value))
            .IsEquivalentTo([1, 2, 3, 4]);
        await Assert.That(Enum.GetValues<RegistrationRequirementCompletionEffectEnum>().Select(value => (int)value))
            .IsEquivalentTo([1, 2, 3]);
        await Assert.That(Enum.GetValues<RegistrationAnswerSyncModeEnum>().Select(value => (int)value))
            .IsEquivalentTo([1, 2, 3, 4, 5]);
        await Assert.That(Enum.GetValues<RegistrationRequirementSubjectTypeEnum>().Select(value => (int)value))
            .IsEquivalentTo([1, 2, 3, 4, 5, 6]);
    }

    private static RegistrationWorkflow Workflow(Guid? id = null) => RegistrationWorkflow.Create(
        id ?? Id(1), TenantId, EventId, "ticket-registration", Now);

    private static RegistrationRequirement Requirement(
        RegistrationWorkflow workflow,
        int id,
        RegistrationRequirementCriticalityEnum criticality = RegistrationRequirementCriticalityEnum.Required,
        bool canSkip = false,
        RegistrationRequirementCompletionEffectEnum effect = RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
        RegistrationAnswerSyncModeEnum sync = RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
        RegistrationRequirementSubjectTypeEnum subject = RegistrationRequirementSubjectTypeEnum.AllOrders,
        Guid? appliesToSubjectId = null) => RegistrationRequirement.Create(
            Id(id), workflow, id, criticality, canSkip, effect, sync, subject, appliesToSubjectId, Now);

    private static RegistrationChannel Native(RegistrationRequirement requirement, int id)
    {
        RegistrationChannel channel = RegistrationChannel.Create(Id(id), requirement, id, true, null, Now);
        requirement.AddChannel(channel);
        return channel;
    }

    private static RegistrationChannel Provider(RegistrationRequirement requirement, int id)
    {
        RegistrationChannel channel = RegistrationChannel.Create(Id(id), requirement, id, false, Id(id + 100), Now);
        requirement.AddChannel(channel);
        return channel;
    }

    private static RegistrationRequirementSubjectContext Subject(RegistrationWorkflow workflow) =>
        RegistrationRequirementSubjectContext.Create(TenantId, workflow.Id);

    private static RegistrationChannelCompletion Completion(
        RegistrationChannel channel,
        bool completed,
        bool verified = true) => new(
            channel.TenantId,
            channel.RegistrationWorkflowId,
            channel.RegistrationRequirementId,
            channel.Id,
            completed,
            verified);

    private static Guid Id(int value) => Guid.Parse($"0198a2b0-0000-7000-8000-{value:000000000000}");
}
