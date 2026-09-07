using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class UpdateEventTicketCatalogCommercialDisclosuresCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    ITenantContext tenant) : IRequestHandler<UpdateEventTicketCatalogCommercialDisclosuresCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventTicketCatalogCommercialDisclosuresCommand request, CancellationToken cancellationToken)
    {
        var validation = await new UpdateEventTicketCatalogCommercialDisclosuresCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(request.EventId, "event_ticketing_validation_failed", validation.Errors[0].ErrorMessage);
        }

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (eventTarget?.TenantId != tenant.TenantId
            || eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId != (int)ParticipationHandlingModeEnum.PlatformManaged)
        {
            return Failure(request.EventId, "event_ticketing_not_found", "Ticketing configuration was not found.");
        }

        EventTicketCatalogVersion? draft = await catalogs.GetDraftCatalogForUpdateAsync(request.EventId, tenant.TenantId, cancellationToken);
        if (draft is null)
        {
            return Failure(request.EventId, "event_ticketing_not_found", "Ticketing configuration was not found.");
        }

        try
        {
            draft.UpdateCommercialDisclosures(
                request.MerchantDisclosureText,
                request.RefundPolicyDisclosureText,
                request.SupportContactDisclosureText);
        }
        catch (ArgumentException exception)
        {
            return Failure(draft.Id, "event_ticketing_validation_failed", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(draft.Id, "event_ticketing_validation_failed", exception.Message);
        }

        await catalogs.SaveChangesAsync(cancellationToken);
        return BaseCommandResponse.Success(draft.Id, "Ticket catalog commercial disclosures updated.");
    }

    private static BaseCommandResponse<Guid> Failure(Guid id, string code, string message) =>
        BaseCommandResponse.Failure<Guid>(code, message, [message], id);
}

public sealed class UpdateEventTicketCatalogCommercialDisclosuresCommandValidator
    : AbstractValidator<UpdateEventTicketCatalogCommercialDisclosuresCommand>
{
    public UpdateEventTicketCatalogCommercialDisclosuresCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.MerchantDisclosureText).MaximumLength(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
        RuleFor(command => command.RefundPolicyDisclosureText).MaximumLength(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
        RuleFor(command => command.SupportContactDisclosureText).MaximumLength(EventTicketCatalogVersion.MaxCommercialDisclosureTextLength);
    }
}
