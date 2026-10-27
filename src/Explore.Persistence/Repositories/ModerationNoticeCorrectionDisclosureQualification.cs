using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Qualifies retained correction content without granting approval or recipient access.</summary>
internal static class ModerationNoticeCorrectionDisclosureQualification
{
    internal static async Task<DateTimeOffset?> ReadAsync(
        ExploreDbContext context, ModerationNoticeDisclosureIntent intent,
        string expectedPendingSnapshotHash, string expectedPendingEffectBindingHash,
        ModerationSourceAuthority authority, TimeProvider clock, CancellationToken token, DateTimeOffset? accessUntil = null)
    {
        try
        {
            var purposeUntil = accessUntil is { } access && access < intent.ValidUntil ? access : intent.ValidUntil;
            bool Live() => clock.GetUtcNow() < purposeUntil && clock.GetUtcNow() < intent.RetainUntil;
            if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not { } transaction
                || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
                || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != authority.Tenant.Id
                || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
                || authority.Tenant.Kind != ModerationScopeKind.Tenant
                || authority.Tenant.TrustDomainId != authority.Binding.TrustDomainId || authority.UserId == Guid.Empty)
                return null;
            intent.Validate();
            if (intent.ContentRepresentatives.IsDefault || intent.ContentRepresentatives.Length != 0
                || intent.ValidUntil > intent.RetainUntil || !Live()
                || !await ModerationSourceReadSession.ProviderTenantMatchesAsync(context, authority.Tenant.Id, token))
                return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, intent.CaseId, token);
            var actualCase = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == intent.CaseId, token);
            if (actualCase is null) return null;
            actualCase.ValidatePersistedState();
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, intent.DecisionId, token);
            var decision = await context.ModerationDecisions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == intent.DecisionId, token);
            if (decision is null || decision.CaseId != intent.CaseId || decision.Revision != intent.ExpectedDecisionRevision
                || actualCase.Revision != intent.ExpectedCaseRevision || actualCase.Subject != decision.Subject
                || actualCase.Scope.TrustDomainId != authority.Binding.TrustDomainId
                || decision.Scope.TrustDomainId != authority.Binding.TrustDomainId
                || actualCase.Scope != decision.Scope
                    && (actualCase.AttributionSourceId is null || decision.Scope.Kind != ModerationScopeKind.Resource
                        || actualCase.Scope != decision.Scope.Parent))
                return null;
            decision.ValidatePersistedState();
            var discovered = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
                source => source.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == intent.EffectId, token), token);
            if (discovered is null || discovered.CompletedAt is not null) return null;
            var original = await ModerationNoticeCorrectionAudience.ReadAsync(context, discovered, decision, clock, token, purposeUntil);
            if (original is null || !Live()) return null;

            // Follow the original-before-dependent order shared by capture and purpose cleanup.
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, intent.EffectId, token);
            var ids = await context.ModerationNotices.AsNoTracking().Where(row => row.EffectId == intent.EffectId)
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
            foreach (var id in ids) await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, id, token);
            await using var sourceSlot = await RelationalNamedLock.AcquireTransactionAsync(context,
                $"moderation.notice:{intent.EffectId:D}", token);
            var effect = await context.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == intent.EffectId, token);
            var envelope = await context.ModerationNoticeSources.AsNoTracking().Where(row => row.EffectId == intent.EffectId)
                .Select(row => new { row.RetainUntil, row.SnapshotHash, row.EffectBindingHash }).SingleOrDefaultAsync(token);
            if (effect is null || effect.CompletedAt is not null || effect.OriginalCompletionId is not null
                || effect.OriginalNoticeCleanupId is not null || envelope is null
                || envelope.RetainUntil != original.RetainUntil || intent.RetainUntil != original.RetainUntil
                || envelope.SnapshotHash != expectedPendingSnapshotHash
                || envelope.EffectBindingHash != expectedPendingEffectBindingHash
                || !Live())
                return null;
            var pendingSource = await context.ModerationNoticeSources.AsNoTracking().SingleOrDefaultAsync(
                row => row.EffectId == effect.Id && row.SnapshotHash == envelope.SnapshotHash
                    && row.EffectBindingHash == envelope.EffectBindingHash && row.RetainUntil == original.RetainUntil, token);
            if (!Live() || pendingSource is null || !pendingSource.Matches(effect))
                return null;
            var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(pendingSource.SnapshotJson);
            if (capture is null || !capture.Matches(effect) || capture.Notice is not null
                || capture.WriterTenantId != authority.Tenant.Id || capture.HostBindingId != authority.Binding.Id
                || capture.HostSolutionId != authority.Binding.SolutionId || capture.HostInstanceId != authority.Binding.InstanceId
                || !ModerationNoticeCorrectionAudience.Same(capture.PendingCorrection, original)
                || !await ModerationNoticeCorrectionAudience.PendingOccurrencesMatchAsync(context, pendingSource, original, clock, token))
                return null;
            var accepted = await context.ModerationOperationReceipts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == effect.OperationId, token);
            if (accepted is null || accepted.DecisionId != effect.DecisionId || accepted.SanctionId != effect.SanctionId
                || accepted.ResultRevision != effect.SanctionRevision || accepted.PolicyRevision != effect.PolicyRevision
                || accepted.QualificationRevision != effect.QualificationRevision || accepted.CompletedAt != effect.CreatedAt
                || accepted.TrustDomainId != effect.TrustDomainId || accepted.SubjectKind != effect.SubjectKind
                || accepted.SubjectId != effect.SubjectId || accepted.ScopeKind != effect.ScopeKind || accepted.ScopeId != effect.ScopeId)
                return null;

            var originalReadAt = clock.GetUtcNow();
            if (!Live()) return null;
            var originalSource = await context.ModerationNoticeSources.AsNoTracking().SingleOrDefaultAsync(row =>
                row.EffectId == original.OriginalEffectId && row.SnapshotHash == original.OriginalSnapshotHash
                && row.EffectBindingHash == original.OriginalEffectBindingHash && row.RetainUntil == original.RetainUntil
                && row.RetainUntil > originalReadAt, token);
            if (!Live() || originalSource is null) return null;
            var originalCapture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(originalSource.SnapshotJson);
            if (originalCapture?.Notice is not { } notice || intent.PolicyId != notice.PolicyId
                || intent.ExpectedPolicyRevision != notice.PolicyRevision || intent.PublicationId != notice.PublicationId
                || intent.ProfileId != notice.ProfileId || intent.ContributionId != notice.ContributionId)
                return null;
            await using var originalApprovalSlot = await RelationalNamedLock.AcquireTransactionAsync(context,
                $"moderation.disclosure:{authority.Binding.Id:N}:{original.OriginalEffectId:N}", token);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, notice.ApprovalId, token);
            var approvalReadAt = clock.GetUtcNow();
            if (!Live()) return null;
            var originalApproval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == notice.ApprovalId && row.RetainUntil == original.RetainUntil
                    && row.RetainUntil > approvalReadAt, token);
            if (!Live() || originalApproval is null) return null;
            originalApproval.ValidatePersistedState();
            if (originalApproval.EffectId != original.OriginalEffectId || originalApproval.DecisionId != decision.Id
                || originalApproval.CaseId != intent.CaseId || originalApproval.HostBindingId != authority.Binding.Id
                || originalApproval.TrustDomainId != authority.Binding.TrustDomainId || originalApproval.TenantId != authority.Tenant.Id
                || originalApproval.DecisionRevision != notice.DecisionRevision
                || originalApproval.PolicyId != notice.PolicyId || originalApproval.PolicyRevision != notice.PolicyRevision
                || originalApproval.PublicationId != notice.PublicationId || originalApproval.ProfileId != notice.ProfileId
                || originalApproval.ContributionId != notice.ContributionId
                || originalApproval.RetainUntil != original.RetainUntil || originalApproval.Intent.SelectionDigest != intent.SelectionDigest)
                return null;

            AuthorityScope? jurisdiction = decision.Scope;
            while (jurisdiction is not null && jurisdiction.Id != decision.Rule.OwnerId) jurisdiction = jurisdiction.Parent;
            if (jurisdiction is null || jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
                return null;
            var publication = await ModerationPolicyDefinitionSourceOwner.AcquireSelectedInTransactionAsync(context,
                transaction.TransactionId, authority.Binding.Id, jurisdiction, intent.PolicyId, false, token);
            if (!Live() || publication is null || publication.Id != intent.PublicationId
                || publication.Revision != intent.ExpectedPolicyRevision)
                return null;
            var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication);
            var profile = draft?.Profiles.SingleOrDefault(row => row.Definition.Id == intent.ProfileId
                && row.Definition.Rule == decision.Rule && row.Definition.ScopeKinds.Contains(decision.Scope.Kind)
                && row.Reference.AcceptsScope(publication.Jurisdiction, decision.Scope));
            var reason = profile is null ? null : draft!.Proposed.Reasons.SingleOrDefault(row => row.Reference == profile.Definition.Reason);
            var classification = reason is null ? null : draft!.Proposed.Classes.SingleOrDefault(row => row.Reference == reason.Class);
            if (reason is null || classification is null || intent.RuleMeaning.Value != classification.Meaning
                || intent.ReasonMeaning.Value != reason.Meaning || !Live()
                || clock.GetUtcNow() >= original.RetainUntil || clock.GetUtcNow() < effect.CreatedAt)
                return null;
            return intent.ValidUntil;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or FormatException)
        {
            return null;
        }
    }
}
