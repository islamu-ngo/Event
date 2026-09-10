
using Explore.Application.Services.Registration;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Queries;

public sealed record GetGuestRegistrationCancellationEligibilityQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<bool?>
{
    public override string ToString() => "GetGuestRegistrationCancellationEligibilityQuery { Redacted = true }";
}

public sealed class GetGuestRegistrationCancellationEligibilityQueryHandler(AnonymousCancellationService cancellation)
    : IRequestHandler<GetGuestRegistrationCancellationEligibilityQuery, bool?>
{
    public async Task<bool?> Handle(GetGuestRegistrationCancellationEligibilityQuery request, CancellationToken cancellationToken) =>
        await cancellation.ExecuteAsync(request.EventId, request.OrderId, request.CapabilityToken, false, cancellationToken) switch
        {
            AnonymousCancellationOutcome.InvalidAuthority => null,
            AnonymousCancellationOutcome.Eligible => true,
            _ => false
        };
}
