using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Joins independently current native evidence to the finite inventory inside the lifecycle transaction.
/// A successful comparison is necessary but never replaces case, reviewer or operation authority.
/// </summary>
public sealed class ModerationNativeActivationQualification(
    ExploreDbContext context, ModerationCapabilityRegistry inventory, IModerationActivationSourceOwner? sources,
    IModerationQualificationPolicySourceOwner? qualificationPolicies, TimeProvider clock)
{
    public async Task<DateTimeOffset?> QualifyAsync(
        IModerationConsumerEvidenceSession evidenceSession,
        Guid hostBindingId, ModerationPolicyCompiledDraft policy, ModerationPolicyCompiledProfile selected,
        ModerationDecision decision, ModerationActivationProfileObservation configuration,
        DateTimeOffset observedAt, CancellationToken token)
    {
        var transaction = context.Database.CurrentTransaction;
        var publication = policy.Publication;
        var definition = selected.Definition;
        if (sources is null || transaction is null || evidenceSession.TransactionId != transaction.TransactionId
            || !Current(transaction)
            || context.ModerationTrustDomainId != publication.Jurisdiction.TrustDomainId
            || publication.HostBindingId != hostBindingId
            || !policy.Profiles.Contains(selected)
            || !policy.Proposed.SupportedProfiles.Contains(definition.Rule)
            || !Permits(policy, selected, decision.Subject, decision.Scope, decision.Capabilities,
                decision.StartsAt, decision.EndsAt, decision.ReviewAt, decision.RequiredApprovals))
            return null;

        var owner = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == decision.CaseId, token);
        if (owner is null || owner.Subject != decision.Subject
            || owner.Scope != decision.Scope
                && (owner.AttributionSourceId is null || decision.Scope.Kind != ModerationScopeKind.Resource
                    || owner.Scope != decision.Scope.Parent))
            return null;
        owner.ValidatePersistedState();
        var procedural = await ReadQualificationPolicyAsync(
            hostBindingId, decision.CaseId, owner.Scope, observedAt, token);
        if (procedural is null || evidenceSession.TransactionId != transaction.TransactionId || !Current(transaction))
            return null;
        var profile = await sources.AcquireProfileAsync(
            evidenceSession, hostBindingId, transaction.TransactionId, policy, selected, decision, observedAt, token);
        if (profile is null || evidenceSession.TransactionId != transaction.TransactionId || !Current(transaction)
            || profile.TransactionId != transaction.TransactionId || profile.HostBindingId != hostBindingId
            || profile.PolicyId != publication.PolicyId || profile.PolicyRevision != publication.ProposedRevision
            || profile.PublicationId != publication.PublicationId || profile.Jurisdiction != publication.Jurisdiction
            || profile.ProfileId != definition.Id || profile.DecisionId != decision.Id
            || profile.DecisionRevision != decision.Revision
            || profile.Rule != definition.Rule || profile.ObservedAt != observedAt
            || !Instant(profile.ValidUntil) || profile.ValidUntil <= observedAt
            || !Version7(profile.SourceId) || profile.SourceRevision < 1
            || !Generation(profile.WriterGeneration) || !policy.Proposed.Modes.Contains(profile.Mode)
            || profile.RequiredPrerequisites.IsDefaultOrEmpty
            || profile.RequiredPrerequisites.Any(item => !Enum.IsDefined(item))
            || profile.RequiredPrerequisites.Distinct().Count() != profile.RequiredPrerequisites.Length
            || !profile.RequiredPrerequisites.Contains(ModerationActivationPrerequisite.ConstitutionAndReviewers)
            || !profile.RequiredPrerequisites.Contains(ModerationActivationPrerequisite.MeasuredOperations)
            || profile.Capabilities.IsDefaultOrEmpty
            || profile.Capabilities.Any(capability => !Enum.IsDefined(capability))
            || profile.Capabilities.Distinct().Count() != profile.Capabilities.Length
            || !profile.Capabilities.Order().SequenceEqual(decision.Capabilities.Order())
            || profile.Contribution is not { } contribution || contribution.Id != decision.Id
            || contribution.Rule != definition.Rule || contribution.Scope != decision.Scope
            || contribution.SubjectKind != decision.Subject.Kind || contribution.SubjectId != decision.Subject.Id
            || contribution.StartsAt != decision.StartsAt)
            return null;

        // The independently accepted configuration is read by the lifecycle participant on
        // its actual acquisition, not manufactured from the deployment owner's response.
        if (configuration is null || configuration.TransactionId != profile.TransactionId
            || configuration.HostBindingId != profile.HostBindingId || configuration.PolicyId != profile.PolicyId
            || configuration.PolicyRevision != profile.PolicyRevision || configuration.PublicationId != profile.PublicationId
            || configuration.Jurisdiction != profile.Jurisdiction || configuration.ProfileId != profile.ProfileId
            || configuration.DecisionId != profile.DecisionId || configuration.DecisionRevision != profile.DecisionRevision
            || configuration.Rule != profile.Rule || configuration.Mode != profile.Mode
            || configuration.SourceId != profile.SourceId || configuration.SourceRevision != profile.SourceRevision
            || configuration.WriterGeneration != profile.WriterGeneration || configuration.ObservedAt != observedAt
            || !Instant(configuration.ValidUntil) || configuration.ValidUntil <= observedAt
            || configuration.RequiredPrerequisites.IsDefaultOrEmpty
            || !configuration.RequiredPrerequisites.Order().SequenceEqual(profile.RequiredPrerequisites.Order())
            || configuration.Capabilities.IsDefaultOrEmpty
            || !configuration.Capabilities.Order().SequenceEqual(profile.Capabilities.Order())
            || configuration.Contribution is null
            || ModerationNoticeSnapshotFactory.SelectionDigest(configuration.Contribution)
                != ModerationNoticeSnapshotFactory.SelectionDigest(contribution))
            return null;

        var prerequisites = policy.Prerequisites.SingleOrDefault(row =>
            row.Rule == definition.Rule && row.Mode == profile.Mode);
        if (prerequisites is null
            || !profile.RequiredPrerequisites.Order().SequenceEqual(prerequisites.ActivationGates.Order()))
            return null;

        var composed = ModerationPolicyProposedStateCompiler.Compose(policy, [contribution], observedAt);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Effects.IsEmpty
            || composed.Consequences.IsEmpty
            || composed.Effects.Any(row => row.Interval.StartsAt != decision.StartsAt
                || row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt)
            || composed.Consequences.Any(row => row.Interval.StartsAt != decision.StartsAt
                || row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt))
            return null;

        var validUntil = procedural.ValidUntil < profile.ValidUntil ? procedural.ValidUntil : profile.ValidUntil;
        if (configuration.ValidUntil < validUntil) validUntil = configuration.ValidUntil;
        foreach (var capability in decision.Capabilities.Order())
        {
            var binding = new ModerationActivationBinding(publication.PolicyId, publication.ProposedRevision,
                publication.PublicationId, publication.Jurisdiction, definition.Rule, capability, profile.Mode);
            var registrations = new List<ModerationCapabilityRegistration>();
            var currentSources = new List<ModerationCapabilitySourceObservation>();
            // Exact finite set, not discovered plugins or an intersection with available consumers.
            foreach (var id in inventory.RequiredConsumers(capability, profile.Mode))
            {
                var source = await sources.AcquireConsumerAsync(
                    evidenceSession, transaction.TransactionId, binding, id, observedAt, token);
                if (evidenceSession.TransactionId != transaction.TransactionId || !Current(transaction) || source is null)
                    return null;
                var registration = await sources.ReadConformanceAsync(
                    evidenceSession, transaction.TransactionId, binding, id, observedAt, token);
                if (evidenceSession.TransactionId != transaction.TransactionId
                    || !Current(transaction) || source is null || registration is null)
                    return null;
                currentSources.Add(source);
                registrations.Add(registration);
            }
            if (!inventory.Match(binding, registrations, currentSources, observedAt).Matches)
                return null;
            foreach (var source in currentSources)
                if (source.ValidUntil < validUntil) validUntil = source.ValidUntil;
            foreach (var registration in registrations)
                if (registration.ValidUntil < validUntil) validUntil = registration.ValidUntil;

            var approvalIds = new HashSet<Guid>();
            var sourceIds = new HashSet<Guid>();
            foreach (var prerequisite in profile.RequiredPrerequisites.Order())
            {
                var evidence = await sources.AcquirePrerequisiteAsync(
                    evidenceSession, transaction.TransactionId, binding, prerequisite, observedAt, token);
                if (evidenceSession.TransactionId != transaction.TransactionId || !Current(transaction) || evidence is null
                    || evidence.TransactionId != transaction.TransactionId || evidence.Binding != binding
                    || evidence.Prerequisite != prerequisite || evidence.ObservedAt != observedAt
                    || !Version7(evidence.ApprovalId) || !Version7(evidence.SourceId)
                    || evidence.ApprovalId == evidence.SourceId || evidence.ApprovalRevision < 1
                    || evidence.ApprovedSourceRevision < 1
                    || evidence.ApprovedSourceRevision != evidence.CurrentSourceRevision
                    || !Generation(evidence.ApprovedWriterGeneration)
                    || evidence.ApprovedWriterGeneration != evidence.CurrentWriterGeneration
                    || evidence.AdoptedAt > observedAt || evidence.ValidUntil <= observedAt
                    || !approvalIds.Add(evidence.ApprovalId) || !sourceIds.Add(evidence.SourceId))
                    return null;
                if (evidence.ValidUntil < validUntil) validUntil = evidence.ValidUntil;
            }
        }
        var completedAt = clock.GetUtcNow();
        return evidenceSession.TransactionId == transaction.TransactionId
            && Current(transaction) && completedAt >= observedAt && completedAt < validUntil ? validUntil : null;
    }

    public Task<ModerationQualificationPolicyObservation?> AcquireQualificationPolicyAsync(
        Guid hostBindingId, ModerationCase allegation, DateTimeOffset observedAt, CancellationToken token) =>
        ReadQualificationPolicyAsync(hostBindingId, allegation.Id, allegation.Scope, observedAt, token);

    private async Task<ModerationQualificationPolicyObservation?> ReadQualificationPolicyAsync(
        Guid hostBindingId, Guid caseId, AuthorityScope scope, DateTimeOffset observedAt, CancellationToken token)
    {
        var transaction = context.Database.CurrentTransaction;
        if (qualificationPolicies is null || transaction is null || !Current(transaction)
            || context.ModerationTrustDomainId != scope.TrustDomainId
            || !Version7(hostBindingId) || !Version7(caseId) || !Instant(observedAt))
            return null;
        var tenantScope = scope;
        while (tenantScope.Kind > ModerationScopeKind.Tenant) tenantScope = tenantScope.Parent!;
        if (tenantScope.Kind == ModerationScopeKind.Tenant && tenantScope.Id != context.TenantFilterTenantId)
            return null;
        var current = await qualificationPolicies.AcquireQualificationPolicyAsync(
            hostBindingId, transaction.TransactionId, caseId, scope, observedAt, token);
        var approval = await qualificationPolicies.ReadQualificationPolicyApprovalAsync(
            hostBindingId, transaction.TransactionId, caseId, scope, observedAt, token);
        if (!Current(transaction) || current is null || approval is null
            || current.TransactionId != transaction.TransactionId || approval.TransactionId != transaction.TransactionId
            || current.HostBindingId != hostBindingId || approval.HostBindingId != hostBindingId
            || current.CaseId != caseId || approval.CaseId != caseId || current.Scope != scope || approval.Scope != scope
            || current.ObservedAt != observedAt || approval.ObservedAt != observedAt
            || !Version7(current.SourceId) || !Version7(approval.ApprovalId) || current.SourceId == approval.ApprovalId
            || current.SourceRevision < 1 || approval.ApprovalRevision < 1
            || !Generation(current.WriterGeneration)
            || approval.ApprovedSourceId != current.SourceId || approval.ApprovedSourceRevision != current.SourceRevision
            || approval.ApprovedWriterGeneration != current.WriterGeneration
            || current.MaximumReviewerLifetime <= TimeSpan.Zero || current.MaximumReviewerLifetime == TimeSpan.MaxValue
            || current.MaximumUrgencyLifetime <= TimeSpan.Zero || current.MaximumUrgencyLifetime == TimeSpan.MaxValue
            || current.MaximumReviewerLifetime != approval.MaximumReviewerLifetime
            || current.MaximumUrgencyLifetime != approval.MaximumUrgencyLifetime
            || !Instant(current.ValidUntil) || !Instant(approval.ValidUntil) || !Instant(approval.AdoptedAt)
            || current.ValidUntil <= observedAt || approval.ValidUntil <= observedAt || approval.AdoptedAt > observedAt)
            return null;
        // An approval read cannot silently change the independently held current source.
        var repeated = await qualificationPolicies.AcquireQualificationPolicyAsync(
            hostBindingId, transaction.TransactionId, caseId, scope, observedAt, token);
        var now = clock.GetUtcNow();
        return Current(transaction) && repeated == current && now >= observedAt
            && now < current.ValidUntil && now < approval.ValidUntil ? new(current, approval) : null;
    }

    /// <summary>
    /// Exact terms for the overlay decision ledger. Native-owner controls, event-bound intervals
    /// and zero-quorum notices require their typed owning lifecycle and cannot become blanket denials.
    /// </summary>
    internal static bool Permits(
        ModerationPolicyCompiledDraft policy, ModerationPolicyCompiledProfile selected,
        SubjectReference subject, AuthorityScope scope, IReadOnlyCollection<ModerationCapability> capabilities,
        DateTimeOffset startsAt, DateTimeOffset? endsAt, DateTimeOffset? reviewAt, int requiredApprovals)
    {
        var definition = selected.Definition;
        if (!policy.Proposed.Jurisdiction.Contains(scope) || !selected.Reference.AcceptsScope(policy.Proposed.Jurisdiction, scope)
            || !definition.ScopeKinds.Contains(scope.Kind) || !definition.SubjectKinds.Contains(subject.Kind)
            || definition.RequiredApprovals != requiredApprovals || capabilities.Count == 0
            || definition.Effects.IsEmpty || definition.Effects.Any(effect =>
                effect.Execution != ModerationReferenceExecution.OverlayDenial || effect.Capability is null)
            || !definition.Effects.Select(effect => effect.Capability!.Value).Distinct().Order()
                .SequenceEqual(capabilities.Order())
            || definition.Timing.Duration <= TimeSpan.Zero
            || startsAt.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - definition.Timing.Duration.Ticks)
            return false;
        var boundary = startsAt + definition.Timing.Duration;
        return definition.Timing.Kind switch
        {
            ModerationReferenceTimingKind.Finite => endsAt == boundary && reviewAt is null,
            ModerationReferenceTimingKind.ReviewedIndefinite => endsAt is null && reviewAt == boundary,
            _ => false
        };
    }

    private bool Current(IDbContextTransaction transaction) =>
        ReferenceEquals(context.Database.CurrentTransaction, transaction)
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && transaction.GetDbTransaction().Connection == context.Database.GetDbConnection()
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId is not null;

    private static bool Instant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;

    private static bool Version7(Guid id) =>
        id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;

    private static bool Generation(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256
        && value == value.Trim() && !value.Any(char.IsControl);
}
