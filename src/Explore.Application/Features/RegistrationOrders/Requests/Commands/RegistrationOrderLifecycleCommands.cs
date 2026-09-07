using Explore.Application.DTOs.RegistrationOrders;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

public interface IRegistrationOrderLifecycleCommand
{
    Guid OrderId { get; }
}

public sealed record SubmitRegistrationOrderCommand(Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record ReadyRegistrationOrderForCheckoutCommand(Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record FinalizeFreeRegistrationOrderCommand(Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record CancelRegistrationOrderCommand(Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record ApproveRegistrationOrderCommand(Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record RejectRegistrationOrderCommand(Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;
