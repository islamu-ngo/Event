using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private static bool ValidClassificationControlRequest(ModerationQualificationMutation mutation)
    {
        if (mutation is not (RecordModerationClassificationControlSelection or AdoptModerationClassificationEventTarget
            or RevokeModerationClassificationEventTarget)) return true;
        try
        {
            ModerationHostBinding.RequireUuidV7(mutation.OperationId, nameof(mutation));
            if (mutation.Target is null || mutation.Target.Kind != ModerationScopeKind.Event
                || mutation.ExpectedQualificationRevision is < 1 or >= long.MaxValue - 1) return false;
            switch (mutation)
            {
                case RecordModerationClassificationControlSelection record:
                    foreach (var id in new[] { record.SelectionId, record.MaterialSourceId, record.MaterialReviewId, record.EventGenerationId })
                        ModerationHostBinding.RequireUuidV7(id, nameof(mutation));
                    if (new[] { record.SelectionId, record.MaterialSourceId, record.MaterialReviewId, record.EventGenerationId }.Distinct().Count() != 4
                        || record.ExpectedMaterialSourceRevision != 1 || record.ExpectedMaterialReviewRevision != 1
                        || !Generation(record.ExpectedMaterialWriterGeneration) || !Generation(record.ExpectedReviewWriterGeneration)
                        || !Generation(record.ExpectedEventGenerationRevision) || !Generation(record.ExpectedEventWriterGeneration)
                        || record.ExpectedMaterialWriterGeneration <= record.ExpectedEventWriterGeneration
                        || record.ExpectedReviewWriterGeneration <= record.ExpectedMaterialWriterGeneration
                        || record.ExpectedQualificationRevision < record.ExpectedReviewWriterGeneration
                        || !Finite(record.ValidUntil) || record.Contribution is not { } contribution
                        || contribution.Id != record.SelectionId || contribution.Rule is null || contribution.Scope is null
                        || contribution.Scope != mutation.Target
                        || contribution.SubjectKind != ModerationSubjectKind.Content || contribution.SubjectId != contribution.Scope.Id
                        || !Finite(contribution.StartsAt) || contribution.EventWindowEnd is not null
                        || contribution.Effects.IsDefaultOrEmpty || contribution.Consequences.IsDefaultOrEmpty
                        || contribution.Effects.Length + contribution.Consequences.Length > 64
                        || contribution.Effects.Any(row => row is null || row.Effect is null || !Target(row.Target))
                        || contribution.Consequences.Any(row => row is null || !Target(row.Target)))
                        return false;
                    break;
                case AdoptModerationClassificationEventTarget adopt:
                    foreach (var id in new[] { adopt.AdoptionId, adopt.SelectionId, adopt.EventGenerationId })
                        ModerationHostBinding.RequireUuidV7(id, nameof(mutation));
                    if (new[] { adopt.AdoptionId, adopt.SelectionId, adopt.EventGenerationId }.Distinct().Count() != 3
                        || adopt.ExpectedSelectionRevision != 1 || !Generation(adopt.ExpectedEventGenerationRevision)
                        || adopt.BindingOrdinal is < 0 or >= 64 || !Finite(adopt.ValidUntil)) return false;
                    break;
                case RevokeModerationClassificationEventTarget revoke:
                    ModerationHostBinding.RequireUuidV7(revoke.AdoptionId, nameof(mutation));
                    if (revoke.ExpectedAdoptionRevision != 1 || !ModerationChildAgePolicyTerms.Grounds(revoke.Grounds)) return false;
                    break;
            }
            return true;
        }
        catch (ArgumentException) { return false; }

        static bool Generation(long value) => value is > 0 and < long.MaxValue;
        static bool Finite(DateTimeOffset value) => value.Offset == TimeSpan.Zero
            && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
        static bool Target(Explore.Application.Features.Moderation.Policy.ModerationPolicyTarget? target) =>
            target is not null && target.Scope is not null && target.SubjectKind == ModerationSubjectKind.Content
            && target.SubjectId != Guid.Empty && target.TargetId != Guid.Empty && Generation(target.TargetRevision)
            && target.WindowEnd is null && !string.IsNullOrWhiteSpace(target.Operation) && target.Operation.Length <= 100
            && target.Operation.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private async Task<Guid?> ClassificationControlPublicationAsync(ModerationQualificationMutation mutation, CancellationToken token)
    {
        if (mutation is RecordModerationClassificationControlSelection record)
            return await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
                .Where(row => row.Id == record.MaterialSourceId).Select(row => (Guid?)row.PublicationId).SingleOrDefaultAsync(token);
        var selectionId = (mutation as AdoptModerationClassificationEventTarget)?.SelectionId;
        if (mutation is RevokeModerationClassificationEventTarget revoke)
            selectionId = await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
                .Where(row => row.Id == revoke.AdoptionId).Select(row => (Guid?)row.SelectionId).SingleOrDefaultAsync(token);
        return selectionId is null ? null : await context.Set<ModerationClassificationControlSelection>().AsNoTracking()
            .Where(row => row.Id == selectionId).Select(row => (Guid?)row.PublicationId).SingleOrDefaultAsync(token);
    }

    private async Task DiscoverClassificationControlHumansAsync(ModerationQualificationMutation? mutation,
        ModerationOperation? operation, Guid? publicationCandidateId, Guid? candidateId,
        Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is not (RecordModerationClassificationControlSelection or AdoptModerationClassificationEventTarget
            or RevokeModerationClassificationEventTarget) && operation is null && publicationCandidateId is null) return;
        Guid? selectionId = (mutation as AdoptModerationClassificationEventTarget)?.SelectionId;
        if (mutation is RevokeModerationClassificationEventTarget revoke)
            selectionId = await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
                .Where(row => row.Id == revoke.AdoptionId).Select(row => (Guid?)row.SelectionId).SingleOrDefaultAsync(token);
        var selections = await context.Set<ModerationClassificationControlSelection>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (row.Id == selectionId || row.PublicationId == candidateId || operation != null || publicationCandidateId != null))
            .ToArrayAsync(token);
        foreach (var selection in selections)
        {
            humans.UnionWith(new[] { selection.OwnerHumanId, selection.OwnerControllerHumanId,
                selection.ReviewerHumanId, selection.ReviewerControllerHumanId, selection.AuthoredByHumanId });
        }
        var selectionIds = selections.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && selectionIds.Contains(row.SelectionId)).ToArrayAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.RevokedByHumanId is { } corrector) humans.Add(corrector);
        }
        var sourceIds = selections.Select(row => row.SourceId).ToList();
        if (mutation is RecordModerationClassificationControlSelection record) sourceIds.Add(record.MaterialSourceId);
        var sources = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && sourceIds.Contains(row.Id)).ToArrayAsync(token);
        foreach (var source in sources)
            humans.UnionWith(new[] { source.OwnerHumanId, source.OwnerControllerHumanId, source.ReviewerHumanId, source.ReviewerControllerHumanId });
        var generationIds = selections.Select(row => row.GenerationId).Concat(sources.Select(row => row.EventGenerationId)).ToList();
        if (mutation is RecordModerationClassificationControlSelection recording) generationIds.Add(recording.EventGenerationId);
        if (mutation is AdoptModerationClassificationEventTarget adopting) generationIds.Add(adopting.EventGenerationId);
        foreach (var generation in await context.ModerationEventGenerations.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && generationIds.Contains(row.Id)).ToArrayAsync(token))
        {
            humans.Add(generation.ObservedByHumanId);
            if (generation.SubmittedByHumanId is { } publisher) humans.Add(publisher);
        }
        // Material mandates and current native interests must also be fenced before any
        // subordinate source, definition, epoch or receipt lock is taken.
        await DiscoverClassificationMaterialHumansAsync(mutation, operation,
            publicationCandidateId ?? candidateId, candidateId, hostId, tenantId, humans, token);
    }
}
