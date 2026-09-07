using Explore.Application.Responses;
using Explore.Domain;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

public sealed record ReserveTicketPurchaseCommand(
    Guid EventId,
    Guid OrderId,
    Guid PolicyVersionId,
    TicketPurchaseAccessMode AccessMode,
    Guid? RequestedPurchaserActorId,
    string OperationKey) : IRequest<BaseCommandResponse<Guid>>;

public sealed class ReserveTicketPurchaseCommandValidator :
    AbstractValidator<ReserveTicketPurchaseCommand>
{
    public ReserveTicketPurchaseCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.PolicyVersionId).NotEmpty();
        RuleFor(command => command.AccessMode).IsInEnum();
        RuleFor(command => command.OperationKey)
            .NotEmpty()
            .MaximumLength(128);
    }
}

public static class TicketPurchaseFailureCodes
{
    public const string InvalidRequest =
        "ticket_purchase_invalid";
    public const string AuthorityUnavailable =
        "ticket_purchase_authority_unavailable";
    public const string PolicyUnavailable =
        "ticket_purchase_policy_unavailable";
    public const string OrderUnavailable =
        "ticket_purchase_order_unavailable";
    public const string CeilingExceeded =
        "ticket_purchase_ceiling_exceeded";
    public const string OperationConflict =
        "ticket_purchase_operation_conflict";
    public const string Unavailable =
        "ticket_purchase_unavailable";
}
