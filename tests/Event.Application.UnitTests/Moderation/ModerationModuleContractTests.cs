using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public class ModerationModuleContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Current_permission_is_resolved_inside_the_store_authority_fence()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Authority.WasFenced).IsTrue();
    }

    [Test]
    public async Task Denied_subject_proof_does_not_become_a_protected_remedy_bypass()
    {
        var fixture = new Fixture { Permission = ModerationOperationOutcome.Denied };
        var result = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Contestation));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(result.Evaluation is null).IsTrue();
    }

    [Test]
    public async Task Unknown_permission_cannot_produce_clean_unrestricted_success()
    {
        var fixture = new Fixture { Permission = ModerationOperationOutcome.Unavailable };
        var result = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
    }

    [Test]
    [Arguments(ModerationCapability.ProtectedStatus)]
    [Arguments(ModerationCapability.Contestation)]
    [Arguments(ModerationCapability.Support)]
    [Arguments(ModerationCapability.PrivacyErasure)]
    public async Task Positively_owned_remedy_survives_unknown_restriction_authority(ModerationCapability capability)
    {
        var fixture = new Fixture { RestrictionAuthority = ModerationAuthorityState.Unknown };
        var result = await fixture.Service.ExecuteAsync(fixture.Evaluation(capability));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
    }

    [Test]
    public async Task Unknown_restriction_authority_closes_ordinary_capability()
    {
        var fixture = new Fixture { RestrictionAuthority = ModerationAuthorityState.Pending };
        var result = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
    }

    [Test]
    public async Task Current_contribution_denies_only_its_target_capability()
    {
        var fixture = new Fixture();
        fixture.Store.Sanctions = [fixture.ApprovedDecision().Activate(Guid.CreateVersion7(), 5, Now)];

        var publish = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));
        var attend = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Participate));

        await Assert.That(publish.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(attend.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
    }

    [Test]
    public async Task Foreign_purpose_grant_is_not_reused_for_another_operation()
    {
        var fixture = new Fixture { GrantedPurpose = ModerationOperationKind.Activate };
        var result = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Stale_policy_revision_cannot_create_a_contribution_or_effect()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ExecuteAsync(fixture.Activation(expectedPolicyRevision: 2));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Stale_decision_revision_does_not_activate_an_approved_snapshot()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ExecuteAsync(fixture.Activation(expectedRevision: 4));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
    }

    [Test]
    public async Task Revoked_approval_qualification_cannot_activate_an_old_quorum()
    {
        var fixture = new Fixture { ActivationAuthority = ModerationAuthorityState.Unknown };
        var result = await fixture.Service.ExecuteAsync(fixture.Activation());

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Qualification_read_crossing_the_exact_end_cannot_activate()
    {
        var fixture = new Fixture { AdvanceClockDuringQualification = true };
        var result = await fixture.Service.ExecuteAsync(fixture.Activation());

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Activation_commits_the_exact_stable_contribution_and_effect_together()
    {
        var fixture = new Fixture();
        var operation = fixture.Activation();
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedDecision?.State).IsEqualTo(ModerationDecisionState.Active);
        await Assert.That(fixture.Store.CommittedSanction?.Id).IsEqualTo(operation.ReferenceId);
        await Assert.That(fixture.Store.CommittedEffect?.Id).IsEqualTo(operation.EffectId);
        await Assert.That(fixture.Store.CommittedEffect?.OperationId).IsEqualTo(operation.OperationId);
        await Assert.That(fixture.Store.CommittedEffect?.DecisionRevision).IsEqualTo(result.Revision);
    }

    [Test]
    public async Task Mismatched_effect_identity_rolls_back_instead_of_silently_dropping_delivery()
    {
        var fixture = new Fixture { MismatchEffect = true };
        var result = await fixture.Service.ExecuteAsync(fixture.Activation());

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    [Arguments(ModerationOperationKind.RecordResponse, ModerationDecisionState.Proposed, 1, ModerationDecisionState.AwaitingFinding)]
    [Arguments(ModerationOperationKind.AcceptFinding, ModerationDecisionState.AwaitingFinding, 2, ModerationDecisionState.AwaitingApproval)]
    [Arguments(ModerationOperationKind.Approve, ModerationDecisionState.AwaitingApproval, 3, ModerationDecisionState.AwaitingApproval)]
    [Arguments(ModerationOperationKind.Reject, ModerationDecisionState.Proposed, 1, ModerationDecisionState.Rejected)]
    [Arguments(ModerationOperationKind.Withdraw, ModerationDecisionState.AwaitingFinding, 2, ModerationDecisionState.Withdrawn)]
    public async Task Qualified_operations_own_the_domain_transition_and_durable_pointer(
        ModerationOperationKind kind, ModerationDecisionState initial, int revision, ModerationDecisionState expected)
    {
        var fixture = new Fixture { SeedState = initial, ReviewerPermission = kind == ModerationOperationKind.Approve };
        var operation = fixture.Transition(kind, revision);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedDecision?.State).IsEqualTo(expected);
        await Assert.That(result.Revision).IsEqualTo((long)revision + 1);
        await Assert.That(fixture.Store.CommittedEffect?.Id).IsEqualTo(operation.EffectId);
    }

    private sealed class Fixture
    {
        private readonly Guid _decisionId = Guid.CreateVersion7();
        private readonly Guid _caseId = Guid.CreateVersion7();
        private readonly SubjectReference _issuer;
        private readonly SubjectReference _firstReviewer;
        private readonly SubjectReference _secondReviewer;
        private readonly ModerationRuleReference _rule;
        private readonly FixedClock _clock = new();

        public Fixture()
        {
            var root = AuthorityScope.TrustDomain(Guid.CreateVersion7());
            var solution = AuthorityScope.Solution(root, Guid.CreateVersion7());
            Scope = AuthorityScope.Tenant(
                AuthorityScope.Instance(solution, Guid.CreateVersion7()), Guid.CreateVersion7());
            Subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope);
            _issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope);
            _firstReviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope);
            _secondReviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope);
            _rule = ModerationRuleReference.Create(Scope.TrustDomainId, "native-qualification", 1);
            Store = new InMemoryStore(StoredDecision);
            Authority = new AuthorityContext(this);
            Service = new ModerationOperationService(Store, Authority, new Planner(this), _clock);
        }

        public SubjectReference Subject { get; }
        public AuthorityScope Scope { get; }
        public InMemoryStore Store { get; }
        public AuthorityContext Authority { get; }
        public ModerationOperationService Service { get; }
        public ModerationOperationOutcome Permission { get; init; } = ModerationOperationOutcome.Known;
        public ModerationAuthorityState RestrictionAuthority { get; init; } = ModerationAuthorityState.Known;
        public ModerationAuthorityState ActivationAuthority { get; init; } = ModerationAuthorityState.Known;
        public ModerationOperationKind? GrantedPurpose { get; init; }
        public bool MismatchEffect { get; init; }
        public ModerationDecisionState SeedState { get; init; } = ModerationDecisionState.Approved;
        public bool ReviewerPermission { get; init; }
        public bool AdvanceClockDuringQualification { get; init; }

        public ModerationOperation Evaluation(ModerationCapability capability) =>
            ModerationOperation.Evaluate(Subject, Scope, capability);

        public ModerationOperation Activation(long expectedRevision = 5, long expectedPolicyRevision = 1) =>
            ModerationOperation.Transition(ModerationOperationKind.Activate, Subject, Scope,
                Guid.CreateVersion7(), Guid.CreateVersion7(), _decisionId,
                expectedRevision, expectedPolicyRevision, Guid.CreateVersion7());

        public ModerationOperation Transition(ModerationOperationKind kind, long revision) =>
            ModerationOperation.Transition(kind, Subject, Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
                _decisionId, revision, 1, kind == ModerationOperationKind.AcceptFinding ? Guid.CreateVersion7() : Guid.Empty);

        public ModerationDecision ApprovedDecision() => DecisionAt(ModerationDecisionState.Approved);
        private ModerationDecision StoredDecision() => DecisionAt(SeedState);

        private ModerationDecision DecisionAt(ModerationDecisionState state)
        {
            var decision = ModerationDecision.Propose(_decisionId, _caseId, Subject, _issuer,
                Scope, Scope, _rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 2, Now);
            if (state == ModerationDecisionState.Proposed)
            {
                return decision;
            }

            decision.RecordResponse(1, Now);
            if (state == ModerationDecisionState.AwaitingFinding)
            {
                return decision;
            }

            decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
            if (state == ModerationDecisionState.AwaitingApproval)
            {
                return decision;
            }

            decision.Approve(_firstReviewer, Scope, 3, Now);
            decision.Approve(_secondReviewer, Scope, 4, Now);
            return decision;
        }

        public sealed class AuthorityContext(Fixture fixture) : IModerationAuthorityContext
        {
            public bool WasFenced { get; private set; }

            public Task<ModerationAuthorityResolution> ResolveAsync(
                IModerationSession session, ModerationOperation operation, CancellationToken cancellationToken)
            {
                WasFenced = session is InMemoryStore { InFence: true };
                var resolution = !WasFenced || fixture.Permission == ModerationOperationOutcome.Unavailable
                    ? ModerationAuthorityResolution.Unavailable()
                    : fixture.Permission == ModerationOperationOutcome.Denied
                        ? ModerationAuthorityResolution.Denied()
                        : ModerationAuthorityResolution.Known(new ModerationAuthorityGrant(
                            fixture.ReviewerPermission ? fixture._firstReviewer : fixture._issuer, fixture.Subject, fixture.Scope,
                            fixture.GrantedPurpose ?? operation.Kind, 1, 1, fixture.RestrictionAuthority));
                return Task.FromResult(resolution);
            }

            public Task<ModerationAuthorityState> QualifyActivationAsync(
                IModerationSession session, ModerationDecision decision, ModerationAuthorityGrant grant,
                CancellationToken cancellationToken)
            {
                if (fixture.AdvanceClockDuringQualification)
                {
                    fixture._clock.UtcNow = Now.AddHours(1);
                }

                return Task.FromResult(session is InMemoryStore { InFence: true }
                    ? fixture.ActivationAuthority : ModerationAuthorityState.Unknown);
            }
        }

        private sealed class Planner(Fixture fixture) : IModerationEffectPlanner
        {
            public ModerationPlannedEffect Plan(
                ModerationOperation operation, ModerationDecision decision, ModerationSanction? sanction) =>
                new(fixture.MismatchEffect ? Guid.CreateVersion7() : operation.EffectId,
                    operation.OperationId, decision.Id, decision.Revision, sanction?.Id, sanction?.Revision,
                    decision.Subject, decision.Scope, operation.Kind);
        }
    }

    /// <summary>Fresh isolated entity reads and atomic staged writes model rollback without asserting mock call counts.</summary>
    private sealed class InMemoryStore(Func<ModerationDecision> decisionFactory) : IModerationStore, IModerationSession
    {
        private ModerationDecision? _stagedDecision;
        private ModerationSanction? _stagedSanction;
        private ModerationPlannedEffect? _stagedEffect;

        public bool InFence { get; private set; }
        public IReadOnlyList<ModerationSanction> Sanctions { get; set; } = [];
        public ModerationDecision? CommittedDecision { get; private set; }
        public ModerationSanction? CommittedSanction { get; private set; }
        public ModerationPlannedEffect? CommittedEffect { get; private set; }

        public async Task<ModerationOperationResult> ExecuteAsync(
            ModerationOperation operation,
            Func<IModerationSession, CancellationToken, Task<ModerationOperationResult>> execute,
            CancellationToken cancellationToken)
        {
            InFence = true;
            try
            {
                var result = await execute(this, cancellationToken);
                if (operation.Kind != ModerationOperationKind.Evaluate && result.Outcome == ModerationOperationOutcome.Known)
                {
                    CommittedDecision = _stagedDecision;
                    CommittedSanction = _stagedSanction;
                    CommittedEffect = _stagedEffect;
                }

                return result;
            }
            finally
            {
                InFence = false;
                _stagedDecision = null;
                _stagedSanction = null;
                _stagedEffect = null;
            }
        }

        public Task<ModerationDecision?> FindDecisionAsync(
            Guid decisionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken)
        {
            var decision = decisionFactory();
            return Task.FromResult<ModerationDecision?>(
                decision.Id == decisionId && decision.Subject == subject && decision.Scope == scope ? decision : null);
        }

        public Task<ModerationSanction?> FindSanctionAsync(
            Guid sanctionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(Sanctions.SingleOrDefault(sanction =>
                sanction.Id == sanctionId && sanction.Subject == subject && sanction.Scope == scope));

        public Task<IReadOnlyList<ModerationSanction>> ReadSanctionsAsync(
            SubjectReference subject, CancellationToken cancellationToken) => Task.FromResult(Sanctions);

        public Task<IReadOnlyList<ModerationProtectiveMeasure>> ReadProtectionAsync(
            SubjectReference subject, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModerationProtectiveMeasure>>([]);

        public Task SaveAsync(
            ModerationDecision decision, ModerationSanction? sanction, ModerationAuthorityGrant grant,
            ModerationPlannedEffect effect, CancellationToken cancellationToken)
        {
            _stagedDecision = decision;
            _stagedSanction = sanction;
            _stagedEffect = effect;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
