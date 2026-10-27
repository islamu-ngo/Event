using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverOperationalBudgetHumansAsync(ModerationQualificationMutation? mutation,
        ModerationOperationalAction? action, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is RecordModerationOperationalBudgetSource recording)
        {
            humans.Add(recording.OperatorHumanId);
            action = recording.Terms.Action;
        }
        Guid? sourceId = (mutation as AdoptModerationOperationalBudgetSource)?.SourceId;
        if (mutation is WithdrawModerationOperationalBudgetAdoption withdrawal)
            sourceId = await context.Set<ModerationOperationalBudgetAdoption>().AsNoTracking()
                .Where(row => row.Id == withdrawal.AdoptionId && row.HostBindingId == hostId && row.TenantId == tenantId)
                .Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        if (action is null && sourceId is null) return;
        var sources = await context.Set<ModerationOperationalBudgetSource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (row.SubjectKind == ModerationSubjectKind.Human && row.Action == action || row.Id == sourceId))
            .ToListAsync(token);
        foreach (var source in sources)
        {
            humans.Add(source.RecordedByHumanId);
            humans.Add(source.OperatorHumanId);
        }
        var ids = sources.Select(row => row.Id).ToArray();
        var adoptions = await context.Set<ModerationOperationalBudgetAdoption>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId) && row.HostBindingId == hostId && row.TenantId == tenantId)
            .ToListAsync(token);
        foreach (var adoption in adoptions)
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
        }
    }
}
