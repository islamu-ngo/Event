using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Responses;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Services.Registration;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class SubmitRegistrationOrderCommandHandler(
    RegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant)
    : ICommandHandler<SubmitRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> ExecuteAsync(SubmitRegistrationOrderCommand request, CancellationToken cancellationToken)
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
    : ICommandHandler<ReadyRegistrationOrderForCheckoutCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> ExecuteAsync(ReadyRegistrationOrderForCheckoutCommand request, CancellationToken cancellationToken)
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
    : ICommandHandler<FinalizeFreeRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> ExecuteAsync(FinalizeFreeRegistrationOrderCommand request, CancellationToken cancellationToken)
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
    : ICommandHandler<CancelRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> ExecuteAsync(CancelRegistrationOrderCommand request, CancellationToken cancellationToken)
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
    : ICommandHandler<ApproveRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> ExecuteAsync(ApproveRegistrationOrderCommand request, CancellationToken cancellationToken)
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
    : ICommandHandler<RejectRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> ExecuteAsync(RejectRegistrationOrderCommand request, CancellationToken cancellationToken)
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
