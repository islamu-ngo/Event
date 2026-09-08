using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class ReserveTicketPurchaseCommandHandler(
    ITicketPurchaseAuthorityResolver authorityResolver,
    ITicketPurchaseOrderResolver orders,
    ITicketPurchaseGovernanceRepository governance,
    ITenantContext tenant) :
    IRequestHandler<
        ReserveTicketPurchaseCommand,
        BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(
        ReserveTicketPurchaseCommand request,
        CancellationToken cancellationToken) =>
        HandleCoreAsync(request, cancellationToken);

    private async Task<BaseCommandResponse<Guid>> HandleCoreAsync(
        ReserveTicketPurchaseCommand request,
        CancellationToken cancellationToken)
    {
        var validation =
            await new ReserveTicketPurchaseCommandValidator()
                .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.OrderId,
                TicketPurchaseFailureCodes.InvalidRequest);
        }

        TicketPurchaseOrderSnapshot? order =
            await orders.ResolveAsync(
                tenant.TenantId,
                request.EventId,
                request.OrderId,
                cancellationToken);
        if (order is null)
        {
            return Failure(
                request.OrderId,
                TicketPurchaseFailureCodes.OrderUnavailable);
        }

        TicketPurchaseAuthorityResolution resolution =
            await authorityResolver.ResolveAsync(
                new TicketPurchaseAuthorityResolutionRequest(
                    request.EventId,
                    request.OrderId,
                    request.AccessMode,
                    request.RequestedPurchaserActorId),
                cancellationToken);
        if (!resolution.IsSuccess)
        {
            return Failure(
                request.OrderId,
                resolution.FailureCode!);
        }

        TicketPurchasePolicyVersion? policy =
            await governance.GetPolicyVersionAsync(
                tenant.TenantId,
                request.EventId,
                request.PolicyVersionId,
                cancellationToken);
        if (policy is null)
        {
            return Failure(
                request.OrderId,
                TicketPurchaseFailureCodes.PolicyUnavailable);
        }

        TicketPurchaseAuthorityDimension authority =
            resolution.Authority!;
        TicketPurchaseOperationIdentity operation =
            CreateOperationIdentity(
                tenant.TenantId,
                request,
                order.Quantity,
                authority);
        TicketPurchaseReservationResult reservation =
            await governance.ReserveAsync(
                policy,
                new TicketPurchaseReservationRequest(
                    tenant.TenantId,
                    request.EventId,
                    request.OrderId,
                    order.Quantity,
                    authority,
                    operation),
                cancellationToken);
        return reservation.Disposition switch
        {
            TicketPurchaseReservationDisposition.Reserved
                or TicketPurchaseReservationDisposition.Replay =>
                BaseCommandResponse.Success(request.OrderId),
            TicketPurchaseReservationDisposition.CeilingExceeded =>
                Failure(
                    request.OrderId,
                    TicketPurchaseFailureCodes.CeilingExceeded),
            TicketPurchaseReservationDisposition.OperationConflict =>
                Failure(
                    request.OrderId,
                    TicketPurchaseFailureCodes.OperationConflict),
            _ => Failure(
                request.OrderId,
                TicketPurchaseFailureCodes.Unavailable),
        };
    }

    private static TicketPurchaseOperationIdentity
        CreateOperationIdentity(
            Guid tenantId,
            ReserveTicketPurchaseCommand request,
            int quantity,
            TicketPurchaseAuthorityDimension authority)
    {
        string fingerprint = string.Join(
            '|',
            "reserve-ticket-purchase-v1",
            tenantId.ToString("N"),
            request.EventId.ToString("N"),
            request.OrderId.ToString("N"),
            request.PolicyVersionId.ToString("N"),
            quantity.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ((int)request.AccessMode).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            authority.EnforcementKey,
            authority.PurchaserActorId?.ToString("N") ?? "-");
        return TicketPurchaseOperationIdentity.Create(
            Hash(request.OperationKey),
            Hash(fingerprint));
    }

    private static string Hash(string value) =>
        Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static BaseCommandResponse<Guid> Failure(
        Guid orderId,
        string failureCode) =>
        BaseCommandResponse.Failure<Guid>(
            failureCode,
            id: orderId);
}
