using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;

namespace Explore.Persistence.Security;

internal sealed class HumanActionCommitFinalizer(
    ExploreDbContext context, ExploreDbContextExecutionLease lease, TimeProvider clock) : IHumanActionCommitFinalizer
{
    public void Register(Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments)
    {
        lease.RequireOriginalOwner(context);
        var subject = lease.HumanScope?.Subject
            ?? throw new UnauthorizedAccessException("Final admission requires the original resolved human.");
        var lifetime = lease.HumanLifetime
            ?? throw new UnauthorizedAccessException("Final admission requires original validated credential custody.");
        context.RegisterHumanCommitAdmission(subject, lifetime, clock, validateSources, validateAssignments);
    }
}
