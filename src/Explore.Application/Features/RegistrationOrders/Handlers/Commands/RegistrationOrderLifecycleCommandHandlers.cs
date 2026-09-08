using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Responses;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Services.Registration;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class SubmitRegistrationOrderCommandHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<SubmitRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(SubmitRegistrationOrderCommand request, CancellationToken cancellationToken)
    {
        var validator = new RegistrationOrderLifecycleCommandValidator<SubmitRegistrationOrderCommand>();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        RegistrationOrderLifecycleResponseDto? failure = RegistrationOrderLifecycleCommandFailures.Failure(request, validation);
        return failure ?? await lifecycle.SubmitAsync(request.OrderId, tenant.TenantId, cancellationToken);
    }
}

public sealed class ReadyRegistrationOrderForCheckoutCommandHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<ReadyRegistrationOrderForCheckoutCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(ReadyRegistrationOrderForCheckoutCommand request, CancellationToken cancellationToken)
    {
        var validator = new RegistrationOrderLifecycleCommandValidator<ReadyRegistrationOrderForCheckoutCommand>();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        RegistrationOrderLifecycleResponseDto? failure = RegistrationOrderLifecycleCommandFailures.Failure(request, validation);
        return failure ?? await lifecycle.ReadyForCheckoutAsync(request.OrderId, tenant.TenantId, cancellationToken);
    }
}

public sealed class FinalizeFreeRegistrationOrderCommandHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<FinalizeFreeRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(FinalizeFreeRegistrationOrderCommand request, CancellationToken cancellationToken)
    {
        var validator = new RegistrationOrderLifecycleCommandValidator<FinalizeFreeRegistrationOrderCommand>();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        RegistrationOrderLifecycleResponseDto? failure = RegistrationOrderLifecycleCommandFailures.Failure(request, validation);
        return failure ?? await lifecycle.FinalizeFreeAsync(request.OrderId, tenant.TenantId, cancellationToken);
    }
}

public sealed class CancelRegistrationOrderCommandHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<CancelRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(CancelRegistrationOrderCommand request, CancellationToken cancellationToken)
    {
        var validator = new RegistrationOrderLifecycleCommandValidator<CancelRegistrationOrderCommand>();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        RegistrationOrderLifecycleResponseDto? failure = RegistrationOrderLifecycleCommandFailures.Failure(request, validation);
        return failure ?? await lifecycle.CancelAsync(request.OrderId, tenant.TenantId, cancellationToken);
    }
}

public sealed class ApproveRegistrationOrderCommandHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<ApproveRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(ApproveRegistrationOrderCommand request, CancellationToken cancellationToken)
    {
        var validator = new RegistrationOrderLifecycleCommandValidator<ApproveRegistrationOrderCommand>();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        RegistrationOrderLifecycleResponseDto? failure = RegistrationOrderLifecycleCommandFailures.Failure(request, validation);
        return failure ?? await lifecycle.ApproveAsync(request.OrderId, tenant.TenantId, cancellationToken);
    }
}

public sealed class RejectRegistrationOrderCommandHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : IRequestHandler<RejectRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(RejectRegistrationOrderCommand request, CancellationToken cancellationToken)
    {
        var validator = new RegistrationOrderLifecycleCommandValidator<RejectRegistrationOrderCommand>();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        RegistrationOrderLifecycleResponseDto? failure = RegistrationOrderLifecycleCommandFailures.Failure(request, validation);
        return failure ?? await lifecycle.RejectAsync(request.OrderId, tenant.TenantId, cancellationToken);
    }
}

file static class RegistrationOrderLifecycleCommandFailures
{
    public static RegistrationOrderLifecycleResponseDto? Failure<TCommand>(
        TCommand command,
        FluentValidation.Results.ValidationResult validation)
        where TCommand : IRegistrationOrderLifecycleCommand
    {
        return validation.IsValid
            ? null
            : RegistrationOrderLifecycleResponseDto.Failure(BaseCommandResponse.Validation(
                validation.Errors.Select(error => error.ErrorMessage),
                "Registration order lifecycle request is invalid.",
                command.OrderId));
    }
}
