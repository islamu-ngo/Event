
using Explore.Application.Contracts.Operations;
using Explore.Application.Services.Registration;

namespace Explore.Application.Features.RegistrationOrders.Queries;

public sealed record GetGuestRegistrationCancellationEligibilityQuery(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IQuery<bool?>
{
    public override string ToString() => "GetGuestRegistrationCancellationEligibilityQuery { Redacted = true }";
}

public sealed class GetGuestRegistrationCancellationEligibilityQueryHandler(AnonymousCancellationService cancellation)
    : IQueryHandler<GetGuestRegistrationCancellationEligibilityQuery, bool?>
{
    public async Task<bool?> QueryAsync(GetGuestRegistrationCancellationEligibilityQuery query, CancellationToken cancellationToken = default) =>
        await cancellation.ExecuteAsync(query.EventId, query.OrderId, query.CapabilityToken, false, cancellationToken) switch
        {
            AnonymousCancellationOutcome.InvalidAuthority => null,
            AnonymousCancellationOutcome.Eligible => true,
            _ => false
        };
}
