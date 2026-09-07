using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Handlers.Queries;

public sealed class GetSupportAccessAuditEventsQueryHandler(
    ISupportAccessSessionRepository sessionRepository,
    ISupportAccessAuditEventRepository auditEventRepository)
    : IRequestHandler<GetSupportAccessAuditEventsQuery, PaginatedResult<SupportAccessAuditEventDto>>
{
    public async Task<PaginatedResult<SupportAccessAuditEventDto>> Handle(
        GetSupportAccessAuditEventsQuery request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, PaginatedResult<SupportAccessAuditEventDto>.MaxPageSize);
        var session = await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null || session.TargetTenantId != request.TargetTenantId)
        {
            return PaginatedResult<SupportAccessAuditEventDto>.Create([], 0, 1, limit);
        }

        var auditEvents = await auditEventRepository.ListForSessionAsync(
            request.SessionId,
            limit,
            cancellationToken);
        var items = auditEvents
            .Select(SupportAccessMapper.ToDto)
            .ToList();

        return PaginatedResult<SupportAccessAuditEventDto>.Create(items, items.Count, 1, limit);
    }
}
