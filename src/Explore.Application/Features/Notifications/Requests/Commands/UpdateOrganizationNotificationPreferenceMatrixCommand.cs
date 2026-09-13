using Explore.Application.Authorization;
using Explore.Application.DTOs.Notification;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

[AuthorizeResource(ResourceKinds.Organization, AuthorizationActions.Update)]
public sealed record UpdateOrganizationNotificationPreferenceMatrixCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid OrganizationId { get; init; }
    private IReadOnlyList<UpdateNotificationPreferenceCellDto>? _cells;

    public IReadOnlyList<UpdateNotificationPreferenceCellDto>? Cells
    {
        get => _cells;
        init => _cells = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    string? ISecureRequest.ResourceId => OrganizationId.ToString();
}
