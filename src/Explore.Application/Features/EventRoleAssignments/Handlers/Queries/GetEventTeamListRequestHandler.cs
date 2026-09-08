using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventRoleAssignments.Handlers.Queries;

public sealed class GetEventTeamListRequestHandler
    : IRequestHandler<GetEventTeamListRequest, List<EventTeamMemberDto>>
{
    private readonly IEventRoleAssignmentRepository _eventRoleAssignmentRepository;

    public GetEventTeamListRequestHandler(IEventRoleAssignmentRepository eventRoleAssignmentRepository)
    {
        _eventRoleAssignmentRepository = eventRoleAssignmentRepository;
    }

    public async Task<List<EventTeamMemberDto>> Handle(
        GetEventTeamListRequest request,
        CancellationToken cancellationToken)
    {
        var assignments = await _eventRoleAssignmentRepository.GetTeamMembersForEventAsync(
            request.TenantId,
            request.EventId,
            request.IncludeInactive,
            cancellationToken);

        var utcNow = DateTime.UtcNow;

        return assignments
            .Select(a => new EventTeamMemberDto
            {
                TenantId = a.TenantId,
                EventId = a.EventId,
                AssignmentId = a.Id,
                UserId = a.UserId,
                UserEmail = a.User.Email,
                UserFullName = $"{a.User.FirstName} {a.User.LastName}",
                RoleId = a.RoleId,
                RoleName = a.Role.FullName,
                RoleMasterCode = a.Role.MasterCode,
                Status = a.Status,
                StartsAtUtc = a.StartsAtUtc,
                ExpiresAtUtc = a.ExpiresAtUtc,
                IsEffective = a.IsEffectiveAt(utcNow),
                CreatedAt = a.CreatedAt,
                CreatedBy = a.CreatedBy
            })
            .ToList();
    }
}
