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
                || (operation.Kind != ModerationOperationKind.Revoke && decision.Revision != operation.ExpectedRevision))
            {
                return ModerationOperationResult.Conflict();
            }

            ModerationSanction? sanction = null;
            switch (operation.Kind)
            {
                case ModerationOperationKind.RecordResponse:
                    if (decision.State != ModerationDecisionState.Proposed)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    decision.RecordResponse(operation.ExpectedRevision, now);
                    break;
                case ModerationOperationKind.AcceptFinding:
                    if (decision.State != ModerationDecisionState.AwaitingFinding)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    decision.AcceptFinding(operation.ReferenceId, operation.ExpectedRevision, now);
                    break;
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
                    if (decision.State != ModerationDecisionState.Approved || (decision.EndsAt is not null && now >= decision.EndsAt))
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    if (await authority.QualifyActivationAsync(session, decision, grant, token) != ModerationAuthorityState.Known)
                    {
                        return ModerationOperationResult.Unavailable();
                    }

                    now = clock.GetUtcNow();
                    if (decision.EndsAt is not null && now >= decision.EndsAt)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    sanction = decision.Activate(operation.ReferenceId, operation.ExpectedRevision, now);
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

                    if (sanction.Revision != operation.ExpectedRevision || now < sanction.UpdatedAt
                        || sanction.State is ModerationSanctionState.Revoked or ModerationSanctionState.Superseded)
                    {
                        return ModerationOperationResult.Conflict();
                    }

                    sanction.Revoke(operation.ExpectedRevision, now);
                    break;
                default:
                    return ModerationOperationResult.Unavailable();
            }

            var effect = effects.Plan(operation, decision, sanction);
            if (effect.Id != operation.EffectId || effect.OperationId != operation.OperationId
                || effect.DecisionId != decision.Id || effect.DecisionRevision != decision.Revision
                || effect.SanctionId != sanction?.Id || effect.SanctionRevision != sanction?.Revision
                || effect.Subject != decision.Subject || effect.Scope != decision.Scope || effect.Kind != operation.Kind)
            {
                return ModerationOperationResult.Unavailable();
            }

            await session.SaveAsync(decision, sanction, grant, effect, token);
            return ModerationOperationResult.Applied(
                decision.Id, sanction?.Id,
                operation.Kind == ModerationOperationKind.Revoke && sanction is not null ? sanction.Revision : decision.Revision);
        }, cancellationToken);
    }
}
