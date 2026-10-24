using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    private async Task<NativeEnvironment?> AttributionEnvironmentAsync(
        ModerationAuthorityParticipant.FindingProvenanceSession provenance, bool acquire, CancellationToken token)
    {
        var source = await provenance.AcquireAsync(context, provenance.CaseId, token);
        if (source is null) return null;
        if (acquire)
        {
            // The parent already holds these exact rows in native global order. Register that
            // custody instead of acquiring new human locks after case/finding fences.
            foreach (var human in source.FencedHumans) _fences.Add((typeof(User), human));
            var humans = source.FencedHumans.ToArray();
            foreach (var id in await context.PlatformUserRoles.AsNoTracking()
                .Where(row => humans.Contains(row.UserId)).Select(row => row.Id).ToArrayAsync(token))
                _fences.Add((typeof(PlatformUserRole), id));
            var members = await context.TenantUsers.AsNoTracking()
                .Where(row => row.TenantId == source.Tenant.Id && humans.Contains(row.UserId))
                .Select(row => row.Id).ToArrayAsync(token);
            foreach (var id in members) _fences.Add((typeof(TenantUser), id));
            foreach (var id in await context.TenantUserRoleGrants.AsNoTracking()
                .Where(row => members.Contains(row.TenantUserId)).Select(row => row.Id).ToArrayAsync(token))
                _fences.Add((typeof(TenantUserRoleGrant), id));
            _fences.Add((typeof(Tenant), source.Tenant.Id));
            _fences.Add((typeof(ModerationHostBinding), source.Binding.Id));
            _fences.Add((typeof(ServicePrincipal), source.Binding.MaintenanceServicePrincipalId));
            _fences.Add((typeof(ModerationQualificationEpoch), source.Epoch.Id));
        }
        return new(context.Database.CurrentTransaction!.TransactionId, source.UserId, source.Tenant.Id,
            source.Binding.Id, source.Binding.TrustDomainId, source.Binding.SolutionId, source.Binding.InstanceId,
            source.Binding.MaintenanceServicePrincipalId, source.Epoch.Id, source.Epoch.Revision);
    }
}
