
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;

namespace Explore.Application.Features.RegistrationOrders.Commands;

public sealed record CancelConfirmedGuestRegistrationCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : ICommand<BaseCommandResponse<Guid>>
{
    public override string ToString() => "CancelConfirmedGuestRegistrationCommand { Redacted = true }";
}

public sealed class CancelConfirmedGuestRegistrationCommandHandler(AnonymousCancellationService cancellation)
    : ICommandHandler<CancelConfirmedGuestRegistrationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CancelConfirmedGuestRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        var outcome = await cancellation.ExecuteAsync(command.EventId, command.OrderId, command.CapabilityToken, true, cancellationToken);
        return outcome switch
        {
            AnonymousCancellationOutcome.Cancelled => BaseCommandResponse.Success(command.OrderId),
            AnonymousCancellationOutcome.InvalidAuthority => BaseCommandResponse.Failure<Guid>(
                "registration_order_not_found", "Registration order was not found.", id: command.OrderId),
            _ => BaseCommandResponse.Failure<Guid>("guest_registration_cancellation_ineligible",
                "Registration is not eligible for guest cancellation.", id: command.OrderId)
        };
    }
}
