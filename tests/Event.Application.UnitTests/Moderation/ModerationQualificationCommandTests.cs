using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationQualificationCommandTests
{
    private static readonly Guid HumanId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab");
    private static readonly AuthorityScope Target = AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32)), Id(33));
    private static readonly ModerationRuleReference Rule = ModerationRuleReference.Create(Target.Id, "conduct", 3);
    private static readonly ModerationRuleReference Reason = ModerationRuleReference.Create(Target.Id, "conduct-reason", 5);
    private static readonly DateTimeOffset Until = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

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
    public async Task Native_variants_preserve_exact_intent_and_do_not_certify_authority(int variant)
    {
        var command = Create(variant);
        var validation = await Validate(command);
        await Assert.That(validation.IsValid).IsTrue();
        await Assert.That(command.ToMutation()).IsEqualTo(Expected(variant));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Operation_identity_requires_uuidv7_and_rfc_variant(int invalidKind)
    {
        var invalid = invalidKind switch
        {
            0 => Guid.Empty,
            1 => HumanId,
            _ => Guid.Parse("019a0000-0000-7000-7000-000000000001")
        };
        for (var variant = 0; variant < 9; variant++)
            await Assert.That((await Validate(Create(variant) with { OperationId = invalid })).IsValid).IsFalse();
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
    public async Task Missing_target_and_nonpositive_qualification_generation_are_invalid(int variant)
    {
        await Assert.That((await Validate(Create(variant) with { Target = null! })).IsValid).IsFalse();
        await Assert.That((await Validate(Create(variant) with { ExpectedQualificationRevision = 0 })).IsValid).IsFalse();
        await Assert.That((await Validate(Create(variant) with { ExpectedQualificationRevision = -1 })).IsValid).IsFalse();
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
    public async Task New_or_referenced_source_identity_requires_uuidv7(int variant)
    {
        ModerationQualificationCommand command = Create(variant) switch
        {
            OpenModerationCaseCommand value => value with { CaseId = HumanId },
            RecordModerationCaseResponseCommand value => value with { EvidenceId = HumanId },
            AssignModerationReviewerCommand value => value with { AssignmentId = HumanId },
            QualifyModerationReviewerControllerCommand value => value with { EvidenceId = HumanId },
            AssessModerationReviewerConflictCommand value => value with { AssignmentId = HumanId },
            WithdrawModerationReviewerCommand value => value with { AssignmentId = HumanId },
            QualifyModerationCaseUrgencyCommand value => value with { EvidenceId = HumanId },
            FinalizeModerationFindingCommand value => value with { FindingId = HumanId },
            ReverseModerationFindingCommand value => value with { ReversalReferenceId = HumanId },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await Assert.That((await Validate(command)).IsValid).IsFalse();
    }

    [Test]
    public async Task Existing_human_identities_are_opaque_but_not_empty()
    {
        var opening = (OpenModerationCaseCommand)Create(0);
        var assignment = (AssignModerationReviewerCommand)Create(2);
        var controller = (QualifyModerationReviewerControllerCommand)Create(3);
        await Assert.That((await Validate(opening)).IsValid).IsTrue();
        await Assert.That((await Validate(assignment)).IsValid).IsTrue();
        await Assert.That((await Validate(controller)).IsValid).IsTrue();
        await Assert.That((await Validate(opening with { SubjectId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await Validate(assignment with { ReviewerHumanId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await Validate(controller with { ControllerHumanId = Guid.Empty })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Generic_entry_preserves_the_same_opaque_canonical_identity(int variant)
    {
        var command = Create(variant);
        var validator = new ModerationQualificationValidator();
        await Assert.That((await validator.ValidateAsync(
            new ApplyModerationQualificationCommand(command.ToMutation()))).IsValid).IsTrue();
        ModerationQualificationCommand invalid = command switch
        {
            OpenModerationCaseCommand value => value with { SubjectId = Guid.Empty },
            AssignModerationReviewerCommand value => value with { ReviewerHumanId = Guid.Empty },
            QualifyModerationReviewerControllerCommand value => value with { ControllerHumanId = Guid.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await Assert.That((await validator.ValidateAsync(
            new ApplyModerationQualificationCommand(invalid.ToMutation()))).IsValid).IsFalse();
    }

    [Test]
    [Arguments(ModerationSubjectKind.Human)]
    [Arguments(ModerationSubjectKind.Service)]
    [Arguments(ModerationSubjectKind.Organization)]
    [Arguments(ModerationSubjectKind.Content)]
    public async Task Every_native_case_subject_kind_reaches_its_exact_source(ModerationSubjectKind kind)
    {
        var command = ((OpenModerationCaseCommand)Create(0)) with { SubjectKind = kind };
        await Assert.That((await Validate(command)).IsValid).IsTrue();
        await Assert.That(((OpenModerationCaseSource)command.ToMutation()).SubjectKind).IsEqualTo(kind);
        await Assert.That(((OpenModerationCaseSource)command.ToMutation()).SubjectId).IsEqualTo(HumanId);
    }

    [Test]
    public async Task Unknown_subject_kind_does_not_enter_native_case_intake()
    {
        var command = ((OpenModerationCaseCommand)Create(0)) with { SubjectKind = (ModerationSubjectKind)99 };
        await Assert.That((await Validate(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task Existing_source_revisions_are_positive_and_never_replaced(int variant)
    {
        ModerationQualificationCommand invalid = Create(variant) switch
        {
            RecordModerationCaseResponseCommand value => value with { ExpectedCaseRevision = 0 },
            AssignModerationReviewerCommand value => value with { ExpectedCaseRevision = 0 },
            QualifyModerationReviewerControllerCommand value => value with { ExpectedAssignmentRevision = 0 },
            AssessModerationReviewerConflictCommand value => value with { ExpectedAssignmentRevision = 0 },
            WithdrawModerationReviewerCommand value => value with { ExpectedAssignmentRevision = 0 },
            QualifyModerationCaseUrgencyCommand value => value with { ExpectedCaseRevision = 0 },
            FinalizeModerationFindingCommand value => value with { ExpectedPolicyRevision = 0 },
            ReverseModerationFindingCommand value => value with { ExpectedFindingRevision = 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await Assert.That((await Validate(invalid)).IsValid).IsFalse();
    }

    [Test]
    public async Task Policy_creation_keeps_zero_expected_revision_distinct_from_replacement()
    {
        var request = Publication();
        var validator = new ModerationPolicyPublicationCommandValidator();
        await Assert.That((await validator.ValidateAsync(new PublishModerationPolicyCommand(request))).IsValid).IsTrue();
        await Assert.That((await validator.ValidateAsync(new PublishModerationPolicyCommand(request with
        {
            Proposed = request.Proposed with { ExpectedRevision = -1 }
        }))).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(new PublishModerationPolicyCommand(request with
        {
            Proposed = request.Proposed with { ExpectedRevision = 1 }
        }))).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(new PublishModerationPolicyCommand(request with
        {
            ExpectedPublicationId = Id(60),
            Proposed = request.Proposed with { ExpectedRevision = 1 }
        }))).IsValid).IsTrue();
    }

    [Test]
    [Arguments(ModerationReviewerConflictState.Unresolved)]
    [Arguments((ModerationReviewerConflictState)0)]
    [Arguments((ModerationReviewerConflictState)4)]
    public async Task Conflict_assessment_requires_a_resolved_finite_state(ModerationReviewerConflictState state)
    {
        await Assert.That((await Validate(((AssessModerationReviewerConflictCommand)Create(4)) with { State = state })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(ModerationReviewerConflictState.Independent)]
    [Arguments(ModerationReviewerConflictState.Conflicted)]
    public async Task Conflict_assessment_is_intent_not_a_caller_permission(ModerationReviewerConflictState state)
    {
        var command = ((AssessModerationReviewerConflictCommand)Create(4)) with { State = state };
        await Assert.That((await Validate(command)).IsValid).IsTrue();
        await Assert.That(((AssessModerationReviewerConflict)command.ToMutation()).State).IsEqualTo(state);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Evidence_validity_is_finite_but_current_clock_and_policy_ceiling_are_native(int sentinel)
    {
        var instant = sentinel == 0 ? DateTimeOffset.MinValue : DateTimeOffset.MaxValue;
        var controller = (QualifyModerationReviewerControllerCommand)Create(3);
        var urgency = (QualifyModerationCaseUrgencyCommand)Create(6);
        await Assert.That((await Validate(controller with { QualifiedUntil = instant })).IsValid).IsFalse();
        await Assert.That((await Validate(urgency with { ValidUntil = instant })).IsValid).IsFalse();
        var past = Until.AddYears(-10);
        await Assert.That((await Validate(controller with { QualifiedUntil = past })).IsValid).IsTrue();
        await Assert.That(((QualifyModerationReviewerController)(controller with { QualifiedUntil = past }).ToMutation()).QualifiedUntil).IsEqualTo(past);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Evidence_requires_substantive_bounded_text(int invalidKind)
    {
        var text = invalidKind switch { 0 => " ", 1 => "!!!", _ => new string('x', 4001) };
        await Assert.That((await Validate(((RecordModerationCaseResponseCommand)Create(1)) with { Statement = text })).IsValid).IsFalse();
        await Assert.That((await Validate(((QualifyModerationReviewerControllerCommand)Create(3)) with { Justification = text })).IsValid).IsFalse();
        await Assert.That((await Validate(((AssessModerationReviewerConflictCommand)Create(4)) with { Justification = text })).IsValid).IsFalse();
        await Assert.That((await Validate(((QualifyModerationCaseUrgencyCommand)Create(6)) with { Justification = text })).IsValid).IsFalse();
    }

    [Test]
    public async Task Finding_requires_separate_rule_and_reason_references_and_bounded_justification()
    {
        var final = (FinalizeModerationFindingCommand)Create(7);
        var reverse = (ReverseModerationFindingCommand)Create(8);
        await Assert.That((await Validate(final with { Rule = null! })).IsValid).IsFalse();
        await Assert.That((await Validate(final with { Reason = null! })).IsValid).IsFalse();
        await Assert.That((await new ModerationQualificationValidator().ValidateAsync(
            new ApplyModerationQualificationCommand((final with { Reason = null! }).ToMutation()))).IsValid).IsFalse();
        await Assert.That((await Validate(final with { Justification = new string('x', 4001) })).IsValid).IsFalse();
        await Assert.That((await Validate(reverse with { Justification = " " })).IsValid).IsFalse();
        await Assert.That(((FinalizeModerationFindingSource)final.ToMutation()).Rule).IsEqualTo(Rule);
        await Assert.That(((FinalizeModerationFindingSource)final.ToMutation()).Reason).IsEqualTo(Reason);
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
    [Arguments(10)]
    [Arguments(11)]
    public async Task Complete_publication_rejects_malformed_profile_declarations(int invalidKind)
    {
        var request = Publication();
        var proposed = request.Proposed;
        var profile = proposed.Profiles.Single();
        proposed = invalidKind switch
        {
            0 => proposed with { Profiles = [] },
            1 => proposed with { Profiles = [null!] },
            2 => proposed with { Profiles = [profile with { Id = HumanId }] },
            3 => proposed with { Profiles = [profile with { Rule = ModerationRuleReference.Create(Id(70), "foreign", 1) }] },
            4 => proposed with { Profiles = [profile with { ReferenceRevision = 0 }] },
            5 => proposed with { Profiles = [profile with { ScopeKinds = [] }] },
            6 => proposed with { Profiles = [profile with { Effects = [] }] },
            7 => proposed with { Profiles = [profile with { Timing = profile.Timing with { Duration = TimeSpan.Zero } }] },
            8 => proposed with { Profiles = [profile with { Timing = profile.Timing with { Duration = TimeSpan.MaxValue } }] },
            9 => proposed with { Profiles = [profile with { RequiredApprovals = 3 }] },
            10 => proposed with { Profiles = [profile, profile with { Id = Id(12) }] },
            _ => proposed with
            {
                Profiles = [profile with
            {
                Timing = new(ModerationReferenceTimingKind.ReviewedIndefinite, TimeSpan.FromHours(1)),
                RequiredApprovals = 1
            }]
            }
        };
        await Assert.That(Compile(proposed).Draft).IsNull();
    }

    [Test]
    public async Task Publication_values_preserve_nested_semantics_after_input_changes()
    {
        var request = Publication();
        var original = request.Proposed.Profiles.Single();
        var scopes = original.ScopeKinds.ToList();
        var effects = original.Effects.ToList();
        var profiles = new List<ModerationPolicyProfileDefinition>
        {
            original with { ScopeKinds = scopes.ToImmutableArray(), Effects = effects.ToImmutableArray() }
        };
        var command = new PublishModerationPolicyCommand(request with
        {
            Proposed = request.Proposed with { Profiles = profiles.ToImmutableArray() }
        });
        scopes.Clear();
        effects.Clear();
        profiles.Clear();
        var draft = Compile(command.Request.Proposed).Draft;
        await Assert.That(draft).IsNotNull();
        await Assert.That(draft!.Profiles.Single().Definition.Rule).IsEqualTo(original.Rule);
        await Assert.That(draft.Profiles.Single().Definition.ScopeKinds.Single()).IsEqualTo(ModerationScopeKind.Tenant);
        await Assert.That(draft.Profiles.Single().Definition.Effects).IsEquivalentTo(original.Effects);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Malformed_policy_collections_or_duplicate_source_ids_fail_validation(int invalidKind)
    {
        var proposed = Publication().Proposed;
        var profile = proposed.Profiles.Single();
        proposed = invalidKind switch
        {
            0 => proposed with { Profiles = default },
            1 => proposed with { Profiles = [profile with { ScopeKinds = default }] },
            2 => proposed with { Profiles = [profile with { Effects = default }] },
            3 => proposed with { Profiles = [profile, profile with { Rule = ModerationRuleReference.Create(Target.Id, "other", 1) }] },
            _ => proposed with { Profiles = [profile with { SubjectKinds = [(ModerationSubjectKind)999] }] }
        };
        await Assert.That(Compile(proposed).Draft).IsNull();
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
    public async Task Every_real_handler_returns_validation_before_requiring_a_repository(int variant)
    {
        var invalid = Create(variant) with { ExpectedQualificationRevision = 0 };
        var response = await ExecuteInvalid(invalid);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Errors).IsNotNull();
        await Assert.That(response.Revision).IsNull();
        await Assert.That(response.QualificationRevision).IsNull();
    }

    [Test]
    [Arguments(ModerationQualificationSourceKind.Case)]
    [Arguments(ModerationQualificationSourceKind.Evidence)]
    [Arguments(ModerationQualificationSourceKind.Reviewer)]
    [Arguments(ModerationQualificationSourceKind.Finding)]
    [Arguments(ModerationQualificationSourceKind.Policy)]
    public async Task Accepted_results_project_only_exact_source_identity_and_revisions(ModerationQualificationSourceKind kind)
    {
        var receipt = ModerationQualificationMutationReceipt.Complete(Id(1), new string('A', 64), Id(40), Id(30), Id(33),
            kind, Id(41), HumanId, 37, 43, Until);
        var result = ModerationQualificationResponse.FromOutcome(Id(1), new(ModerationQualificationWriteOutcome.Known, receipt));
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(Id(41));
        await Assert.That(result.Revision).IsEqualTo(37L);
        await Assert.That(result.QualificationRevision).IsEqualTo(43L);
        await Assert.That(result.FailureCode).IsNull();
    }

    [Test]
    [Arguments(ModerationQualificationWriteOutcome.Unauthenticated, FailureCodes.AuthenticationRequired)]
    [Arguments(ModerationQualificationWriteOutcome.Denied, FailureCodes.AdminRequired)]
    [Arguments(ModerationQualificationWriteOutcome.Conflict, FailureCodes.ConcurrencyConflict)]
    [Arguments(ModerationQualificationWriteOutcome.NotFound, FailureCodes.NotFound)]
    [Arguments(ModerationQualificationWriteOutcome.Unavailable, "ModerationAuthorityUnavailable")]
    [Arguments(ModerationQualificationWriteOutcome.Known, "ModerationAuthorityUnavailable")]
    [Arguments((ModerationQualificationWriteOutcome)99, "ModerationAuthorityUnavailable")]
    public async Task Nonaccepted_outcomes_never_publish_source_revisions(ModerationQualificationWriteOutcome outcome, string code)
    {
        var result = ModerationQualificationResponse.FromOutcome(Id(1), new(outcome));
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(code);
        await Assert.That(result.Revision).IsNull();
        await Assert.That(result.QualificationRevision).IsNull();
        await Assert.That(result.Id).IsEqualTo(outcome == ModerationQualificationWriteOutcome.Conflict ? Id(1) : Guid.Empty);
    }

    private static Task<FluentValidation.Results.ValidationResult> Validate(ModerationQualificationCommand command) =>
        new ModerationQualificationCommandValidator<ModerationQualificationCommand>().ValidateAsync(command);

    private static Task<ModerationQualificationResponse> ExecuteInvalid(ModerationQualificationCommand command) => command switch
    {
        OpenModerationCaseCommand value => new OpenModerationCaseCommandHandler(null!).ExecuteAsync(value),
        RecordModerationCaseResponseCommand value => new RecordModerationCaseResponseCommandHandler(null!).ExecuteAsync(value),
        AssignModerationReviewerCommand value => new AssignModerationReviewerCommandHandler(null!).ExecuteAsync(value),
        QualifyModerationReviewerControllerCommand value => new QualifyModerationReviewerControllerCommandHandler(null!).ExecuteAsync(value),
        AssessModerationReviewerConflictCommand value => new AssessModerationReviewerConflictCommandHandler(null!).ExecuteAsync(value),
        WithdrawModerationReviewerCommand value => new WithdrawModerationReviewerCommandHandler(null!).ExecuteAsync(value),
        QualifyModerationCaseUrgencyCommand value => new QualifyModerationCaseUrgencyCommandHandler(null!).ExecuteAsync(value),
        FinalizeModerationFindingCommand value => new FinalizeModerationFindingCommandHandler(null!).ExecuteAsync(value),
        ReverseModerationFindingCommand value => new ReverseModerationFindingCommandHandler(null!).ExecuteAsync(value),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");

    private static ModerationPolicyPublicationRequest Publication()
    {
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
        var response = reference.Responses[0];
        var classification = ModerationRuleReference.Create(Target.Id, "conduct-class", 1);
        var reason = ModerationRuleReference.Create(Target.Id, "conduct-reason", 1);
        var profile = new ModerationPolicyProfileDefinition(Id(11), Rule, reason,
            reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Tenant],
            [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(), reference.Bundle,
            reference.ProtectionPurpose, [], new(response.Timing.Kind, TimeSpan.FromHours(1)),
            response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
        var proposed = new ModerationPolicyProposedState(Id(8), 0, Id(10), Target,
            ModerationReferenceCatalogue.Revision, inventory.Revision, inventory.Fingerprint,
            ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            [new(classification, "Conduct classification.", reference.Ground, reference.Basis)],
            [new(reason, "Conduct reason.", classification)], [profile], [Rule]);
        return new(Id(1), Id(40), Id(41), 13, ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
    }

    private static ModerationPolicyCompilationResult Compile(ModerationPolicyProposedState proposed) =>
        ModerationPolicyProposedStateCompiler.Compile(proposed,
            new(Id(40), Id(41), 13, ModerationPolicyPublicationProjection.WriterGeneration,
                ModerationPolicyPublicationProjection.AuthoringBounds(Target), null),
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory()));

    private static ModerationQualificationCommand Create(int variant) => variant switch
    {
        0 => new OpenModerationCaseCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            CaseId = Id(2),
            SubjectKind = ModerationSubjectKind.Human,
            SubjectId = HumanId,
            OccurrenceId = Id(4),
        },
        1 => new RecordModerationCaseResponseCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            EvidenceId = Id(5),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            Statement = " Statement preserved exactly ",
        },
        2 => new AssignModerationReviewerCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            ReviewerHumanId = HumanId,
        },
        3 => new QualifyModerationReviewerControllerCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            ExpectedAssignmentRevision = 19,
            ControllerHumanId = HumanId,
            EvidenceId = Id(5),
            Justification = "Reason preserved exactly",
            QualifiedUntil = Until,
        },
        4 => new AssessModerationReviewerConflictCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            ExpectedAssignmentRevision = 19,
            State = ModerationReviewerConflictState.Independent,
            EvidenceId = Id(5),
            Justification = "Reason preserved exactly",
        },
        5 => new WithdrawModerationReviewerCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            ExpectedAssignmentRevision = 19,
        },
        6 => new QualifyModerationCaseUrgencyCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            EvidenceId = Id(5),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            Justification = "Reason preserved exactly",
            ValidUntil = Until,
        },
        7 => new FinalizeModerationFindingCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            FindingId = Id(7),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            PolicyId = Id(8),
            ExpectedPolicyRevision = 23,
            Rule = Rule,
            Reason = Reason,
            EvidenceId = Id(5),
            Justification = "Reason preserved exactly",
        },
        8 => new ReverseModerationFindingCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            FindingId = Id(7),
            ExpectedFindingRevision = 29,
            ReversalReferenceId = Id(9),
            Justification = "Reason preserved exactly",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(variant))
    };

    private static ModerationQualificationMutation Expected(int variant) => variant switch
    {
        0 => new OpenModerationCaseSource
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            CaseId = Id(2),
            SubjectKind = ModerationSubjectKind.Human,
            SubjectId = HumanId,
            OccurrenceId = Id(4),
        },
        1 => new RecordModerationCaseResponse
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            EvidenceId = Id(5),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            Statement = " Statement preserved exactly ",
        },
        2 => new AssignModerationReviewerSource
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            ReviewerHumanId = HumanId,
        },
        3 => new QualifyModerationReviewerController
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            ExpectedAssignmentRevision = 19,
            ControllerHumanId = HumanId,
            EvidenceId = Id(5),
            Justification = "Reason preserved exactly",
            QualifiedUntil = Until,
        },
        4 => new AssessModerationReviewerConflict
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            ExpectedAssignmentRevision = 19,
            State = ModerationReviewerConflictState.Independent,
            EvidenceId = Id(5),
            Justification = "Reason preserved exactly",
        },
        5 => new WithdrawModerationReviewerSource
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AssignmentId = Id(6),
            ExpectedAssignmentRevision = 19,
        },
        6 => new QualifyModerationCaseUrgency
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            EvidenceId = Id(5),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            Justification = "Reason preserved exactly",
            ValidUntil = Until,
        },
        7 => new FinalizeModerationFindingSource
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            FindingId = Id(7),
            CaseId = Id(2),
            ExpectedCaseRevision = 17,
            PolicyId = Id(8),
            ExpectedPolicyRevision = 23,
            Rule = Rule,
            Reason = Reason,
            EvidenceId = Id(5),
            Justification = "Reason preserved exactly",
        },
        8 => new ReverseModerationFindingSource
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            FindingId = Id(7),
            ExpectedFindingRevision = 29,
            ReversalReferenceId = Id(9),
            Justification = "Reason preserved exactly",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(variant))
    };
}
