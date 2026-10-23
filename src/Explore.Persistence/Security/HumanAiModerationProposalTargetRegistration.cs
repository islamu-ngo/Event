using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

internal sealed class HumanAiModerationProposalTargetFactory(
    IServiceProvider provider, IdentityDatabaseTopology identityTopology) : IHumanAiModerationProposalTargetSourceFactory
{
    public IHumanAiModerationProposalTargetSource Create(Guid eventId)
    {
        var context = provider.GetRequiredService<ExploreDbContext>();
        var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(context);
        var subject = lease.HumanScope?.Subject
            ?? throw new UnauthorizedAccessException("Moderation proposal discovery requires the original human.");
        var lifetime = lease.HumanLifetime
            ?? throw new UnauthorizedAccessException("Moderation proposal discovery requires original credential custody.");
        if (lease.SelectedTenantId is null || lease.PolicyPurpose is not null
            || lease.HumanScope!.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant)
            throw new UnauthorizedAccessException("Moderation proposal discovery requires the original ordinary tenant owner.");
        if (lifetime.LocalSession is not null && identityTopology == IdentityDatabaseTopology.External)
            throw new NotSupportedException("ai_moderation_target_requires_colocated_local_identity");
        return new HumanAiModerationProposalTargetSource(context, subject, eventId,
            HumanSourceProviderOperations.BindEnrollment(context, () => provider.GetRequiredService<IConfiguration>()),
            lifetime.LocalSession is null ? null : context);
    }
}
