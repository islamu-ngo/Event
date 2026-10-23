using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Scoped lifecycle authority. The store owns the serializable transaction and its completion;
/// this participant owns no transaction, receipt replay, external effect or generic moderation call.
/// </summary>
public sealed partial class ModerationAuthorityParticipant(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    IModerationMachineSubjectBindingRepository machineBindings, IExternalApiKeyRepository apiKeys,
    IModerationPolicyPublicationRepository publications, ModerationCapabilityRegistry inventory,
    ModerationNativeActivationQualification activation, TimeProvider clock,
    IEnumerable<IModerationConsumerRuntimeSource>? consumerSources = null,
    IModerationMeasurementReceiptVerifier? measurementReceipts = null,
    IModerationCaseAttributionOwner? caseAttribution = null,
    IModerationPreFindingResponseOwner? preFindingResponses = null)
    : IModerationAuthorityParticipant
{
    private Acquisition? _acquisition;

    public async Task<ModerationAuthorityResolution> AcquireAsync(
        IModerationSession session, ModerationOperation operation, CancellationToken cancellationToken)
    {
        _acquisition = null;
        _findingProvenance = null;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !context.Database.IsRelational()
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenantId)
            return ModerationAuthorityResolution.Unavailable();
        if (!await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(tenantId, cancellationToken))
            return ModerationAuthorityResolution.Unavailable();

        if (operation.Kind is ModerationOperationKind.RecordResponse
            or ModerationOperationKind.AcceptFinding or ModerationOperationKind.Approve
            or ModerationOperationKind.Activate or ModerationOperationKind.Supersede or ModerationOperationKind.Reconcile)
        {
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (host is null) return ModerationAuthorityResolution.Unavailable();
            context.ModerationTrustDomainId = host.TrustDomainId;
            var caseId = await context.ModerationDecisions.AsNoTracking()
                .Where(row => row.Id == operation.DecisionId).Select(row => (Guid?)row.CaseId)
                .SingleOrDefaultAsync(cancellationToken);
            if (caseId is { } responseCase && await context.ModerationCases.AsNoTracking().AnyAsync(
                row => row.Id == responseCase && row.AttributionSourceId != null, cancellationToken)
                && (preFindingResponses is null
                    || await preFindingResponses.DiscoverResponseHumansAsync(responseCase, cancellationToken) is null))
                return ModerationAuthorityResolution.Unavailable();
        }

        // The native helper locks canonical users/external identities, actual roles/memberships,
        // host/service, tenant/status and the qualification epoch, on this acquisition connection.
        var source = machine.IsMachineCaller
            ? await new ModerationMaintenanceAuthority(context, machine, machineBindings, apiKeys, clock)
                .AcquireAsync(operation, cancellationToken)
            : await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireAsync(operation, cancellationToken);
        if (source is null)
            return ModerationAuthorityResolution.Denied();
        var retainedCorrection = operation.Kind is ModerationOperationKind.Revoke
            or ModerationOperationKind.Reject or ModerationOperationKind.Withdraw;
        if (!retainedCorrection && !await SubjectExistsAsync(operation.Subject, source, cancellationToken))
            return ModerationAuthorityResolution.Denied();

        // Discover coordinates only. The exact case/decision is reread under its row fence below.
        var discovered = operation.Kind is ModerationOperationKind.Propose or ModerationOperationKind.Evaluate
            ? null : await context.ModerationDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == operation.DecisionId, cancellationToken);
        var requestedRule = operation.Proposal?.Rule ?? discovered?.Rule;
        var slots = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == source.Binding.Id
                && (row.JurisdictionId == source.Instance.Id || row.JurisdictionId == source.Tenant.Id))
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var applicable = new List<ModerationPolicyCompiledDraft>();
        foreach (var slot in slots)
        {
            var jurisdiction = slot.JurisdictionId == source.Instance.Id ? source.Instance : source.Tenant;
            // The publication reader locks and rereads the real slot, including first adoption.
            // The discovery row and static compiler never supply permission or active conformance.
            var publication = await publications.AcquireSelectedAsync(transaction.TransactionId,
                source.Binding.Id, jurisdiction, slot.Id, false, cancellationToken);
            var policy = publication is null ? null : ModerationPolicyPublicationProjection.RestoreHistory(publication);
            if (policy is null) return ModerationAuthorityResolution.Unavailable();
            if (!jurisdiction.Contains(operation.Target)) continue;
            if (requestedRule is not null
                ? policy.Profiles.Any(profile => profile.Definition.Rule == requestedRule)
                : operation.Capability is { } capability && (EffectiveRestrictions.IsProtectedRemedy(capability)
                    || policy.Profiles.Any(profile => profile.Definition.SubjectKinds.Contains(operation.Subject.Kind)
                        && profile.Definition.ScopeKinds.Contains(operation.Target.Kind)
                        && profile.Reference.AcceptsScope(jurisdiction, operation.Target)
                        && profile.Definition.Effects.Any(effect => effect.Capability == capability))))
                applicable.Add(policy);
        }
        if (applicable.Count == 0) return ModerationAuthorityResolution.Unavailable();
        // Every real jurisdiction slot stays fenced; the grant names one actual publication.
        var selected = requestedRule is null
            ? applicable.OrderByDescending(row => (int)row.Publication.Jurisdiction.Kind).First()
            : applicable.SingleOrDefault(row => row.Profiles.Any(profile => profile.Definition.Rule == requestedRule));
        if (selected is null || !retainedCorrection
            && !await TargetExistsAsync(operation.Target, operation.Subject, source, cancellationToken))
            return ModerationAuthorityResolution.Denied();
        var rule = requestedRule is null ? null : selected.Profiles.Single(row => row.Definition.Rule == requestedRule);

        ModerationCase? moderationCase = null;
        ModerationDecision? decision = null;
        if (operation.Kind != ModerationOperationKind.Evaluate)
        {
            var caseId = operation.Proposal?.CaseId ?? discovered?.CaseId;
            if (caseId is null)
                return ModerationAuthorityResolution.Denied();
            // Case and decision anchors precede the operation-specific finding, reviewer,
            // evidence and contribution reads. Human authority is never acquired this late.
            moderationCase = await session.FindCaseAsync(
                caseId.Value, operation.Subject, operation.Target, cancellationToken);
            if (moderationCase is null || moderationCase.Subject != operation.Subject
                || moderationCase.Scope != operation.Target
                    && (moderationCase.AttributionSourceId is null
                        || operation.Target.Kind != ModerationScopeKind.Resource
                        || moderationCase.Scope != operation.Target.Parent))
                return ModerationAuthorityResolution.Denied();
            if (discovered is not null)
            {
                decision = await session.FindDecisionAsync(
                    operation.DecisionId, operation.Subject, operation.Target, cancellationToken);
                if (decision is null || decision.CaseId != moderationCase.Id
                    || decision.Subject != operation.Subject || decision.Scope != operation.Target
                    || decision.Rule != requestedRule || decision.Issuer.Origin != source.Instance
                    || !source.Instance.Contains(decision.IssuingScope))
                    return ModerationAuthorityResolution.Denied();
            }
        }

        var principal = SubjectReference.Create(
            machine.IsMachineCaller ? ModerationSubjectKind.Service : ModerationSubjectKind.Human,
            source.UserId, source.Instance);
        var scope = source.IsInstanceAdmin || principal.Kind == ModerationSubjectKind.Service
            ? source.Instance
            : source.Administrators.Contains(source.UserId) && source.Tenant.Contains(operation.Target)
                ? source.Tenant : operation.Target;
        if (!scope.Contains(operation.Target))
            return ModerationAuthorityResolution.Denied();
        var now = clock.GetUtcNow();
        var needsActivation = operation.Kind is ModerationOperationKind.Activate or ModerationOperationKind.Supersede
            || operation.Kind == ModerationOperationKind.Reconcile
                && decision is { State: ModerationDecisionState.Scheduled }
                && decision.StartsAt <= now && (decision.EndsAt is null || decision.EndsAt > now);
        ModerationQualificationPolicyObservation? procedural = null;
        if (moderationCase is not null && (operation.Kind is ModerationOperationKind.AcceptFinding
            or ModerationOperationKind.Approve or ModerationOperationKind.Activate or ModerationOperationKind.Supersede
            || needsActivation && decision?.FindingId is not null))
        {
            procedural = await activation.AcquireQualificationPolicyAsync(
                source.Binding.Id, moderationCase, now, cancellationToken);
            if (procedural is null) return ModerationAuthorityResolution.Unavailable();
        }
        var grant = new ModerationAuthorityGrant(principal, operation.Subject, scope, operation.Kind,
            selected.Publication.ProposedRevision, source.Epoch.Revision, ModerationAuthorityState.Known);
        var acquired = new Acquisition(session, operation, transaction, tenantId, source,
            selected, rule, moderationCase, decision, decision?.Revision, grant, machine.Current, procedural, null);
        try
        {
            if (moderationCase?.AttributionSourceId is not null)
                _findingProvenance = new(this, acquired, context);
            if (!await PermittedAsync(acquired, cancellationToken))
                return ModerationAuthorityResolution.Denied();
            if (needsActivation)
            {
                // A later draft is authoring progress, not withdrawal of the real active pointer.
                // Current activation is qualified before receipt replay as well as before mutation.
                var active = await publications.AcquireSelectedAsync(transaction.TransactionId, source.Binding.Id,
                    selected.Publication.Jurisdiction, selected.Publication.PolicyId, true, cancellationToken);
                var activePolicy = active is null ? null : ModerationPolicyPublicationProjection.Restore(active, inventory);
                var activeRule = activePolicy?.Profiles.SingleOrDefault(profile => profile.Definition.Rule == requestedRule);
                if (activePolicy is null || activeRule is null || decision is null
                    || !activePolicy.Proposed.SupportedProfiles.Contains(activeRule.Definition.Rule))
                    return ModerationAuthorityResolution.Unavailable();
                acquired = acquired with
                {
                    Policy = activePolicy,
                    Rule = activeRule,
                    Grant = grant with { PolicyRevision = activePolicy.Publication.ProposedRevision }
                };
                var validUntil = await QualifyCurrentActivationAsync(acquired, decision, now, cancellationToken);
                if (validUntil is null)
                    return ModerationAuthorityResolution.Unavailable();
                acquired = acquired with { ActivationValidUntil = validUntil };
            }
            if (!await CurrentAsync(acquired, cancellationToken))
                return ModerationAuthorityResolution.Unavailable();
            _acquisition = acquired;
            return ModerationAuthorityResolution.Known(acquired.Grant);
        }
        finally
        {
            if (_acquisition is null) _findingProvenance = null;
        }
    }

    internal async Task<ModerationSourceAuthority?> BorrowNoticeSourceAuthorityAsync(
        ExploreDbContext receivingContext, IModerationSession session, ModerationEffectIntent effect,
        ModerationAuthorityGrant grant, CancellationToken token)
    {
        var acquired = _acquisition;
        if (acquired is null || !ReferenceEquals(context, receivingContext)
            || !ReferenceEquals(acquired.Session, session) || !ReferenceEquals(acquired.Grant, grant)
            || !ModerationStore.OwnsCaptureSession(session, context, acquired.Operation, grant)
            || !context.ModerationEffectIntents.Local.Any(row => ReferenceEquals(row, effect))
            || effect.Id != acquired.Operation.EffectId || effect.OperationId != acquired.Operation.OperationId
            || effect.DecisionId != acquired.Operation.DecisionId || effect.CompletedAt is not null
            || effect.TrustDomainId != acquired.Operation.Target.TrustDomainId
            || effect.ScopeKind != (int)acquired.Operation.Target.Kind || effect.ScopeId != acquired.Operation.Target.Id
            || effect.SubjectKind != (int)grant.Subject.Kind || effect.SubjectId != grant.Subject.Id
            || effect.OperationKind != (int)acquired.Operation.Kind || grant.Purpose != acquired.Operation.Kind
            || effect.PolicyRevision != grant.PolicyRevision || effect.QualificationRevision != grant.QualificationRevision
            || !await CurrentAsync(acquired, token))
            return null;
        return acquired.Source;
    }

    public async Task<ModerationAuthorityResolution> ResolveAsync(
        IModerationSession session, ModerationOperation operation, CancellationToken cancellationToken)
    {
        var acquired = _acquisition;
        if (acquired is null || !ReferenceEquals(acquired.Session, session)
            || !ReferenceEquals(acquired.Operation, operation)
            || !await CurrentAsync(acquired, cancellationToken)
            || !await PermittedAsync(acquired, cancellationToken))
            return ModerationAuthorityResolution.Unavailable();
        // Requalify the operation's actual case, finding and reviewer permission at each
        // checkpoint, including after the final flush and before returning a stored receipt.
        // Keep the exact acquired grant; permission renewal never widens or replaces it.
        return ModerationAuthorityResolution.Known(acquired.Grant);
    }

    public async Task<ModerationAuthorityState> QualifyActivationAsync(
        IModerationSession session, ModerationDecision decision, ModerationAuthorityGrant grant,
        CancellationToken cancellationToken)
    {
        var acquired = _acquisition;
        if (acquired is null || !ReferenceEquals(acquired.Session, session)
            || !ReferenceEquals(acquired.Grant, grant) || acquired.Rule is not { } rule
            || acquired.Operation.Kind is not (ModerationOperationKind.Activate
                or ModerationOperationKind.Supersede or ModerationOperationKind.Reconcile)
            || decision.Id != acquired.Operation.DecisionId || decision.Subject != grant.Subject
            || decision.Scope != acquired.Operation.Target || decision.Rule != rule.Definition.Rule
            || decision.Revision != acquired.Operation.ExpectedRevision
            || !await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == acquired.TenantId && row.TenantStatus.IsActiveState, cancellationToken)
            || !await CurrentAsync(acquired, cancellationToken))
            return ModerationAuthorityState.Unavailable;

        var persisted = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == decision.Id, cancellationToken);
        if (persisted is null)
            return ModerationAuthorityState.Unavailable;
        persisted.ValidatePersistedState();
        if (persisted.Revision != decision.Revision || persisted.State != decision.State
            || persisted.CaseId != decision.CaseId || persisted.FindingId != decision.FindingId
            || persisted.Subject != decision.Subject || persisted.Scope != decision.Scope
            || persisted.Rule != decision.Rule || persisted.Issuer != decision.Issuer
            || persisted.CapabilityMask != decision.CapabilityMask || persisted.StartsAt != decision.StartsAt
            || persisted.EndsAt != decision.EndsAt || persisted.ReviewAt != decision.ReviewAt
            || persisted.RequiredApprovals != decision.RequiredApprovals
            || !persisted.Approvals.SequenceEqual(decision.Approvals))
            return ModerationAuthorityState.Unavailable;

        var now = clock.GetUtcNow();
        if (now < decision.UpdatedAt || decision.EndsAt <= now || decision.FindingId is not { } findingId
            || await ReadFindingAsync(acquired, findingId, now, cancellationToken) is null
            || !await ApproversQualifiedAsync(acquired, decision, null, now, cancellationToken))
            return ModerationAuthorityState.Unavailable;
        var validUntil = await QualifyCurrentActivationAsync(acquired, decision, now, cancellationToken);
        if (validUntil is null) return ModerationAuthorityState.Unavailable;
        _acquisition = acquired with
        {
            ActivationValidUntil = acquired.ActivationValidUntil < validUntil
                ? acquired.ActivationValidUntil : validUntil
        };
        return ModerationAuthorityState.Known;
    }

    private async Task<DateTimeOffset?> QualifyCurrentActivationAsync(
        Acquisition acquired, ModerationDecision decision, DateTimeOffset observedAt, CancellationToken token)
    {
        var selected = acquired.Policy.Publication;
        if (acquired.Rule is not { } profile || acquired.QualificationPolicy is null
            || selected.ProposedRevision != acquired.Grant.PolicyRevision
            || !acquired.Policy.Proposed.SupportedProfiles.Contains(profile.Definition.Rule))
            return null;
        using var evidence = BorrowConsumerEvidence(acquired);
        var publicationUntil = await publications.RequalifyActivationAsync(evidence, acquired.Transaction.TransactionId,
            acquired.Source.Binding.Id, selected.Jurisdiction, selected.PolicyId,
            selected.PublicationId, observedAt, token);
        if (publicationUntil is null) return null;
        var publication = await publications.AcquireSelectedAsync(acquired.Transaction.TransactionId,
            acquired.Source.Binding.Id, selected.Jurisdiction, selected.PolicyId, true, token);
        if (publication is null) return null;
        var configuration = await AcquireProfileConfigurationAsync(acquired, acquired.Transaction.TransactionId,
            publication, decision, acquired.Grant, observedAt, token);
        if (configuration is null) return null;
        var profileUntil = await activation.QualifyAsync(evidence, acquired.Source.Binding.Id, acquired.Policy,
            profile, decision, configuration, observedAt, token);
        if (profileUntil is null || !await FindingAndApprovalsAsync(acquired, decision, clock.GetUtcNow(), token)
            || !await CurrentAsync(acquired, token))
            return null;
        var validUntil = publicationUntil < profileUntil ? publicationUntil : profileUntil;
        return clock.GetUtcNow() < validUntil ? validUntil : null;
    }

    private async Task<bool> PermittedAsync(Acquisition acquired, CancellationToken token)
    {
        var operation = acquired.Operation;
        var source = acquired.Source;
        var principal = acquired.Grant.Principal;
        var decision = acquired.Decision;
        var now = clock.GetUtcNow();
        bool administrator = principal.Kind == ModerationSubjectKind.Human
            && source.Administrators.Contains(principal.Id) && operation.Subject != principal
            && (source.IsInstanceAdmin || operation.Subject.Kind is ModerationSubjectKind.Human or ModerationSubjectKind.Content);
        if (principal.Kind == ModerationSubjectKind.Service)
        {
            // Only the actual native helper-qualified installed maintenance principal may reconcile.
            // A machine owner's human permission or an arbitrary Service subject never enters here.
            return operation.Kind == ModerationOperationKind.Reconcile
                && principal.Id == source.Binding.MaintenanceServicePrincipalId
                && decision is not null && await ContributionMatchesAsync(acquired, token)
                && (acquired.ActivationValidUntil is null
                    || await FindingAndApprovalsAsync(acquired, decision, now, token))
                && (decision.State != ModerationDecisionState.Scheduled
                    || decision.StartsAt > now || decision.EndsAt <= now
                    || await FindingAndApprovalsAsync(acquired, decision, now, token));
        }
        switch (operation.Kind)
        {
            case ModerationOperationKind.Evaluate:
                // Evaluation is a private overlay query, not ordinary native action permission.
                // Administrator status does not permit evaluating another person's protected remedy.
                return principal == operation.Subject;
            case ModerationOperationKind.Propose:
                return administrator && acquired.Case is { State: ModerationCaseState.Open } allegation
                    && allegation.Revision == operation.ExpectedRevision && allegation.UpdatedAt <= now
                    && operation.Proposal is { } proposal && acquired.Rule is { } rule
                    && acquired.Policy.Proposed.SupportedProfiles.Contains(rule.Definition.Rule)
                    && ModerationNativeActivationQualification.Permits(acquired.Policy, rule,
                        operation.Subject, operation.Target, proposal.Capabilities,
                        proposal.StartsAt, proposal.EndsAt, proposal.ReviewAt, proposal.RequiredApprovals);
            case ModerationOperationKind.RecordResponse:
                return principal == operation.Subject && acquired.Case is { State: ModerationCaseState.Open } responseCase
                    && await HasResponseAsync(responseCase, principal.Id, now, token);
            case ModerationOperationKind.Withdraw:
                return decision is not null && principal == decision.Issuer;
            case ModerationOperationKind.AcceptFinding:
                var finding = await ReadFindingAsync(acquired, operation.ReferenceId, now, token);
                return finding is not null && finding.Adjudicator == principal;
            case ModerationOperationKind.Approve:
                return decision is { FindingId: { } acceptedFinding }
                    && await ReadFindingAsync(acquired, acceptedFinding, now, token) is not null
                    && await ApproversQualifiedAsync(acquired, decision, principal, now, token);
            case ModerationOperationKind.Activate:
                return administrator && decision is not null
                    && await FindingAndApprovalsAsync(acquired, decision, now, token);
            case ModerationOperationKind.Reject:
                return administrator && decision is not null;
            case ModerationOperationKind.Revoke:
                return administrator && decision is not null && await ContributionMatchesAsync(acquired, token);
            case ModerationOperationKind.Supersede:
                if (!administrator || decision is null || operation.SupersededScope is not { } oldScope
                    || !acquired.Grant.Scope.Contains(oldScope) || !oldScope.Contains(operation.Target))
                    return false;
                var old = await acquired.Session.FindSanctionAsync(
                    operation.SupersededSanctionId, operation.Subject, oldScope, token);
                return old is not null && old.Subject == operation.Subject && old.Scope == oldScope
                    && old.DecisionId != decision.Id
                    && await FindingAndApprovalsAsync(acquired, decision, now, token);
            // Reconciliation is an installed service responsibility, not an administrator shortcut.
            default:
                return false;
        }
    }

    private async Task<bool> FindingAndApprovalsAsync(
        Acquisition acquired, ModerationDecision decision, DateTimeOffset now, CancellationToken token) =>
        decision.FindingId is { } findingId && await ReadFindingAsync(acquired, findingId, now, token) is not null
        && await ApproversQualifiedAsync(acquired, decision, null, now, token);

    private async Task<bool> ContributionMatchesAsync(Acquisition acquired, CancellationToken token)
    {
        var operation = acquired.Operation;
        var sanction = await acquired.Session.FindSanctionAsync(
            operation.ReferenceId, operation.Subject, operation.Target, token);
        return sanction is not null && sanction.DecisionId == operation.DecisionId
            && sanction.Subject == operation.Subject && sanction.Scope == operation.Target;
    }

    private async Task<bool> SubjectExistsAsync(
        SubjectReference subject, ModerationSourceAuthority source, CancellationToken token)
    {
        if (subject.Kind == ModerationSubjectKind.Content)
        {
            if (subject.Origin != source.Tenant) return false;
            await RelationalEntityRowFence.AcquireAsync<Explore.Domain.Event>(
                context, source.Tenant.Id, row => row.Id, subject.Id, token);
            return await context.Events.AsNoTracking()
                .AnyAsync(row => row.Id == subject.Id && row.TenantId == source.Tenant.Id && !row.IsDeleted, token);
        }
        if (subject.Origin != source.Instance) return false;
        switch (subject.Kind)
        {
            case ModerationSubjectKind.Human:
                return source.FencedHumans.Contains(subject.Id)
                    && await context.Users.AsNoTracking().AnyAsync(row => row.Id == subject.Id && !row.IsDeleted, token);
            case ModerationSubjectKind.Service:
                await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, subject.Id, token);
                return await context.ServicePrincipals.AsNoTracking()
                    .AnyAsync(row => row.Id == subject.Id && !row.IsDeleted, token);
            case ModerationSubjectKind.Organization:
                await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(context, subject.Id, token);
                return await context.Organizations.AsNoTracking()
                    .AnyAsync(row => row.Id == subject.Id && !row.IsDeleted, token);
            default:
                return false;
        }
    }

    private async Task<bool> TargetExistsAsync(
        AuthorityScope target, SubjectReference subject, ModerationSourceAuthority source, CancellationToken token)
    {
        if (target == source.Instance || target == source.Tenant) return true;
        // Resource means an actual EventResource, not a guessed Event or Group identifier.
        // Community targets have no native lifecycle source owner yet and cannot be invented.
        if (target.Kind != ModerationScopeKind.Resource || target.Parent != source.Tenant) return false;
        var discovered = await context.EventResources.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == target.Id && row.TenantId == source.Tenant.Id, token);
        if (discovered is null || subject.Kind == ModerationSubjectKind.Content && discovered.EventId != subject.Id)
            return false;
        await RelationalEntityRowFence.AcquireAsync<Explore.Domain.Event>(
            context, source.Tenant.Id, row => row.Id, discovered.EventId, token);
        await RelationalEntityRowFence.AcquireAsync<EventResource>(
            context, source.Tenant.Id, row => row.Id, target.Id, token);
        return await context.EventResources.AsNoTracking().AnyAsync(
            row => row.Id == target.Id && row.EventId == discovered.EventId
                && row.TenantId == source.Tenant.Id && !row.IsDeleted, token)
            && await context.Events.AsNoTracking().AnyAsync(
                row => row.Id == discovered.EventId && row.TenantId == source.Tenant.Id && !row.IsDeleted, token);
    }

    private async Task<bool> HasResponseAsync(
        ModerationCase moderationCase, Guid humanId, DateTimeOffset now, CancellationToken token)
    {
        if (moderationCase.AttributionSourceId is not null)
        {
            if (preFindingResponses is null || _findingProvenance is null) return false;
            var responseIds = await context.Set<ModerationPreFindingNotice>().AsNoTracking()
                .Where(row => row.CaseId == moderationCase.Id && row.RecipientHumanId == humanId && row.ResponseId != null)
                .OrderBy(row => row.Id).Select(row => row.ResponseId!.Value).ToListAsync(token);
            foreach (var responseId in responseIds)
            {
                var response = await preFindingResponses.AcquireCurrentResponseAsync(
                    _findingProvenance, moderationCase.Id, responseId, token);
                if (response is not null && response.ReferenceId == responseId
                    && response.CaseId == moderationCase.Id && response.CaseRevision == moderationCase.Revision
                    && response.RecordedAt >= moderationCase.CreatedAt && response.RecordedAt <= now
                    && (context.ChangeTracker.HasChanges() || await preFindingResponses.RecheckResponseAsync(token)))
                    return true;
            }
            return false;
        }
        var ids = await context.ModerationQualificationEvidence.AsNoTracking()
            .Where(row => row.CaseId == moderationCase.Id
                && row.Purpose == ModerationQualificationEvidencePurpose.Response
                && row.SubjectHumanId == humanId && row.AssertedByHumanId == humanId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var id in ids)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, id, token);
        var evidence = await context.ModerationQualificationEvidence.AsNoTracking()
            .Where(row => ids.Contains(row.Id)).ToListAsync(token);
        foreach (var row in evidence) row.ValidatePersistedState();
        return evidence.Any(row => row.CaseId == moderationCase.Id
            && row.Purpose == ModerationQualificationEvidencePurpose.Response
            && row.SubjectHumanId == humanId && row.AssertedByHumanId == humanId
            && row.RecordedAt >= moderationCase.CreatedAt && row.RecordedAt <= now);
    }

    private async Task<ModerationFinding?> ReadFindingAsync(
        Acquisition acquired, Guid findingId, DateTimeOffset now, CancellationToken token)
    {
        if (acquired.Case is not { } expectedCase || acquired.Rule is not { } rule
            || acquired.QualificationPolicy is not { } procedural || now >= procedural.ValidUntil)
            return null;
        var moderationCase = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == expectedCase.Id, token);
        if (moderationCase is null) return null;
        moderationCase.ValidatePersistedState();
        if (moderationCase.State != ModerationCaseState.Open || moderationCase.Subject != acquired.Grant.Subject
            || moderationCase.Scope != acquired.Operation.Target
                && (moderationCase.AttributionSourceId is null
                    || acquired.Operation.Target.Kind != ModerationScopeKind.Resource
                    || moderationCase.Scope != acquired.Operation.Target.Parent)
            || moderationCase.UpdatedAt > now)
            return null;
        Explore.Domain.ModerationCaseAttributionSource? attribution = null;
        if (moderationCase.AttributionSourceId is not null)
        {
            if (caseAttribution is null || _findingProvenance is null
                || (attribution = await caseAttribution.AcquireCurrentAttributionAsync(
                    _findingProvenance, moderationCase.Id, token)) is null
                || !context.ChangeTracker.HasChanges() && !await caseAttribution.RecheckAttributionAsync(token))
                return null;
            if (acquired.Operation.Target.Kind == ModerationScopeKind.Resource
                && moderationCase.SubjectKind == ModerationSubjectKind.Content
                && !await context.ModerationCaseTargetSources.AsNoTracking().AnyAsync(
                    row => row.Id == attribution.EvidenceSourceId
                        && row.NativeKind == ModerationCaseTargetKind.Resource
                        && row.NativeTargetId == acquired.Operation.Target.Id, token))
                return null;
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, findingId, token);
        var finding = await context.ModerationFindings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == findingId, token);
        if (finding is null) return null;
        finding.ValidatePersistedState();
        if (finding.State != ModerationFindingState.Final || finding.CaseId != moderationCase.Id
            || finding.CaseRevision != moderationCase.Revision || finding.CaseOccurrenceId != moderationCase.OccurrenceId
            || finding.CaseCreatedAt != moderationCase.CreatedAt || finding.Subject != moderationCase.Subject
            || finding.AttributionSourceId != attribution?.Id
            || finding.AttributionGeneration != (attribution?.Generation ?? 0)
            || finding.Scope != acquired.Operation.Target || finding.Rule != rule.Definition.Rule
            || finding.Reason != rule.Definition.Reason || finding.FinalizedAt > now
            || finding.Adjudicator.Origin != acquired.Source.Instance
            || acquired.Decision?.Issuer == finding.Adjudicator)
            return null;
        var adjudicator = await ReadReviewerAsync(acquired, finding.AdjudicatorId, now, token);
        if (adjudicator is null || adjudicator.ControllerHumanId == acquired.Decision?.IssuerId)
            return null;

        if (moderationCase.AttributionSourceId is not null
            && finding.EvidencePurpose == ModerationFindingEvidencePurpose.Response)
        {
            if (preFindingResponses is null || _findingProvenance is null) return null;
            var response = await preFindingResponses.AcquireCurrentResponseAsync(
                _findingProvenance, moderationCase.Id, finding.EvidenceReferenceId, token);
            if (response is null || response.ReferenceId != finding.EvidenceReferenceId
                || response.CaseId != moderationCase.Id || response.CaseRevision != moderationCase.Revision
                || response.RecordedAt != finding.EvidenceRecordedAt || response.RecordedAt > finding.FinalizedAt
                || !context.ChangeTracker.HasChanges() && !await preFindingResponses.RecheckResponseAsync(token))
                return null;
            return finding;
        }

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(
            context, finding.EvidenceReferenceId, token);
        var evidence = await context.ModerationQualificationEvidence.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == finding.EvidenceReferenceId, token);
        if (evidence is null) return null;
        evidence.ValidatePersistedState();
        if (evidence.CaseId != moderationCase.Id || evidence.RecordedAt != finding.EvidenceRecordedAt
            || evidence.TrustDomainId != acquired.Source.Binding.TrustDomainId
            || evidence.TenantId != acquired.TenantId || evidence.RecordedAt > finding.FinalizedAt)
            return null;
        if (finding.EvidencePurpose == ModerationFindingEvidencePurpose.Response)
            return evidence.Purpose == ModerationQualificationEvidencePurpose.Response
                && moderationCase.SubjectKind == ModerationSubjectKind.Human
                && evidence.SubjectHumanId == moderationCase.SubjectId
                && evidence.AssertedByHumanId == moderationCase.SubjectId ? finding : null;

        if (finding.EvidencePurpose != ModerationFindingEvidencePurpose.QualifiedUrgency
            || evidence.Purpose != ModerationQualificationEvidencePurpose.Urgency
            || evidence.ValidUntil <= now
            || evidence.ValidUntil - evidence.RecordedAt > procedural.Current.MaximumUrgencyLifetime
            || finding.UrgencyQualificationId is not { } receiptId
            || !acquired.Source.FencedHumans.Contains(evidence.AssertedByHumanId)
            || !acquired.Source.Administrators.Contains(evidence.AssertedByHumanId))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId, token);
        if (receipt is null) return null;
        receipt.ValidatePersistedState();
        return receipt.SourceKind == ModerationQualificationSourceKind.Evidence
            && receipt.SourceId == evidence.Id && receipt.ResultRevision == evidence.Revision
            && receipt.HostBindingId == acquired.Source.Binding.Id
            && receipt.TrustDomainId == acquired.Source.Binding.TrustDomainId && receipt.TenantId == acquired.TenantId
            && receipt.PrincipalId == evidence.AssertedByHumanId
            && receipt.CompletedAt >= evidence.RecordedAt && receipt.CompletedAt <= finding.FinalizedAt
                ? finding : null;
    }

    private async Task<bool> ApproversQualifiedAsync(
        Acquisition acquired, ModerationDecision decision, SubjectReference? candidate,
        DateTimeOffset now, CancellationToken token)
    {
        if (candidate is null && decision.Approvals.Count != decision.RequiredApprovals)
            return false;
        if (candidate is not null && (candidate.Kind != ModerationSubjectKind.Human
            || candidate.Origin != acquired.Source.Instance || candidate == decision.Issuer || candidate == decision.Subject))
            return false;
        // An existing actor can requalify for exact receipt replay. Transition sequencing, including
        // duplicate new approvals, remains the service's job after the store's receipt lookup.
        var humans = decision.Approvals.Concat(candidate is null ? [] : new[] { candidate }).Distinct()
            .OrderBy(row => row.Id).ToArray();
        var qualified = new List<ModerationReviewerAssignment>();
        foreach (var human in humans)
        {
            if (human.Kind != ModerationSubjectKind.Human || human.Origin != acquired.Source.Instance
                || human == decision.Issuer || human == decision.Subject)
                return false;
            var assignment = await ReadReviewerAsync(acquired, human.Id, now, token);
            if (assignment is null || assignment.ControllerHumanId == decision.IssuerId
                || assignment.ControllerHumanId == decision.SubjectId
                || qualified.Any(other => !assignment.IsIndependentOf(other, now)))
                return false;
            qualified.Add(assignment);
        }
        return qualified.Count > 0;
    }

    private async Task<ModerationReviewerAssignment?> ReadReviewerAsync(
        Acquisition acquired, Guid humanId, DateTimeOffset now, CancellationToken token)
    {
        if (acquired.Case is not { } moderationCase
            || acquired.QualificationPolicy is not { } procedural || now >= procedural.ValidUntil)
            return null;
        var ids = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == moderationCase.Id && row.ReviewerHumanId == humanId && row.WithdrawnAt == null)
            .Select(row => row.Id).ToListAsync(token);
        if (ids.Count != 1) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationReviewerAssignment>(context, ids[0], token);
        var reviewer = await context.ModerationReviewerAssignments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == ids[0], token);
        if (reviewer is null) return null;
        reviewer.ValidatePersistedState();
        if (!reviewer.IsQualifiedAt(now) || reviewer.CaseId != moderationCase.Id
            || reviewer.TrustDomainId != acquired.Source.Binding.TrustDomainId
            || reviewer.CaseTenantId != moderationCase.ScopeTenantId || reviewer.ReviewerHumanId != humanId
            || reviewer.ControllerHumanId is not { } controller
            || reviewer.QualifiedByHumanId is not { } qualifier || reviewer.AssessedByHumanId is not { } assessor
            || reviewer.IdentityEvidenceId is not { } identityId || reviewer.ConflictEvidenceId is not { } conflictId
            || !acquired.Source.Administrators.Contains(qualifier) || !acquired.Source.Administrators.Contains(assessor)
            || qualifier == humanId || qualifier == controller || qualifier == reviewer.SubjectHumanId
            || assessor == humanId || assessor == controller || assessor == reviewer.SubjectHumanId)
            return null;
        var humans = new[] { humanId, controller, qualifier, assessor }.Distinct().ToArray();
        if (humans.Any(id => !acquired.Source.FencedHumans.Contains(id))
            || await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) != humans.Length)
            return null;
        foreach (var id in new[] { identityId, conflictId }.Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, id, token);
        var identity = await context.ModerationQualificationEvidence.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == identityId, token);
        var conflict = await context.ModerationQualificationEvidence.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == conflictId, token);
        if (identity is null || conflict is null) return null;
        identity.ValidatePersistedState();
        conflict.ValidatePersistedState();
        if (identity.Purpose != ModerationQualificationEvidencePurpose.Controller
            || conflict.Purpose != ModerationQualificationEvidencePurpose.Conflict
            || identity.CaseId != reviewer.CaseId || conflict.CaseId != reviewer.CaseId
            || identity.TrustDomainId != acquired.Source.Binding.TrustDomainId
            || conflict.TrustDomainId != acquired.Source.Binding.TrustDomainId
            || identity.TenantId != acquired.TenantId || conflict.TenantId != acquired.TenantId
            || identity.ReviewerHumanId != humanId || conflict.ReviewerHumanId != humanId
            || identity.ControllerHumanId != controller || conflict.ControllerHumanId != controller
            || identity.AssertedByHumanId != qualifier || conflict.AssertedByHumanId != assessor
            || conflict.ConflictState != (int)ModerationReviewerConflictState.Independent
            || identity.ValidUntil != reviewer.QualifiedUntil || conflict.ValidUntil != reviewer.QualifiedUntil
            || identity.ValidUntil <= now || conflict.ValidUntil <= now
            || identity.ValidUntil - identity.RecordedAt > procedural.Current.MaximumReviewerLifetime
            || conflict.ValidUntil - conflict.RecordedAt > procedural.Current.MaximumReviewerLifetime
            || identity.RecordedAt > conflict.RecordedAt || conflict.RecordedAt > now)
            return null;
        return reviewer;
    }

    private async Task<bool> CurrentAsync(Acquisition acquired, CancellationToken token)
    {
        if (!ReferenceEquals(context.Database.CurrentTransaction, acquired.Transaction)
            || acquired.Transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != acquired.TenantId
            || context.ModerationTrustDomainId != acquired.Source.Binding.TrustDomainId
            || acquired.ActivationValidUntil is { } activationUntil && clock.GetUtcNow() >= activationUntil
            || !await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(acquired.TenantId, token))
            return false;
        if (acquired.Machine is { } principal)
        {
            if (machine.Current != principal) return false;
            var binding = await machineBindings.ReadCurrentAsync(
                principal.KeyId, Explore.Domain.Enums.ModerationMachineCredentialSource.Persisted, token);
            if (binding is null || binding.HostBindingId != acquired.Source.Binding.Id
                || binding.ServicePrincipalId != acquired.Grant.Principal.Id
                || binding.AuthorityTenantId != principal.TenantId
                || binding.OwnerId != principal.OwnerId || binding.OwnerType != principal.OwnerType)
                return false;
        }
        else if (machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) != acquired.Grant.Principal.Id)
            return false;
        if (acquired.QualificationPolicy is { } procedural)
        {
            var now = clock.GetUtcNow();
            if (now < procedural.Current.ObservedAt || now >= procedural.ValidUntil || acquired.Case is null)
                return false;
            var repeated = await activation.AcquireQualificationPolicyAsync(acquired.Source.Binding.Id,
                acquired.Case, procedural.Current.ObservedAt, token);
            if (repeated != procedural) return false;
        }
        return await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(
            row => row.Id == acquired.Source.Epoch.Id && row.HostBindingId == acquired.Source.Binding.Id
                && row.TenantId == acquired.TenantId && row.Revision == acquired.Grant.QualificationRevision, token);
    }

    internal sealed record Acquisition(
        IModerationSession Session, ModerationOperation Operation, IDbContextTransaction Transaction,
        Guid TenantId, ModerationSourceAuthority Source, ModerationPolicyCompiledDraft Policy,
        ModerationPolicyCompiledProfile? Rule,
        ModerationCase? Case, ModerationDecision? Decision, long? DecisionRevision, ModerationAuthorityGrant Grant,
        ApiKeyPrincipalContext? Machine, ModerationQualificationPolicyObservation? QualificationPolicy,
        DateTimeOffset? ActivationValidUntil);
}
