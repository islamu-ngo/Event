using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverDeploymentHumansAsync(
        ModerationQualificationMutation? mutation, ModerationOperation? operation, Guid? publicationCandidateId,
        Guid? candidateId, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is not (RecordModerationDeploymentSource or AdoptModerationDeploymentSource or WithdrawModerationDeploymentAdoption)
            && operation is null && publicationCandidateId is null) return;
        var publicationIds = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token);
        if (candidateId is { } candidate) publicationIds.Add(candidate);
        var sources = await context.Set<ModerationDeploymentSource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && publicationIds.Contains(row.PublicationId))
            .Select(row => new { row.Id, row.RecordedByHumanId }).ToListAsync(token);
        foreach (var source in sources) humans.Add(source.RecordedByHumanId);
        var sourceIds = sources.Select(row => row.Id).ToArray();
        var adoptions = await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
            .Where(row => sourceIds.Contains(row.SourceId)).ToListAsync(token);
        if (mutation is WithdrawModerationDeploymentAdoption withdrawal)
        {
            var adoption = await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == withdrawal.AdoptionId, token);
            if (adoption is not null)
            {
                adoptions.Add(adoption);
                var recorder = await context.Set<ModerationDeploymentSource>().AsNoTracking()
                    .Where(row => row.Id == adoption.SourceId).Select(row => (Guid?)row.RecordedByHumanId).SingleOrDefaultAsync(token);
                if (recorder is { } human) humans.Add(human);
            }
        }
        foreach (var adoption in adoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
        }
        // Discovery only: the borrowed session independently requalifies the latest receipted installation.
        var installed = await context.ModerationConsumerInstallations.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && row.WithdrawnAt == null)
            .Select(row => row.InstalledByHumanId).Distinct().ToListAsync(token);
        foreach (var installer in installed) humans.Add(installer);
    }
}
