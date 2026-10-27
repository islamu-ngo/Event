using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Services;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private static bool ValidClassificationMaterialRequest(ModerationQualificationMutation mutation)
    {
        try
        {
            if (mutation is RecordModerationClassificationMaterialSource record)
            {
                foreach (var id in new[] { record.SourceId, record.EventGenerationId, record.PolicyId, record.PublicationId,
                    record.ProfileId, record.TaxonomyId, record.ExpectedTaxonomySourceId, record.ExpectedTaxonomyAdoptionId })
                    ModerationHostBinding.RequireUuidV7(id, nameof(mutation));
                if (record.Mandate is null) return false;
                record.Mandate.Validate();
                if (record.ExpectedMaterialGeneration < 0 || record.ExpectedPolicyRevision is < 1 or long.MaxValue
                    || !Enum.IsDefined(record.Mode) || record.Materials.IsDefaultOrEmpty || record.Materials.Length > 4096
                    || record.Components.IsDefaultOrEmpty || record.Components.Length > 4096
                    || record.ValidUntil.Offset != TimeSpan.Zero || record.ValidUntil == DateTimeOffset.MaxValue) return false;
                foreach (var material in record.Materials)
                {
                    if (material is null) return false;
                    material.Validate();
                }
                foreach (var component in record.Components)
                {
                    if (component is null) return false;
                    component.Validate();
                }
            }
            if (mutation is AcceptModerationClassificationMaterialSource accept)
            {
                ModerationHostBinding.RequireUuidV7(accept.SourceId, nameof(mutation));
                ModerationHostBinding.RequireUuidV7(accept.ReviewId, nameof(mutation));
                if (accept.ExpectedSourceRevision != 1 || accept.ExpectedSourceWriterGeneration is < 1 or long.MaxValue
                    || accept.ValidUntil.Offset != TimeSpan.Zero || accept.ValidUntil == DateTimeOffset.MaxValue) return false;
            }
            if (mutation is EndModerationClassificationMaterialReview end)
            {
                ModerationHostBinding.RequireUuidV7(end.ReviewId, nameof(mutation));
                if (end.ExpectedReviewRevision != 1 || !Enum.IsDefined(end.End) || !ModerationChildAgePolicyTerms.Grounds(end.Grounds)) return false;
            }
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private async Task DiscoverClassificationMaterialHumansAsync(
        ModerationQualificationMutation? mutation, ModerationOperation? operation, Guid? publicationCandidateId,
        Guid? candidateId, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is RecordModerationClassificationMaterialSource record) AddMandate(record.Mandate);
        if (mutation is not (RecordModerationClassificationMaterialSource or AcceptModerationClassificationMaterialSource
            or EndModerationClassificationMaterialReview) && operation is null && publicationCandidateId is null) return;
        var publications = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token);
        if (candidateId is { } candidate) publications.Add(candidate);
        Guid? retainedSource = (mutation as AcceptModerationClassificationMaterialSource)?.SourceId;
        if (mutation is EndModerationClassificationMaterialReview ending)
            retainedSource = await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
                .Where(row => row.Id == ending.ReviewId).Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        var sources = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (publications.Contains(row.PublicationId) || row.Id == retainedSource)).ToArrayAsync(token);
        foreach (var source in sources) AddMandate(source.Mandate);
        var ids = sources.Select(row => row.Id).ToArray();
        foreach (var review in await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId)).ToArrayAsync(token))
        {
            humans.Add(review.ReviewerHumanId);
            if (review.EndedByHumanId is { } corrector) humans.Add(corrector);
        }
        // Native read acquisitions do not yet have a material head. Discover current Event
        // generation observers before subordinate epoch fences, including first-review previews.
        var generationId = (mutation as RecordModerationClassificationMaterialSource)?.EventGenerationId;
        var generations = await context.ModerationEventGenerations.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (row.Id == generationId || !context.ModerationEventGenerations.Any(newer => newer.HostBindingId == hostId
                    && newer.TenantId == tenantId && newer.EventId == row.EventId && newer.Revision > row.Revision)))
            .ToArrayAsync(token);
        foreach (var generation in generations)
        {
            humans.Add(generation.ObservedByHumanId);
            if (generation.SubmittedByHumanId is { } publisher) humans.Add(publisher);
        }
        foreach (var eventId in sources.Select(row => row.EventId).Concat(generations.Select(row => row.EventId)).Distinct().Order())
            humans.UnionWith(await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
                source => ModerationClassificationMaterialSourceOwner.InterestedHumansAsync(source, eventId, tenantId, null, token), token));

        void AddMandate(ModerationClassificationMaterialMandate mandate)
        {
            humans.Add(mandate.OwnerHumanId);
            humans.Add(mandate.OwnerControllerHumanId);
            humans.Add(mandate.ReviewerHumanId);
            humans.Add(mandate.ReviewerControllerHumanId);
        }
    }
}
