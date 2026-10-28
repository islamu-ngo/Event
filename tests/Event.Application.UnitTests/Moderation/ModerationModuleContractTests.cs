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

    [Test]
    [Arguments("missing")]
    [Arguments("case")]
    [Arguments("rule")]
    [Arguments("issuer")]
    [Arguments("issuer-origin")]
    [Arguments("reversed")]
    [Arguments("future")]
    [Arguments("purpose")]
    public async Task Ordinary_finding_acceptance_requires_current_independent_case_bound_response(string defect)
    {
        var fixture = new Fixture { SeedState = ModerationDecisionState.AwaitingFinding };
        fixture.Store.Finding = defect == "missing" ? null : fixture.FinalFinding(defect);
        var result = await fixture.Service.ExecuteAsync(fixture.Transition(ModerationOperationKind.AcceptFinding, 2));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Qualified_urgency_acceptance_does_not_invent_a_subject_response()
    {
        var fixture = new Fixture { SeedState = ModerationDecisionState.Proposed };
        fixture.Store.Finding = fixture.FinalFinding("purpose");
        var operation = fixture.Transition(ModerationOperationKind.AcceptFinding, 1);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedDecision?.State).IsEqualTo(ModerationDecisionState.AwaitingApproval);
        await Assert.That(fixture.Store.CommittedDecision?.FindingId).IsEqualTo(fixture.Store.Finding.Id);
        await Assert.That(fixture.Store.CommittedDecision?.Revision).IsEqualTo(2L);
        await Assert.That(fixture.Store.CommittedEffect?.Id).IsEqualTo(operation.EffectId);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
    }

    [Test]
    public async Task Urgency_adjudicator_cannot_reuse_the_issuer_under_another_origin()
    {
        var fixture = new Fixture { SeedState = ModerationDecisionState.Proposed };
        fixture.Store.Finding = fixture.FinalFinding("issuer-origin", true);
        var result = await fixture.Service.ExecuteAsync(fixture.Transition(ModerationOperationKind.AcceptFinding, 1));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Proposal_uses_qualified_issuer_and_grant_scope_without_creating_a_restriction()
    {
        var fixture = new Fixture { GrantParentScope = true };
        var operation = fixture.Proposal();
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedDecision?.Issuer).IsEqualTo(fixture.Issuer);
        await Assert.That(fixture.Store.CommittedDecision?.IssuingScope).IsEqualTo(fixture.Scope.Parent);
        await Assert.That(fixture.Store.CommittedDecision?.CaseId).IsEqualTo(operation.Proposal!.CaseId);
        await Assert.That(fixture.Store.CommittedDecision?.State).IsEqualTo(ModerationDecisionState.Proposed);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
    }

    [Test]
    [Arguments(false, 2, ModerationOperationOutcome.Conflict)]
    [Arguments(true, 2, ModerationOperationOutcome.Conflict)]
    public async Task Proposal_requires_the_exact_open_case_revision(
        bool closed, int revision, ModerationOperationOutcome expected)
    {
        var fixture = new Fixture();
        if (closed) fixture.Store.Case!.Close(1, Now);
        var result = await fixture.Service.ExecuteAsync(fixture.Proposal(revision));

        await Assert.That(result.Outcome).IsEqualTo(expected);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
    }

    [Test]
    public async Task Proposal_cannot_replace_an_existing_decision_identity()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ExecuteAsync(fixture.Proposal(decisionId: fixture.ApprovedDecision().Id));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Proposal_capabilities_are_snapshotted_before_retry()
    {
        var fixture = new Fixture();
        var capabilities = new List<ModerationCapability> { ModerationCapability.Publish };
        var operation = fixture.Proposal(capabilities: capabilities);
        capabilities.Clear();
        capabilities.Add(ModerationCapability.Manage);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedDecision!.Capabilities.Contains(ModerationCapability.Publish)).IsTrue();
        await Assert.That(fixture.Store.CommittedDecision.Capabilities.Contains(ModerationCapability.Manage)).IsFalse();
    }

    [Test]
    public async Task Reconciliation_activates_only_the_expected_scheduled_contribution_at_the_real_start()
    {
        var fixture = new Fixture { StartsAt = Now.AddMinutes(10) };
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddMinutes(10);
        var operation = fixture.Reconciliation(sanction);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedSanction?.State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(fixture.Store.CommittedSanction?.UpdatedAt).IsEqualTo(fixture.UtcNow);
        await Assert.That(fixture.Store.CommittedSanction?.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task Expiry_is_half_open_even_before_materialized_reconciliation()
    {
        var fixture = new Fixture();
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddHours(1);
        var evaluation = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));
        var result = await fixture.Service.ExecuteAsync(fixture.Reconciliation(sanction));

        await Assert.That(evaluation.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedSanction?.State).IsEqualTo(ModerationSanctionState.Expired);
        await Assert.That(fixture.Store.CommittedSanction?.EndsAt).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task Reconciliation_rereads_time_after_current_reviewer_qualification()
    {
        var fixture = new Fixture { StartsAt = Now.AddMinutes(10), AdvanceClockDuringQualification = true };
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddMinutes(10);
        var result = await fixture.Service.ExecuteAsync(fixture.Reconciliation(sanction));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedSanction?.State).IsEqualTo(ModerationSanctionState.Expired);
        await Assert.That(fixture.Store.CommittedSanction?.UpdatedAt).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task Reconciliation_never_invents_a_future_time_to_force_a_state_change()
    {
        var fixture = new Fixture { StartsAt = Now.AddMinutes(10) };
        var sanction = fixture.SeedContribution();
        var result = await fixture.Service.ExecuteAsync(fixture.Reconciliation(sanction));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Scheduled);
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Reconciliation_cannot_activate_with_unavailable_current_qualification()
    {
        var fixture = new Fixture { StartsAt = Now.AddMinutes(10), ActivationAuthority = ModerationAuthorityState.Unknown };
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddMinutes(10);
        var result = await fixture.Service.ExecuteAsync(fixture.Reconciliation(sanction));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.Sanctions.Single().Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Supersession_commits_replacement_and_only_the_requested_old_contribution()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        var independent = fixture.OldContribution();
        fixture.Store.Sanctions = [old, independent];
        var operation = fixture.Supersession(old);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.Sanctions.Single(s => s.Id == old.Id).State).IsEqualTo(ModerationSanctionState.Superseded);
        await Assert.That(fixture.Store.Sanctions.Single(s => s.Id == independent.Id).State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(fixture.Store.Sanctions.Single(s => s.Id == independent.Id).Revision).IsEqualTo(1L);
        await Assert.That(fixture.Store.CommittedSanction?.Id).IsEqualTo(operation.ReferenceId);
        await Assert.That(fixture.Store.CommittedEffect?.SupersededSanctionId).IsEqualTo(old.Id);
        await Assert.That(fixture.Store.CommittedEffect?.SupersededSanctionRevision).IsEqualTo(2L);
    }

    [Test]
    public async Task Supersession_stale_old_revision_rolls_back_the_new_contribution()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        fixture.Store.Sanctions = [old];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old, oldRevision: 2));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
    }

    [Test]
    public async Task Supersession_cannot_widen_the_old_contribution_scope()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution(AuthorityScope.Community(fixture.Scope, Guid.CreateVersion7()));
        fixture.Store.Sanctions = [old];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
    }

    [Test]
    public async Task Supersession_effect_mismatch_leaves_both_entities_unchanged()
    {
        var fixture = new Fixture { MismatchEffect = true };
        var old = fixture.OldContribution();
        fixture.Store.Sanctions = [old];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(fixture.Store.Sanctions.Single().Revision).IsEqualTo(1L);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
    }

    [Test]
    public async Task Supersession_retry_reuses_all_request_identities_and_discards_first_attempt_changes()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        fixture.Store.Sanctions = [old];
        fixture.Store.ReplayBeforeCommit = true;
        var operation = fixture.Supersession(old);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.Sanctions.Count).IsEqualTo(2);
        await Assert.That(fixture.Store.Sanctions.Single(s => s.Id == old.Id).Revision).IsEqualTo(2L);
        await Assert.That(fixture.Store.CommittedSanction?.Id).IsEqualTo(operation.ReferenceId);
        await Assert.That(fixture.Store.CommittedEffect?.Id).IsEqualTo(operation.EffectId);
    }

    [Test]
    public async Task Narrower_supersession_does_not_restore_an_independent_restriction()
    {
        var fixture = new Fixture { GrantParentScope = true };
        var old = fixture.OldContribution(fixture.Scope.Parent);
        var independent = fixture.OldContribution();
        fixture.Store.Sanctions = [old, independent];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));
        var evaluation = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedSanction?.Scope).IsEqualTo(fixture.Scope);
        await Assert.That(evaluation.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(evaluation.Evaluation!.ContributionIds.Contains(independent.Id)).IsTrue();
        await Assert.That(evaluation.Evaluation.ContributionIds.Contains(old.Id)).IsFalse();
    }

    [Test]
    public async Task Correction_permission_must_cover_the_old_scope_not_only_the_new_scope()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution(fixture.Scope.Parent);
        fixture.Store.Sanctions = [old];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
    }

    [Test]
    public async Task Supersession_crossing_replacement_expiry_during_qualification_preserves_old_state()
    {
        var fixture = new Fixture { AdvanceClockDuringQualification = true };
        var old = fixture.OldContribution();
        fixture.Store.Sanctions = [old];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Supersession_requires_current_independent_replacement_reviewers()
    {
        var fixture = new Fixture { ActivationAuthority = ModerationAuthorityState.Pending };
        var old = fixture.OldContribution();
        fixture.Store.Sanctions = [old];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
    }

    [Test]
    public async Task Non_known_commit_outcome_discards_the_entire_staged_supersession()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        fixture.Store.Sanctions = [old];
        fixture.Store.AbortBeforeCommit = true;
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Missing_case_is_not_a_fabricated_allegation()
    {
        var fixture = new Fixture();
        fixture.Store.Case = null;
        var result = await fixture.Service.ExecuteAsync(fixture.Proposal());

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.NotFound);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
    }

    [Test]
    [Arguments(ModerationOperationOutcome.Denied)]
    [Arguments(ModerationOperationOutcome.Unavailable)]
    public async Task Proposal_terms_are_not_proof_of_native_case_or_profile_authority(ModerationOperationOutcome permission)
    {
        var fixture = new Fixture { Permission = permission };
        var result = await fixture.Service.ExecuteAsync(fixture.Proposal());

        await Assert.That(result.Outcome).IsEqualTo(permission);
        await Assert.That(fixture.Store.CommittedDecision is null).IsTrue();
    }

    [Test]
    public async Task Reconciliation_stale_contribution_revision_cannot_change_state()
    {
        var fixture = new Fixture();
        var sanction = fixture.SeedContribution();
        var operation = fixture.Reconciliation(sanction);
        sanction.Revoke(1, Now);
        fixture.UtcNow = Now.AddHours(1);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Revoked);
        await Assert.That(fixture.Store.CommittedEffect is null).IsTrue();
    }

    [Test]
    public async Task Expiry_reconciliation_does_not_require_revoked_activation_qualification()
    {
        var fixture = new Fixture { ActivationAuthority = ModerationAuthorityState.Unknown };
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddHours(1);
        var result = await fixture.Service.ExecuteAsync(fixture.Reconciliation(sanction));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(fixture.Store.CommittedSanction?.State).IsEqualTo(ModerationSanctionState.Expired);
    }

    [Test]
    public async Task Indefinite_review_deadline_is_not_an_invented_expiry()
    {
        var fixture = new Fixture { EndsAt = null, ReviewAt = Now.AddHours(1) };
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddHours(1);
        var result = await fixture.Service.ExecuteAsync(fixture.Reconciliation(sanction));
        var evaluation = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(evaluation.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(fixture.Store.Sanctions.Single().EndsAt is null).IsTrue();
    }

    [Test]
    public async Task Missing_old_contribution_cannot_leave_a_new_replacement()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.NotFound);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
    }

    [Test]
    public async Task Reconciliation_effect_failure_does_not_mutate_the_stored_contribution()
    {
        var fixture = new Fixture { MismatchEffect = true };
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddHours(1);
        var result = await fixture.Service.ExecuteAsync(fixture.Reconciliation(sanction));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(fixture.Store.Sanctions.Single().Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Temporary_protection_expiry_does_not_create_a_punitive_contribution()
    {
        var fixture = new Fixture();
        var protection = ModerationProtectiveMeasure.Create(Guid.CreateVersion7(), fixture.Subject, fixture.Issuer,
            fixture.Scope, fixture.Scope, [ModerationCapability.Publish], "temporary risk",
            Now, Now.AddMinutes(30), Now.AddMinutes(10));
        fixture.Store.Protection = [protection];
        var before = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));
        fixture.UtcNow = Now.AddMinutes(30);
        var after = await fixture.Service.ExecuteAsync(fixture.Evaluation(ModerationCapability.Publish));
        var operation = ModerationOperation.Reconcile(fixture.Subject, fixture.Scope, Guid.CreateVersion7(),
            Guid.CreateVersion7(), fixture.ApprovedDecision().Id, 5, 1, protection.Id, protection.Revision);
        var reconciliation = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(before.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(after.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(reconciliation.Outcome).IsEqualTo(ModerationOperationOutcome.NotFound);
        await Assert.That(fixture.Store.Sanctions.Count).IsEqualTo(0);
        await Assert.That(protection.EndsAt).IsEqualTo(Now.AddMinutes(30));
        await Assert.That(protection.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Reconciliation_requires_the_exact_decision_revision_as_well_as_contribution_revision()
    {
        var fixture = new Fixture();
        var sanction = fixture.SeedContribution();
        fixture.UtcNow = Now.AddHours(1);
        var operation = ModerationOperation.Reconcile(fixture.Subject, fixture.Scope, Guid.CreateVersion7(),
            Guid.CreateVersion7(), sanction.DecisionId, 5, 1, sanction.Id, sanction.Revision);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.Single().Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Supersession_requires_the_exact_approved_replacement_revision()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        fixture.Store.Sanctions = [old];
        var operation = ModerationOperation.Supersede(fixture.Subject, fixture.Scope, Guid.CreateVersion7(),
            Guid.CreateVersion7(), fixture.ApprovedDecision().Id, 4, 1, Guid.CreateVersion7(), old.Id, old.Scope, 1);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Active);
    }

    [Test]
    public async Task Supersession_does_not_resurrect_a_revoked_contribution()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        old.Revoke(1, Now);
        fixture.Store.Sanctions = [old];
        var result = await fixture.Service.ExecuteAsync(fixture.Supersession(old, oldRevision: 2));

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.Single().State).IsEqualTo(ModerationSanctionState.Revoked);
        await Assert.That(fixture.Store.CommittedSanction is null).IsTrue();
    }

    [Test]
    public async Task Supersession_rejects_a_new_identity_already_used_by_an_independent_contribution()
    {
        var fixture = new Fixture();
        var old = fixture.OldContribution();
        var independent = fixture.OldContribution();
        fixture.Store.Sanctions = [old, independent];
        var operation = ModerationOperation.Supersede(fixture.Subject, fixture.Scope, Guid.CreateVersion7(),
            Guid.CreateVersion7(), fixture.ApprovedDecision().Id, 5, 1, independent.Id, old.Id, old.Scope, 1);
        var result = await fixture.Service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await Assert.That(fixture.Store.Sanctions.All(s => s.State == ModerationSanctionState.Active && s.Revision == 1)).IsTrue();
    }

    private sealed class Fixture
    {
        private readonly Guid _decisionId = Guid.CreateVersion7();
        private readonly Guid _caseId = Guid.CreateVersion7();
        private readonly Guid _findingId = Guid.CreateVersion7();
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
            Store.Case = ModerationCase.Open(_caseId, Subject, Scope, Guid.CreateVersion7(), Now);
            Store.Finding = FinalFinding();
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
        public bool GrantParentScope { get; init; }
        public DateTimeOffset StartsAt { get; init; } = Now;
        public DateTimeOffset? EndsAt { get; init; } = Now.AddHours(1);
        public DateTimeOffset? ReviewAt { get; init; }
        public SubjectReference Issuer => _issuer;
        public DateTimeOffset UtcNow { get => _clock.UtcNow; set => _clock.UtcNow = value; }

        public ModerationOperation Proposal(
            long caseRevision = 1, Guid? decisionId = null, IEnumerable<ModerationCapability>? capabilities = null) =>
            ModerationOperation.Propose(Subject, Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
                decisionId ?? Guid.CreateVersion7(), caseRevision, 1,
                new ModerationProposal(_caseId, _rule, capabilities ?? [ModerationCapability.Publish],
                    Now, Now.AddHours(1), null, 2));

        public ModerationSanction SeedContribution()
        {
            var decision = ApprovedDecision();
            var sanction = decision.Activate(Guid.CreateVersion7(), 5, Now);
            Store.Decision = decision;
            Store.Sanctions = [sanction];
            return sanction;
        }

        public ModerationOperation Reconciliation(ModerationSanction sanction) =>
            ModerationOperation.Reconcile(Subject, Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
                sanction.DecisionId, 6, 1, sanction.Id, sanction.Revision);

        public ModerationOperation Supersession(ModerationSanction old, long oldRevision = 1) =>
            ModerationOperation.Supersede(Subject, Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
                _decisionId, 5, 1, Guid.CreateVersion7(), old.Id, old.Scope, oldRevision);

        public ModerationSanction OldContribution(AuthorityScope? scope = null)
        {
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), _caseId, Subject, _issuer,
                Scope.Parent!, scope ?? Scope, _rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 2, Now);
            decision.RecordResponse(1, Now);
            decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
            decision.Approve(_firstReviewer, Scope.Parent!, 3, Now);
            decision.Approve(_secondReviewer, Scope.Parent!, 4, Now);
            return decision.Activate(Guid.CreateVersion7(), 5, Now);
        }

        public ModerationOperation Evaluation(ModerationCapability capability) =>
            ModerationOperation.Evaluate(Subject, Scope, capability);

        public ModerationOperation Activation(long expectedRevision = 5, long expectedPolicyRevision = 1) =>
            ModerationOperation.Transition(ModerationOperationKind.Activate, Subject, Scope,
                Guid.CreateVersion7(), Guid.CreateVersion7(), _decisionId,
                expectedRevision, expectedPolicyRevision, Guid.CreateVersion7());

        public ModerationOperation Transition(ModerationOperationKind kind, long revision) =>
            ModerationOperation.Transition(kind, Subject, Scope, Guid.CreateVersion7(), Guid.CreateVersion7(),
                _decisionId, revision, 1, kind == ModerationOperationKind.AcceptFinding ? _findingId : Guid.Empty);

        public ModerationFinding FinalFinding(string? defect = null, bool urgent = false)
        {
            var moderationCase = defect == "case"
                ? ModerationCase.Open(Guid.CreateVersion7(), Subject, Scope, Guid.CreateVersion7(), Now)
                : Store.Case!;
            var rule = defect == "rule" ? ModerationRuleReference.Create(_rule.OwnerId, _rule.Code, 2) : _rule;
            var adjudicator = defect switch
            {
                "issuer" => _issuer,
                "issuer-origin" => SubjectReference.Create(ModerationSubjectKind.Human, _issuer.Id, Scope.Parent!),
                _ => _firstReviewer
            };
            var evidence = urgent || defect == "purpose"
                ? ModerationFindingEvidenceReference.QualifiedUrgency(Guid.CreateVersion7(), Guid.CreateVersion7(),
                    moderationCase.Id, 1, Now)
                : ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), moderationCase.Id, 1, Now);
            var finding = ModerationFinding.Finalize(_findingId, moderationCase, 1,
                moderationCase.Scope, null, null, rule, rule, _rule, _rule,
                adjudicator, evidence, "Checked evidence supports this case-specific finding.",
                defect == "future" ? Now.AddMinutes(1) : Now);
            if (defect == "reversed")
                finding.Reverse(Guid.CreateVersion7(), _secondReviewer,
                    "Independent review corrected the finding.", 1, Now);
            return finding;
        }

        public ModerationDecision ApprovedDecision() => DecisionAt(ModerationDecisionState.Approved);
        private ModerationDecision StoredDecision() => DecisionAt(SeedState);

        private ModerationDecision DecisionAt(ModerationDecisionState state)
        {
            var decision = ModerationDecision.Propose(_decisionId, _caseId, Subject, _issuer,
                Scope, Scope, _rule, [ModerationCapability.Publish], StartsAt, EndsAt, ReviewAt, 2, Now);
            if (state == ModerationDecisionState.Proposed)
            {
                return decision;
            }

            decision.RecordResponse(1, Now);
            if (state == ModerationDecisionState.AwaitingFinding)
            {
                return decision;
            }

            decision.AcceptFinding(_findingId, 2, Now);
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
                            fixture.ReviewerPermission ? fixture._firstReviewer : fixture._issuer, fixture.Subject,
                            fixture.GrantParentScope ? fixture.Scope.Parent! : fixture.Scope,
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
                ModerationOperation operation, ModerationDecision decision, ModerationSanction? sanction,
                ModerationSanction? supersededSanction) =>
                new(fixture.MismatchEffect ? Guid.CreateVersion7() : operation.EffectId,
                    operation.OperationId, decision.Id, decision.Revision, sanction?.Id, sanction?.Revision,
                    decision.Subject, decision.Scope, operation.Kind,
                    supersededSanction?.Id, supersededSanction?.Revision);
        }
    }

    /// <summary>Fresh isolated entity reads and atomic staged writes model rollback without asserting mock call counts.</summary>
    private sealed class InMemoryStore(Func<ModerationDecision> decisionFactory) : IModerationStore, IModerationSession
    {
        private ModerationDecision? _stagedDecision;
        private ModerationSanction? _stagedSanction;
        private ModerationPlannedEffect? _stagedEffect;
        private ModerationSanction? _stagedSuperseded;

        public bool InFence { get; private set; }
        public bool ReplayBeforeCommit { get; set; }
        public bool AbortBeforeCommit { get; set; }
        public ModerationCase? Case { get; set; }
        public ModerationFinding? Finding { get; set; }
        public ModerationDecision? Decision { get; set; }
        public IReadOnlyList<ModerationSanction> Sanctions { get; set; } = [];
        public IReadOnlyList<ModerationProtectiveMeasure> Protection { get; set; } = [];
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
                if (ReplayBeforeCommit && result.Outcome == ModerationOperationOutcome.Known)
                {
                    _stagedDecision = null;
                    _stagedSanction = null;
                    _stagedSuperseded = null;
                    _stagedEffect = null;
                    result = await execute(this, cancellationToken);
                }

                if (AbortBeforeCommit)
                {
                    return ModerationOperationResult.Unavailable();
                }

                if (operation.Kind != ModerationOperationKind.Evaluate && result.Outcome == ModerationOperationOutcome.Known)
                {
                    CommittedDecision = _stagedDecision;
                    Decision = _stagedDecision;
                    CommittedSanction = _stagedSanction;
                    CommittedEffect = _stagedEffect;
                    var sanctions = Sanctions.ToDictionary(s => s.Id);
                    if (_stagedSanction is not null) sanctions[_stagedSanction.Id] = Snapshot(_stagedSanction);
                    if (_stagedSuperseded is not null) sanctions[_stagedSuperseded.Id] = Snapshot(_stagedSuperseded);
                    Sanctions = sanctions.Values.ToArray();
                }

                return result;
            }
            finally
            {
                InFence = false;
                _stagedDecision = null;
                _stagedSanction = null;
                _stagedEffect = null;
                _stagedSuperseded = null;
            }
        }

        public Task<ModerationDecision?> FindDecisionAsync(
            Guid decisionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken)
        {
            var decision = Snapshot(Decision ?? decisionFactory());
            return Task.FromResult<ModerationDecision?>(
                decision.Id == decisionId && decision.Subject == subject && decision.Scope == scope ? decision : null);
        }

        public Task<ModerationSanction?> FindSanctionAsync(
            Guid sanctionId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(Sanctions.Where(sanction =>
                sanction.Id == sanctionId && sanction.Subject == subject && sanction.Scope == scope)
                .Select(Snapshot).SingleOrDefault());

        public Task<ModerationFinding?> FindFindingAsync(
            Guid findingId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(Finding is { } value && value.Id == findingId
                && value.Subject == subject && value.Scope == scope ? Snapshot(value) : null);

        public Task<ModerationCase?> FindCaseAsync(
            Guid caseId, SubjectReference subject, AuthorityScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(Case is { } value && value.Id == caseId && value.Subject == subject && value.Scope == scope
                ? Snapshot(value) : null);

        public Task<IReadOnlyList<ModerationSanction>> ReadSanctionsAsync(
            SubjectReference subject, CancellationToken cancellationToken) => Task.FromResult(Sanctions);

        public Task<IReadOnlyList<ModerationProtectiveMeasure>> ReadProtectionAsync(
            SubjectReference subject, CancellationToken cancellationToken) =>
            Task.FromResult(Protection);

        public Task SaveAsync(
            ModerationDecision decision, ModerationSanction? sanction, ModerationSanction? supersededSanction,
            ModerationAuthorityGrant grant,
            ModerationPlannedEffect effect, CancellationToken cancellationToken)
        {
            _stagedDecision = decision;
            _stagedSanction = sanction;
            _stagedEffect = effect;
            _stagedSuperseded = supersededSanction;
            return Task.CompletedTask;
        }

        // Domain aggregates store scalar values and immutable references. Copies isolate real lifecycle mutations.
        private static T Snapshot<T>(T entity) where T : class =>
            (T)typeof(object).GetMethod("MemberwiseClone",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(entity, null)!;
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
