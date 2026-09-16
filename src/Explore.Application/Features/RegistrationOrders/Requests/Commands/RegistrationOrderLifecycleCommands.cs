using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

public interface IRegistrationOrderLifecycleCommand : ICommand<RegistrationOrderLifecycleResponseDto>
{
    Guid OrderId { get; }
}

public sealed record SubmitRegistrationOrderCommand(Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record ReadyRegistrationOrderForCheckoutCommand(Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record FinalizeFreeRegistrationOrderCommand(Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record CancelRegistrationOrderCommand(Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record ApproveRegistrationOrderCommand(Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;

public sealed record RejectRegistrationOrderCommand(Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IRegistrationOrderLifecycleCommand;
