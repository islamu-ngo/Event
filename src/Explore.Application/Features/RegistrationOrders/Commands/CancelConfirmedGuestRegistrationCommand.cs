
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Commands;

public sealed record CancelConfirmedGuestRegistrationCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<BaseCommandResponse<Guid>>
{
    public override string ToString() => "CancelConfirmedGuestRegistrationCommand { Redacted = true }";
}

public sealed class CancelConfirmedGuestRegistrationCommandHandler(AnonymousCancellationService cancellation)
    : IRequestHandler<CancelConfirmedGuestRegistrationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CancelConfirmedGuestRegistrationCommand request, CancellationToken cancellationToken)
    {
        var outcome = await cancellation.ExecuteAsync(request.EventId, request.OrderId, request.CapabilityToken, true, cancellationToken);
        return outcome switch
        {
            AnonymousCancellationOutcome.Cancelled => BaseCommandResponse.Success(request.OrderId),
            AnonymousCancellationOutcome.InvalidAuthority => BaseCommandResponse.Failure<Guid>(
                "registration_order_not_found", "Registration order was not found.", id: request.OrderId),
            _ => BaseCommandResponse.Failure<Guid>("guest_registration_cancellation_ineligible",
                "Registration is not eligible for guest cancellation.", id: request.OrderId)
        };
    }
}
