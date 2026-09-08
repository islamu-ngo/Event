using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionCheckInReportingService(
    IAuthorizationProvider authorization,
    IAdmissionCheckInSummaryQuery summaryQuery,
    IAdmissionCheckInReportingRepository repository)
{
    public const int MaximumPageSize = 100;

    public async Task<AdmissionCheckInDetail?> GetDetailAsync(
        AdmissionCheckInDetailRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetDetailCoreAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AdmissionCheckInUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AdmissionCheckInUnavailableException();
        }
    }

    private async Task<AdmissionCheckInDetail?> GetDetailCoreAsync(
        AdmissionCheckInDetailRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            request.TenantId == Guid.Empty ||
            request.EventId == Guid.Empty ||
            request.CheckInId == Guid.Empty ||
            request.CheckInId.Version != 7 ||
            request.StaffActorId == Guid.Empty ||
            !await IsAuthorizedAsync(
                request.TenantId,
                request.EventId,
                request.StaffActorId,
                cancellationToken))
        {
            return null;
        }

        AdmissionCheckInEvent? fact = await repository.GetEventAsync(
            request.TenantId,
            request.EventId,
            request.CheckInId,
            cancellationToken);
        if (fact is null ||
            fact.TenantId != request.TenantId ||
            fact.Id != request.CheckInId)
        {
            return null;
        }

        AdmissionCheckInEvent? activeEvent = await repository.GetActiveEventAsync(
            request.TenantId,
            fact.AdmissionTicketId,
            fact.AdmissionTargetId,
            cancellationToken);
        bool canUndo =
            (AdmissionCheckInActionEnum)fact.AdmissionCheckInActionId ==
                AdmissionCheckInActionEnum.CheckIn &&
            activeEvent?.Id == fact.Id;
        AdmissionCheckInOutcome outcome =
            (AdmissionCheckInActionEnum)fact.AdmissionCheckInActionId switch
            {
                AdmissionCheckInActionEnum.CheckIn => AdmissionCheckInOutcome.CheckedIn,
                AdmissionCheckInActionEnum.Undo => AdmissionCheckInOutcome.Undone,
                _ => AdmissionCheckInOutcome.Rejected
            };
        return new AdmissionCheckInDetail(
            new AdmissionCheckInResult(
                outcome,
                fact.AdmissionTargetId,
                new DateTimeOffset(fact.OccurredAtUtc),
                fact.Id),
            canUndo);
    }

    public async Task<AdmissionCheckInSummary?> GetSummaryAsync(
        AdmissionCheckInSummaryRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidSummaryRequest(request))
        {
            return null;
        }

        try
        {
            if (!await IsAuthorizedAsync(
                    request.TenantId, request.EventId, request.StaffActorId, cancellationToken))
            {
                return null;
            }

            AdmissionCheckInSummaryProjection? projection = await summaryQuery.GetAsync(
                request.TenantId, request.EventId, request.TargetId, cancellationToken);
            if (!ValidSummaryProjection(projection, request))
            {
                return null;
            }

            return new AdmissionCheckInSummary(
                projection!.TargetType,
                [
                    new AdmissionCheckInResultCount(
                        AdmissionCheckInOutcome.CheckedIn, projection.CheckedInCount),
                    new AdmissionCheckInResultCount(
                        AdmissionCheckInOutcome.Undone, projection.UndoneCount)
                ],
                [
                    new AdmissionCheckInStateCount(
                        AdmissionCheckInSummaryState.Active, projection.ActiveStateCount),
                    new AdmissionCheckInStateCount(
                        AdmissionCheckInSummaryState.Inactive, projection.InactiveStateCount)
                ],
                projection.LastActivityUtc.HasValue
                    ? ToHourBucket(projection.LastActivityUtc.Value)
                    : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AdmissionCheckInUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AdmissionCheckInUnavailableException();
        }
    }

    public async Task<AdmissionCheckInAuditPage?> GetAuditPageAsync(
        AdmissionCheckInAuditPageRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidAuditRequest(request))
        {
            return null;
        }

        try
        {
            if (!await IsAuthorizedAsync(
                    request.TenantId, request.EventId, request.StaffActorId, cancellationToken))
            {
                return null;
            }

            if (!AdmissionCheckInAuditCursor.TryDecode(request.Cursor, out AdmissionCheckInAuditCursor? cursor))
            {
                return null;
            }

            AdmissionCheckInEvent? cursorEvent = null;
            if (cursor is not null)
            {
                cursorEvent = await repository.GetEventAsync(
                    request.TenantId,
                    request.EventId,
                    cursor.CheckInId,
                    cancellationToken);
                if (cursorEvent is null || cursorEvent.OccurredAtUtc != cursor.OccurredAtUtc)
                {
                    return null;
                }
            }

            int requestedRows = checked(request.PageSize + 1);
            IReadOnlyList<AdmissionCheckInEvent> page = await repository.ListEventAuditPageAsync(
                request.TenantId,
                request.EventId,
                cursorEvent,
                requestedRows,
                cancellationToken);
            if (page is null || page.Count > requestedRows)
            {
                throw new InvalidOperationException("Admission audit page was not bounded.");
            }

            AdmissionCheckInEvent[] visible = page.Take(request.PageSize).ToArray();
            Guid[] targetIds = visible.Select(item => item.AdmissionTargetId).Distinct().ToArray();
            IReadOnlyList<AdmissionTarget> targets = targetIds.Length == 0
                ? []
                : await repository.ListTargetsAsync(
                    request.TenantId, request.EventId, targetIds, cancellationToken);
            Dictionary<Guid, AdmissionTarget> targetById = targets
                .Where(target => ValidTarget(target, request.TenantId, request.EventId, target.Id))
                .ToDictionary(target => target.Id);
            if (targetById.Count != targetIds.Length || visible.Any(item =>
                    item.TenantId != request.TenantId || !targetById.ContainsKey(item.AdmissionTargetId)))
            {
                return null;
            }

            AdmissionCheckInAuditItem[] items = visible.Select(item => new AdmissionCheckInAuditItem(
                CursorFor(item),
                ToApplicationAction(item.AdmissionCheckInActionId),
                ToOutcome(item.AdmissionCheckInActionId),
                (AdmissionTargetTypeEnum)targetById[item.AdmissionTargetId].AdmissionTargetTypeId,
                ToHourBucket(item.OccurredAtUtc))).ToArray();
            string? nextCursor = page.Count > request.PageSize && items.Length > 0
                ? items[^1].Cursor
                : null;
            return new AdmissionCheckInAuditPage(items, nextCursor);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AdmissionCheckInUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AdmissionCheckInUnavailableException();
        }
    }

    private async Task<bool> IsAuthorizedAsync(
        Guid tenantId,
        Guid eventId,
        Guid staffActorId,
        CancellationToken cancellationToken)
    {
        AuthorizationDecision decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            eventId.ToString("D"),
            AuthorizationActions.Events.EventCheckInView,
            new AuthorizationScope(TenantId: tenantId.ToString("D")),
            new EventScopedAuthorizationFacts(tenantId, eventId),
            new AuthorizationSubject(staffActorId)), cancellationToken);
        return decision.IsAllowed;
    }

    private static bool ValidSummaryRequest(AdmissionCheckInSummaryRequest? request) =>
        request is not null && request.TenantId != Guid.Empty && request.EventId != Guid.Empty &&
        request.TargetId != Guid.Empty && request.StaffActorId != Guid.Empty;

    private static bool ValidAuditRequest(AdmissionCheckInAuditPageRequest? request) =>
        request is not null && request.TenantId != Guid.Empty && request.EventId != Guid.Empty &&
        request.StaffActorId != Guid.Empty &&
        request.PageSize is >= 1 and <= MaximumPageSize &&
        AdmissionCheckInAuditCursor.TryDecode(request.Cursor, out _);

    private static bool ValidSummaryProjection(
        AdmissionCheckInSummaryProjection? projection,
        AdmissionCheckInSummaryRequest request) =>
        projection is not null && projection.TenantId == request.TenantId &&
        projection.EventId == request.EventId && projection.TargetId == request.TargetId &&
        Enum.IsDefined(projection.TargetType) && projection.CheckedInCount >= 0 &&
        projection.UndoneCount >= 0 && projection.ActiveStateCount >= 0 &&
        projection.InactiveStateCount >= 0;

    private static bool ValidTarget(
        AdmissionTarget? target,
        Guid tenantId,
        Guid eventId,
        Guid targetId) =>
        target is not null && target.Id == targetId && target.TenantId == tenantId &&
        target.EventId == eventId && Enum.IsDefined((AdmissionTargetTypeEnum)target.AdmissionTargetTypeId);

    private static AdmissionCheckInAction ToApplicationAction(int actionId) =>
        (AdmissionCheckInActionEnum)actionId switch
        {
            AdmissionCheckInActionEnum.CheckIn => AdmissionCheckInAction.CheckIn,
            AdmissionCheckInActionEnum.Undo => AdmissionCheckInAction.Undo,
            _ => throw new InvalidOperationException("Unknown admission action.")
        };

    private static AdmissionCheckInOutcome ToOutcome(int actionId) =>
        (AdmissionCheckInActionEnum)actionId switch
        {
            AdmissionCheckInActionEnum.CheckIn => AdmissionCheckInOutcome.CheckedIn,
            AdmissionCheckInActionEnum.Undo => AdmissionCheckInOutcome.Undone,
            _ => throw new InvalidOperationException("Unknown admission action.")
        };

    private static string CursorFor(AdmissionCheckInEvent item) =>
        new AdmissionCheckInAuditCursor(item.OccurredAtUtc, item.Id).Encode();

    private static DateTimeOffset ToHourBucket(DateTime occurredAtUtc)
    {
        DateTime utc = occurredAtUtc.Kind == DateTimeKind.Utc
            ? occurredAtUtc
            : occurredAtUtc.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
