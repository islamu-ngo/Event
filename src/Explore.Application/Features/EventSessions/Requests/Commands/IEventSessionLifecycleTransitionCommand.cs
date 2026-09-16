using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

public interface IEventSessionLifecycleTransitionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    Guid Id { get; set; }
    EventSessionLifecycleRequestDto Request { get; set; }
}
