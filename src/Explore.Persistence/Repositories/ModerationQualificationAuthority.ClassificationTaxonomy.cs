using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverClassificationTaxonomyHumansAsync(
        ModerationQualificationMutation? mutation, ModerationOperation? operation, Guid? publicationCandidateId,
        Guid? candidateId, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is RecordModerationClassificationTaxonomySource record) AddMandate(record.Mandate);
        if (mutation is not (RecordModerationClassificationTaxonomySource or AdoptModerationClassificationTaxonomySource
            or WithdrawModerationClassificationTaxonomyAdoption) && operation is null && publicationCandidateId is null) return;
        var publications = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token);
        if (candidateId is { } candidate) publications.Add(candidate);
        Guid? withdrawnSource = null;
        if (mutation is WithdrawModerationClassificationTaxonomyAdoption withdrawal)
            withdrawnSource = await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
                .Where(row => row.Id == withdrawal.AdoptionId).Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        var sources = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (publications.Contains(row.PublicationId) || row.Id == withdrawnSource)).ToListAsync(token);
        foreach (var source in sources) AddMandate(source.Mandate);
        var ids = sources.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId)).ToListAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        void AddMandate(ModerationClassificationTaxonomyMandate mandate)
        {
            humans.Add(mandate.OwnerHumanId);
            humans.Add(mandate.OwnerControllerHumanId);
            humans.Add(mandate.ReviewerHumanId);
            humans.Add(mandate.ReviewerControllerHumanId);
        }
    }
}
