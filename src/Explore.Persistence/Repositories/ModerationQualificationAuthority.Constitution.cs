using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverConstitutionHumansAsync(
        ModerationQualificationMutation? mutation, ModerationOperation? operation, Guid? publicationCandidateId,
        Guid? candidateId, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is RecordModerationConstitutionSource record)
        {
            humans.Add(record.Terms.CoordinatorHumanId);
            humans.Add(record.Terms.FallbackHumanId);
            foreach (var row in record.Reviewers)
            {
                humans.Add(row.ReviewerHumanId);
                humans.Add(row.ControllerHumanId);
            }
        }
        if (mutation is WithdrawModerationConstitutionAvailability withdrawalAvailability)
        {
            var owner = await context.Set<ModerationConstitutionAvailability>().AsNoTracking()
                .Where(row => row.Id == withdrawalAvailability.AvailabilityId)
                .Select(row => (Guid?)row.ReviewerHumanId).SingleOrDefaultAsync(token);
            if (owner is { } human) humans.Add(human);
        }
        if (mutation is not (RecordModerationConstitutionSource or AdoptModerationConstitutionSource or WithdrawModerationConstitutionAdoption)
            && operation is null && publicationCandidateId is null) return;
        var publicationIds = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token);
        if (candidateId is { } candidate) publicationIds.Add(candidate);
        Guid? withdrawnSource = null;
        if (mutation is WithdrawModerationConstitutionAdoption withdrawal)
            withdrawnSource = await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
                .Where(row => row.Id == withdrawal.AdoptionId).Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        var sources = await context.Set<ModerationConstitutionSource>().AsNoTracking().Include(row => row.Reviewers)
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (publicationIds.Contains(row.PublicationId) || row.Id == withdrawnSource)).ToListAsync(token);
        foreach (var source in sources)
        {
            humans.Add(source.RecordedByHumanId);
            humans.Add(source.CoordinatorHumanId);
            humans.Add(source.FallbackHumanId);
            foreach (var reviewer in source.Reviewers)
            {
                humans.Add(reviewer.ReviewerHumanId);
                humans.Add(reviewer.ControllerHumanId);
            }
        }
        var ids = sources.Select(row => row.Id).ToArray();
        var adoptions = await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId)).ToListAsync(token);
        foreach (var adoption in adoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
    }
}
