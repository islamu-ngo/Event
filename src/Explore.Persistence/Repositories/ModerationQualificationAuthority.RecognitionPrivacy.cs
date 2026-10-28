using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverRecognitionPrivacyHumansAsync(
        ModerationQualificationMutation? mutation, ModerationOperation? operation, Guid? publicationCandidateId,
        Guid? candidateId, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is RecordModerationRecognitionPrivacySource record) AddOwners(record.Ownership);
        if (mutation is not (RecordModerationRecognitionPrivacySource or AdoptModerationRecognitionPrivacySource
            or WithdrawModerationRecognitionPrivacyAdoption) && operation is null && publicationCandidateId is null) return;
        var publications = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token);
        if (candidateId is { } candidate) publications.Add(candidate);
        Guid? withdrawnSource = null;
        if (mutation is WithdrawModerationRecognitionPrivacyAdoption withdrawal)
            withdrawnSource = await context.Set<ModerationRecognitionPrivacyAdoption>().AsNoTracking()
                .Where(row => row.Id == withdrawal.AdoptionId).Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        var sources = await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (publications.Contains(row.PublicationId) || row.Id == withdrawnSource)).ToListAsync(token);
        foreach (var source in sources)
        {
            humans.Add(source.RecordedByHumanId);
            AddOwners(source.Ownership);
        }
        var ids = sources.Select(row => row.Id).ToArray();
        var adoptions = await context.Set<ModerationRecognitionPrivacyAdoption>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId)).ToListAsync(token);
        foreach (var adoption in adoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        void AddOwners(ModerationRecognitionPrivacyOwnership ownership)
        {
            humans.Add(ownership.PrivacyOwnerHumanId);
            humans.Add(ownership.PrivacyControllerHumanId);
            humans.Add(ownership.RecoveryOwnerHumanId);
            humans.Add(ownership.RecoveryControllerHumanId);
            humans.Add(ownership.GovernanceControllerHumanId);
        }
    }
}
