using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionTargetMaterializer(IAdmissionTargetMaterializationRepository repository)
    : IAdmissionTargetMaterializer
{
    public async Task MaterializeAsync(
        Event eventTarget,
        EventTicketCatalogVersion catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventTarget);
        ArgumentNullException.ThrowIfNull(catalog);
        if (eventTarget.Id != catalog.EventId || eventTarget.TenantId != catalog.TenantId)
        {
            throw new ArgumentException("Admission targets must belong to the catalog event and tenant.", nameof(catalog));
        }

        Scope[] scopes = catalog.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .SelectMany(ticketType => ticketType.Entitlements)
            .Select(ToScope)
            .Distinct()
            .OrderBy(scope => scope.TargetType)
            .ThenBy(scope => scope.ScopeId)
            .ToArray();

        IReadOnlyList<EventSession> sessions = await repository.ListScheduleSessionsForUpdateAsync(
            catalog.TenantId,
            catalog.EventId,
            cancellationToken);
        IReadOnlyList<AdmissionTarget> existingTargets = await repository.ListTargetsForUpdateAsync(
            catalog.TenantId,
            catalog.EventId,
            cancellationToken);
        IReadOnlyList<AdmissionCheckInPolicy> existingPolicies = await repository.ListPoliciesAsync(
            catalog.TenantId,
            catalog.EventId,
            cancellationToken);

        Dictionary<Scope, AdmissionTarget> targetsByScope = existingTargets.ToDictionary(
            target => new Scope((AdmissionTargetTypeEnum)target.AdmissionTargetTypeId, target.ScopeId));
        HashSet<Guid> policyTargetIds = existingPolicies
            .Select(policy => policy.AdmissionTargetId)
            .ToHashSet();
        var additions = new List<(AdmissionTarget Target, DateTime OpensAtUtc, DateTime ClosesAtUtc)>();

        foreach (Scope scope in scopes)
        {
            (DateTime opensAtUtc, DateTime closesAtUtc) = ResolveScheduleBounds(scope, sessions);
            if (!targetsByScope.TryGetValue(scope, out AdmissionTarget? target))
            {
                target = AdmissionTarget.Create(
                    Guid.CreateVersion7(),
                    catalog.TenantId,
                    catalog.EventId,
                    scope.TargetType,
                    scope.TargetType == AdmissionTargetTypeEnum.EventDay ? scope.ScopeId : null,
                    scope.TargetType == AdmissionTargetTypeEnum.EventSession ? scope.ScopeId : null);
                targetsByScope.Add(scope, target);
            }

            if (!policyTargetIds.Contains(target.Id))
            {
                additions.Add((target, opensAtUtc, closesAtUtc));
                policyTargetIds.Add(target.Id);
            }
        }

        AdmissionTarget[] newTargets = additions
            .Select(addition => addition.Target)
            .Where(target => existingTargets.All(existing => existing.Id != target.Id))
            .ToArray();
        AdmissionCheckInPolicy[] newPolicies = additions
            .Select(addition => AdmissionCheckInPolicy.Create(
                Guid.CreateVersion7(),
                addition.Target,
                addition.OpensAtUtc,
                addition.ClosesAtUtc,
                maximumEntries: 1))
            .ToArray();

        if (newTargets.Length > 0)
        {
            await repository.AddTargetsAsync(newTargets, cancellationToken);
        }

        if (newPolicies.Length > 0)
        {
            await repository.AddPoliciesAsync(newPolicies, cancellationToken);
        }
    }

    private static Scope ToScope(TicketTypeEntitlement entitlement)
    {
        AdmissionTargetTypeEnum targetType = entitlement.EntitlementScopeTypeId switch
        {
            (int)EntitlementScopeTypeEnum.Event => AdmissionTargetTypeEnum.Event,
            (int)EntitlementScopeTypeEnum.EventDay => AdmissionTargetTypeEnum.EventDay,
            (int)EntitlementScopeTypeEnum.EventSession => AdmissionTargetTypeEnum.EventSession,
            _ => throw new ArgumentException("Ticket entitlement scope is not supported for admission.")
        };
        return new Scope(targetType, entitlement.ScopeId);
    }

    private static (DateTime OpensAtUtc, DateTime ClosesAtUtc) ResolveScheduleBounds(
        Scope scope,
        IReadOnlyList<EventSession> sessions)
    {
        EventSession[] scopedSessions = scope.TargetType switch
        {
            AdmissionTargetTypeEnum.Event => sessions.ToArray(),
            AdmissionTargetTypeEnum.EventDay => sessions
                .Where(session => session.EventDayId == scope.ScopeId)
                .ToArray(),
            AdmissionTargetTypeEnum.EventSession => sessions
                .Where(session => session.Id == scope.ScopeId)
                .ToArray(),
            _ => []
        };

        if (scopedSessions.Length == 0 ||
            scopedSessions.Any(session =>
                session.StartTime is null ||
                session.EndTime is null ||
                session.EndTime <= session.StartTime))
        {
            throw new ArgumentException(
                "Admission targets require complete UTC schedule bounds for every session in their entitlement scope.");
        }

        return (
            scopedSessions.Min(session => session.StartTime!.Value).UtcDateTime,
            scopedSessions.Max(session => session.EndTime!.Value).UtcDateTime);
    }

    private readonly record struct Scope(AdmissionTargetTypeEnum TargetType, Guid ScopeId);
}
