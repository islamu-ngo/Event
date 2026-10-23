using System.Collections.Immutable;
using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAuthorityParticipant
{
    /// <summary>
    /// Reads independently adopted profile configuration on the actual acquired lifecycle transaction.
    /// Configuration authority is not installed deployment, measured conformance or target permission.
    /// </summary>
    public Task<ModerationActivationProfileObservation?> AcquireProfileConfigurationAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationDecision decision,
        ModerationAuthorityGrant grant, DateTimeOffset observedAt, CancellationToken token) =>
        AcquireProfileConfigurationAsync(_acquisition, transactionId, publication, decision, grant, observedAt, token);

    private async Task<ModerationActivationProfileObservation?> AcquireProfileConfigurationAsync(
        Acquisition? acquired, Guid transactionId, ModerationPolicyPublication publication, ModerationDecision decision,
        ModerationAuthorityGrant grant, DateTimeOffset observedAt, CancellationToken token)
    {
        if (acquired is null || !ReferenceEquals(acquired.Grant, grant) || !ReferenceEquals(acquired.Decision, decision)
            || acquired.Transaction.TransactionId != transactionId
            || context.Database.CurrentTransaction?.TransactionId != transactionId
            || acquired.Case is not { } moderationCase || acquired.Rule is not { } rule
            || publication.Id != acquired.Policy.Publication.PublicationId
            || publication.PolicyId != acquired.Policy.Publication.PolicyId || publication.Revision != grant.PolicyRevision
            || publication.HostBindingId != acquired.Source.Binding.Id
            || publication.Jurisdiction != acquired.Policy.Publication.Jurisdiction
            || publication.WriterGeneration != acquired.Policy.Publication.WriterGeneration
            || publication.CatalogueRevision != acquired.Policy.Publication.CatalogueRevision
            || acquired.DecisionRevision is not { } decisionRevision
            || decision.Id != acquired.Operation.DecisionId || decision.Revision != decisionRevision
            || decisionRevision != acquired.Operation.ExpectedRevision
            || decision.CaseId != moderationCase.Id || decision.Subject != grant.Subject
            || decision.Scope != acquired.Operation.Target || decision.Rule != rule.Definition.Rule
            || context.ModerationDecisions.Local.SingleOrDefault(row => row.Id == decision.Id) != decision
            || observedAt.Offset != TimeSpan.Zero || !await CurrentAsync(acquired, token))
            return null;

        // Reconciliation advances the sanction only, including scheduled activation.
        // Decision-changing operations author the immediately following decision revision.
        if (acquired.Operation.Kind != ModerationOperationKind.Reconcile && decisionRevision >= long.MaxValue - 1)
            return null;
        var selectionDecisionRevision = acquired.Operation.Kind == ModerationOperationKind.Reconcile
            ? decisionRevision : decisionRevision + 1;

        await using var selectionSlot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.contribution:{acquired.Source.Binding.Id:N}:{acquired.Operation.EffectId:N}", token);
        var discovered = await context.ModerationContributionSelections.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == acquired.Operation.EffectId, token);
        if (discovered is null || !acquired.Source.FencedHumans.Contains(discovered.AuthoredByHumanId)
            || !acquired.Source.Administrators.Contains(discovered.AuthoredByHumanId))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, discovered.Id, token);
        var selection = await context.ModerationContributionSelections.AsNoTracking().Include(row => row.Bindings)
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (selection is null) return null;
        selection.ValidatePersistedState();
        if (selection.AuthoredByHumanId != discovered.AuthoredByHumanId
            || selection.HostBindingId != acquired.Source.Binding.Id || selection.TrustDomainId != acquired.Source.Binding.TrustDomainId
            || selection.TenantId != acquired.TenantId || selection.DecisionId != decision.Id
            || selection.DecisionRevision != selectionDecisionRevision || selection.CaseId != moderationCase.Id
            || selection.CaseRevision != moderationCase.Revision || selection.PolicyId != publication.PolicyId
            || selection.PolicyRevision != publication.Revision || selection.PublicationId != publication.Id
            || selection.ProfileId != rule.Definition.Id || selection.StartsAt != decision.StartsAt
            || selection.RecordedAt > observedAt || selection.ValidUntil <= observedAt
            || selection.WriterGeneration > grant.QualificationRevision
            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == selection.AuthoredByHumanId && !row.IsDeleted, token))
            return null;
        var contribution = ModerationContributionSelectionProjection.Restore(selection, decision);
        if (ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selection.SelectionDigest)
            return null;
        var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(acquired.Policy, [contribution], observedAt);
        if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Effects.IsEmpty || composed.Consequences.IsEmpty
            || composed.Effects.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt)
            || composed.Consequences.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt))
            return null;
        if (!await HasNativeTargetReceiptAsync(operationId => new RecordModerationContributionSelectionSource
        {
            OperationId = operationId, Target = decision.Scope, ExpectedQualificationRevision = selection.WriterGeneration - 1,
            SelectionId = selection.Id, EffectId = selection.EffectId, CaseId = selection.CaseId,
            ExpectedCaseRevision = selection.CaseRevision, DecisionId = decision.Id, ExpectedDecisionRevision = selection.DecisionRevision,
            PolicyId = selection.PolicyId, ExpectedPolicyRevision = selection.PolicyRevision, PublicationId = selection.PublicationId,
            ProfileId = selection.ProfileId, Contribution = contribution, ValidUntil = selection.ValidUntil
        }, ModerationQualificationSourceKind.ContributionSelection, selection.Id, selection.Revision,
            selection.AuthoredByHumanId, selection.WriterGeneration, selection.RecordedAt, acquired, token))
            return null;

        var discoveredAdoption = await context.ModerationProfileAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SelectionId == selection.Id, token);
        if (discoveredAdoption is null || !acquired.Source.FencedHumans.Contains(discoveredAdoption.AdoptedByHumanId)
            || !acquired.Source.Administrators.Contains(discoveredAdoption.AdoptedByHumanId))
            return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.profile-adoption:{selection.Id:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileAdoption>(context, discoveredAdoption.Id, token);
        var adoption = await context.ModerationProfileAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discoveredAdoption.Id, token);
        if (adoption is null) return null;
        adoption.ValidatePersistedState();
        var mode = (ModerationNativeMode)adoption.ModeCode;
        var prerequisites = acquired.Policy.Prerequisites
            .SingleOrDefault(row => row.Rule == decision.Rule && row.Mode == mode)?.ActivationGates;
        if (prerequisites is null || !acquired.Policy.Proposed.Modes.Contains(mode)
            || adoption.HostBindingId != acquired.Source.Binding.Id || adoption.TrustDomainId != selection.TrustDomainId
            || adoption.TenantId != acquired.TenantId || adoption.SelectionId != selection.Id
            || adoption.SelectionRevision != selection.Revision || adoption.SelectionWriterGeneration != selection.WriterGeneration
            || adoption.PublicationId != publication.Id || adoption.ProfileId != rule.Definition.Id
            || adoption.CaseId != moderationCase.Id || adoption.CaseRevision != moderationCase.Revision
            || adoption.AdoptedByHumanId != discoveredAdoption.AdoptedByHumanId
            || adoption.AdoptedByHumanId == selection.AuthoredByHumanId || adoption.AdoptedByHumanId == decision.IssuerId
            || adoption.AdoptedByHumanId == decision.Subject.Id || adoption.RevokedAt is not null
            || adoption.AdoptedAt > observedAt || adoption.ValidUntil <= observedAt || adoption.ValidUntil > selection.ValidUntil
            || adoption.WriterGeneration > grant.QualificationRevision
            || adoption.PrerequisiteMask != prerequisites.Value.Aggregate(0, (value, gate) => value | 1 << ((int)gate - 1))
            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == adoption.AdoptedByHumanId && !row.IsDeleted, token))
            return null;
        if (!await HasNativeTargetReceiptAsync(operationId => new AdoptModerationProfileSource
        {
            OperationId = operationId, Target = decision.Scope, ExpectedQualificationRevision = adoption.WriterGeneration - 1,
            AdoptionId = adoption.Id, SelectionId = selection.Id, Mode = mode,
            CaseId = adoption.CaseId, ExpectedCaseRevision = adoption.CaseRevision, ValidUntil = adoption.ValidUntil
        }, ModerationQualificationSourceKind.ProfileAdoption, adoption.Id, 1,
            adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, acquired, token))
            return null;
        var until = selection.ValidUntil < adoption.ValidUntil ? selection.ValidUntil : adoption.ValidUntil;
        if (!await CurrentAsync(acquired, token)) return null;
        var completedAt = clock.GetUtcNow();
        return completedAt >= observedAt && completedAt < until
            ? new(transactionId, acquired.Source.Binding.Id, publication.PolicyId, publication.Revision, publication.Id,
                publication.Jurisdiction, rule.Definition.Id, decision.Id, decisionRevision, decision.Rule, mode,
                adoption.Id, adoption.Revision, adoption.WriterGeneration.ToString(CultureInfo.InvariantCulture), observedAt, until,
                prerequisites.Value, decision.Capabilities.ToImmutableArray(), contribution)
            : null;
    }
}
