using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class EventLocationManagementAuthorizationService(
    IEventRepository eventRepository,
    IAuthorizationProvider authorizationProvider,
    ICurrentUserService currentUserService,
    IEventLocationExactReadAuditService auditService,
    ILogger<EventLocationManagementAuthorizationService> logger)
    : IEventLocationManagementAuthorizationService
{
    public async Task<IReadOnlyDictionary<Guid, bool>> AuthorizeManyAsync(
        IReadOnlyCollection<EventLocation> eventLocations,
        EventLocationExactReadPurposeEnum purpose,
        Guid? correlationId,
        Guid? traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventLocations);
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        EventLocation[] normalized = Normalize(eventLocations);
        if (normalized.Length == 0)
        {
            return ImmutableDictionary<Guid, bool>.Empty;
        }

        Guid? requesterUserId = currentUserService.UserId;
        if (!currentUserService.IsAuthenticated || requesterUserId is null || requesterUserId == Guid.Empty)
        {
            logger.LogWarning("EventLocation management authorization denied because no authenticated user identity was available.");
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement);
        }

        Guid tenantId = normalized[0].TenantId;
        Guid[] eventIds = normalized
            .Select(item => item.EventId)
            .Distinct()
            .Order()
            .ToArray();

        IReadOnlyList<Event> targets;
        try
        {
            targets = await eventRepository.GetAuthorizationTargetsByIdsAsync(eventIds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "EventLocation management authorization target loading failed closed. FailureType={FailureType}",
                ex.GetType().Name);
            targets = [];
        }

        var descriptor = ResourceDescriptors.EventAuthorizationTarget;
        Event[] validTargets = targets
            .Where(target => target.TenantId == tenantId && eventIds.Contains(target.Id))
            .OrderBy(target => target.Id)
            .ToArray();
        var checks = validTargets
            .Select(target => new AuthorizationRequest(
                descriptor.Kind,
                descriptor.GetResourceId(target),
                AuthorizationActions.Events.ViewManagement,
                Scope: descriptor.GetScope(target),
                Facts: descriptor.GetFacts(target)))
            .ToArray();

        IReadOnlyList<AuthorizationDecision> providerDecisions = [];
        try
        {
            providerDecisions = await authorizationProvider.AuthorizeBatchAsync(checks, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "EventLocation management authorization provider failed closed. FailureType={FailureType}",
                ex.GetType().Name);
        }

        var decisionByEventId = new Dictionary<Guid, bool>(validTargets.Length);
        for (var index = 0; index < validTargets.Length; index++)
        {
            decisionByEventId[validTargets[index].Id] =
                index < providerDecisions.Count && providerDecisions[index].IsAllowed;
        }

        ImmutableDictionary<Guid, bool> decisions = normalized.ToImmutableDictionary(
            item => item.Id,
            item => decisionByEventId.GetValueOrDefault(item.EventId));
        EventLocationExactReadAuditRequest[] auditRequests = normalized
            .Select(item => new EventLocationExactReadAuditRequest(
                item.TenantId,
                item.Id,
                requesterUserId.Value,
                purpose,
                decisions[item.Id],
                correlationId,
                traceId))
            .ToArray();

        await auditService.RecordManyAsync(auditRequests, cancellationToken);
        return decisions;
    }

    private static EventLocation[] Normalize(IReadOnlyCollection<EventLocation> eventLocations)
    {
        if (eventLocations.Count > IEventLocationManagementAuthorizationService.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventLocations),
                $"EventLocation management authorization batches cannot exceed {IEventLocationManagementAuthorizationService.MaximumBatchSize} records.");
        }

        EventLocation[] normalized = eventLocations
            .DistinctBy(item => item.Id)
            .OrderBy(item => item.Id)
            .ToArray();
        if (normalized.Any(item => item.Id == Guid.Empty || item.TenantId == Guid.Empty || item.EventId == Guid.Empty))
        {
            throw new ArgumentException("EventLocation authorization candidates require non-empty identities.", nameof(eventLocations));
        }

        if (normalized.Select(item => item.TenantId).Distinct().Take(2).Count() != 1)
        {
            throw new ArgumentException("EventLocation management authorization requires one tenant per batch.", nameof(eventLocations));
        }

        return normalized;
    }
}
