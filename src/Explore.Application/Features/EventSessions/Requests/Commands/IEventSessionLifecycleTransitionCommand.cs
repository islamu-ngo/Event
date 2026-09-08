using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

public interface IEventSessionLifecycleTransitionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    Guid Id { get; set; }
    EventSessionLifecycleRequestDto Request { get; set; }
}
