using ISLAMU.Moderation.Domain;

namespace ISLAMU.Moderation.Application;

/// <summary>Owns positively qualified evaluation and domain transition sequencing within the host's local authority fence.</summary>
public sealed class ModerationOperationService(
    IModerationStore store,
    IModerationAuthorityContext authority,
    IModerationEffectPlanner effects,
    TimeProvider clock)
{
    public Task<ModerationOperationResult> ExecuteAsync(
        ModerationOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolution = await authority.ResolveAsync(session, operation, token);
            if (resolution.Outcome == ModerationOperationOutcome.Denied)
            {
                return ModerationOperationResult.Denied();
            }

            if (resolution.Outcome != ModerationOperationOutcome.Known || resolution.Grant is not { } grant)
            {
                return ModerationOperationResult.Unavailable();
            }

            if (grant.Subject != operation.Subject || grant.Purpose != operation.Kind
                || !grant.Scope.Contains(operation.Target))
            {
                return ModerationOperationResult.Denied();
            }

            var now = clock.GetUtcNow();
            if (operation.Kind == ModerationOperationKind.Evaluate && operation.Capability is { } capability)
            {
                IReadOnlyList<ModerationSanction> sanctions = [];
                IReadOnlyList<ModerationProtectiveMeasure> protection = [];
                if (grant.RestrictionAuthority == ModerationAuthorityState.Known
                    && !EffectiveRestrictions.IsProtectedRemedy(capability))
                {
                    sanctions = await session.ReadSanctionsAsync(operation.Subject, token);
                    protection = await session.ReadProtectionAsync(operation.Subject, token);
                }

                return ModerationOperationResult.Evaluated(EffectiveRestrictions.Evaluate(
                    operation.Subject, operation.Target, capability, clock.GetUtcNow(),
                    sanctions, protection, grant.RestrictionAuthority));
            }

            if (grant.RestrictionAuthority != ModerationAuthorityState.Known)
            {
                return ModerationOperationResult.Unavailable();
            }

            if (operation.ExpectedPolicyRevision != grant.PolicyRevision)
            {
                return ModerationOperationResult.Conflict();
            }

            var decision = await session.FindDecisionAsync(
                operation.DecisionId, operation.Subject, operation.Target, token);
            if (operation.Kind == ModerationOperationKind.Propose && operation.Proposal is { } proposal)
            {
                if (decision is not null)
                {
                    return ModerationOperationResult.Conflict();
                }

                var moderationCase = await session.FindCaseAsync(proposal.CaseId, operation.Subject, operation.Target, token);
                if (moderationCase is null)
                {
                    return ModerationOperationResult.NotFound();
                }

                if (moderationCase.Id != proposal.CaseId || moderationCase.Subject != operation.Subject
                    || moderationCase.Scope != operation.Target
                        && (moderationCase.AttributionSourceId is null
                            || operation.Target.Kind != ModerationScopeKind.Resource
                            || moderationCase.Scope != operation.Target.Parent))
                {
                    return ModerationOperationResult.Denied();
                }

                now = clock.GetUtcNow();
                if (moderationCase.State != ModerationCaseState.Open || moderationCase.Revision != operation.ExpectedRevision
                    || now < moderationCase.UpdatedAt || (proposal.EndsAt is not null && now >= proposal.EndsAt))
                {
                    return ModerationOperationResult.Conflict();
                }

                decision = ModerationDecision.Propose(operation.DecisionId, moderationCase.Id, operation.Subject,
                    grant.Principal, grant.Scope, operation.Target, proposal.Rule, proposal.Capabilities,
                    proposal.StartsAt, proposal.EndsAt, proposal.ReviewAt, proposal.RequiredApprovals, now);
            }

            if (decision is null)
            {
                return ModerationOperationResult.NotFound();
            }

            if (decision.Id != operation.DecisionId || decision.Subject != operation.Subject || decision.Scope != operation.Target)
            {
                return ModerationOperationResult.Denied();
            }

            now = clock.GetUtcNow();
            if (now < decision.UpdatedAt
                || (operation.Kind is not (ModerationOperationKind.Revoke or ModerationOperationKind.Propose)
                    && decision.Revision != operation.ExpectedRevision))
            {
                return ModerationOperationResult.Conflict();
            }

            ModerationSanction? sanction = null;
            ModerationSanction? supersededSanction = null;
            switch (operation.Kind)
            {
                case ModerationOperationKind.Propose:
                    break;
                case ModerationOperationKind.RecordResponse:
                    if (decision.State != ModerationDecisionState.Proposed)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    decision.RecordResponse(operation.ExpectedRevision, now);
                    break;
                case ModerationOperationKind.AcceptFinding:
                {
                    if (decision.State is not (ModerationDecisionState.Proposed or ModerationDecisionState.AwaitingFinding))
                    {
                        return ModerationOperationResult.Conflict();
                    }
                    var finding = await session.FindFindingAsync(
                        operation.ReferenceId, operation.Subject, operation.Target, token);
                    now = clock.GetUtcNow();
                    if (now < decision.UpdatedAt)
                    {
                        return ModerationOperationResult.Conflict();
                    }
                    var purpose = decision.State == ModerationDecisionState.Proposed
                        ? ModerationFindingEvidencePurpose.QualifiedUrgency : ModerationFindingEvidencePurpose.Response;
                    if (finding is not { State: ModerationFindingState.Final }
                        || finding.Id != operation.ReferenceId || finding.CaseId != decision.CaseId
                        || finding.Subject != decision.Subject || finding.Scope != decision.Scope
                        || finding.Rule != decision.Rule || finding.EvidencePurpose != purpose
                        || finding.Adjudicator.Kind != ModerationSubjectKind.Human
                        || finding.Adjudicator.Id == decision.Issuer.Id || finding.FinalizedAt > now)
                        return ModerationOperationResult.Denied();

                    if (purpose == ModerationFindingEvidencePurpose.QualifiedUrgency)
                        decision.AcceptUrgentFinding(finding, operation.ExpectedRevision, now);
                    else
                        decision.AcceptFinding(finding.Id, operation.ExpectedRevision, now);
                    break;
                }
                case ModerationOperationKind.Approve:
                    if (decision.State != ModerationDecisionState.AwaitingApproval)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    if (grant.Principal.Kind != ModerationSubjectKind.Human
                        || grant.Principal.Id == decision.Issuer.Id || grant.Principal.Id == decision.Subject.Id
                        || decision.Approvals.Any(approval => approval.Id == grant.Principal.Id))
                    {
                        return ModerationOperationResult.Denied();
                    }

                    decision.Approve(grant.Principal, grant.Scope, operation.ExpectedRevision, now);
                    break;
                case ModerationOperationKind.Activate:
                case ModerationOperationKind.Supersede:
                    if (decision.State != ModerationDecisionState.Approved || (decision.EndsAt is not null && now >= decision.EndsAt))
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    if (operation.Kind == ModerationOperationKind.Supersede)
                    {
                        if (operation.SupersededScope is not { } oldScope || !grant.Scope.Contains(oldScope)
                            || !oldScope.Contains(decision.Scope))
                        {
                            return ModerationOperationResult.Denied();
                        }

                        supersededSanction = await session.FindSanctionAsync(
                            operation.SupersededSanctionId, operation.Subject, oldScope, token);
                        if (supersededSanction is null)
                        {
                            return ModerationOperationResult.NotFound();
                        }

                        if (supersededSanction.Id != operation.SupersededSanctionId
                            || supersededSanction.Subject != decision.Subject || supersededSanction.Scope != oldScope
                            || supersededSanction.DecisionId == decision.Id)
                        {
                            return ModerationOperationResult.Denied();
                        }

                        if (supersededSanction.Revision != operation.ExpectedSanctionRevision
                            || supersededSanction.State is ModerationSanctionState.Revoked or ModerationSanctionState.Superseded)
                        {
                            return ModerationOperationResult.Conflict();
                        }
                    }

                    if (await session.FindSanctionAsync(operation.ReferenceId, operation.Subject, operation.Target, token) is not null)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    if (await authority.QualifyActivationAsync(session, decision, grant, token) != ModerationAuthorityState.Known)
                    {
                        return ModerationOperationResult.Unavailable();
                    }

                    now = clock.GetUtcNow();
                    if (now < decision.UpdatedAt || (supersededSanction is not null && now < supersededSanction.UpdatedAt)
                        || (decision.EndsAt is not null && now >= decision.EndsAt))
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    sanction = decision.Activate(operation.ReferenceId, operation.ExpectedRevision, now);
                    supersededSanction?.Supersede(decision.Id, operation.ExpectedSanctionRevision, now);
                    break;
                case ModerationOperationKind.Reject:
                case ModerationOperationKind.Withdraw:
                    if (decision.State is not (ModerationDecisionState.Proposed or ModerationDecisionState.AwaitingFinding
                        or ModerationDecisionState.AwaitingApproval or ModerationDecisionState.Approved))
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    if (operation.Kind == ModerationOperationKind.Reject)
                    {
                        decision.Reject(operation.ExpectedRevision, now);
                    }
                    else
                    {
                        decision.Withdraw(operation.ExpectedRevision, now);
                    }

                    break;
                case ModerationOperationKind.Revoke:
                case ModerationOperationKind.Reconcile:
                    sanction = await session.FindSanctionAsync(operation.ReferenceId, operation.Subject, operation.Target, token);
                    if (sanction is null)
                    {
                        return ModerationOperationResult.NotFound();
                    }

                    if (sanction.Id != operation.ReferenceId || sanction.DecisionId != decision.Id
                        || sanction.Subject != operation.Subject || sanction.Scope != operation.Target)
                    {
                        return ModerationOperationResult.Denied();
                    }

                    now = clock.GetUtcNow();
                    var sanctionRevision = operation.Kind == ModerationOperationKind.Reconcile
                        ? operation.ExpectedSanctionRevision : operation.ExpectedRevision;
                    if (sanction.Revision != sanctionRevision || now < sanction.UpdatedAt || now < decision.UpdatedAt
                        || sanction.State is ModerationSanctionState.Revoked or ModerationSanctionState.Superseded)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    if (operation.Kind == ModerationOperationKind.Reconcile)
                    {
                        if (decision.State is not (ModerationDecisionState.Scheduled or ModerationDecisionState.Active))
                        {
                            return ModerationOperationResult.Conflict();
                        }

                        if (sanction.State == ModerationSanctionState.Scheduled && now >= sanction.StartsAt
                            && (sanction.EndsAt is null || now < sanction.EndsAt))
                        {
                            if (await authority.QualifyActivationAsync(session, decision, grant, token) != ModerationAuthorityState.Known)
                            {
                                return ModerationOperationResult.Unavailable();
                            }

                            now = clock.GetUtcNow();
                        }

                        if (now < sanction.UpdatedAt || now < decision.UpdatedAt || !sanction.Reconcile(sanctionRevision, now))
                        {
                            return ModerationOperationResult.Conflict();
                        }
                    }
                    else
                    {
                        sanction.Revoke(sanctionRevision, now);
                    }

                    break;
                default:
                    return ModerationOperationResult.Unavailable();
            }

            var effect = effects.Plan(operation, decision, sanction, supersededSanction);
            if (effect.Id != operation.EffectId || effect.OperationId != operation.OperationId
                || effect.DecisionId != decision.Id || effect.DecisionRevision != decision.Revision
                || effect.SanctionId != sanction?.Id || effect.SanctionRevision != sanction?.Revision
                || effect.SupersededSanctionId != supersededSanction?.Id
                || effect.SupersededSanctionRevision != supersededSanction?.Revision
                || effect.Subject != decision.Subject || effect.Scope != decision.Scope || effect.Kind != operation.Kind)
            {
                return ModerationOperationResult.Unavailable();
            }

            await session.SaveAsync(decision, sanction, supersededSanction, grant, effect, token);
            return ModerationOperationResult.Applied(
                decision.Id, sanction?.Id,
                operation.Kind is ModerationOperationKind.Revoke or ModerationOperationKind.Reconcile && sanction is not null
                    ? sanction.Revision : decision.Revision);
        }, cancellationToken);
    }
}
