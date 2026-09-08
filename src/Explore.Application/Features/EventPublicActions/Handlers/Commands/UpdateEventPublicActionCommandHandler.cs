using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventPublicAction.Validators;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class UpdateEventPublicActionCommandHandler(
    IEventRepository eventRepository,
    IEventPublicActionRepository actionRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateEventPublicActionCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ManageEventPublicActionDtoValidator(requireConcurrencyStamp: true);
        var validation = await validator.ValidateAsync(request.Action, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(request.ActionId, "Public action failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (currentUserService.UserId is null)
        {
            return Failure(request.ActionId, "Public action could not be updated.", ["An authenticated user is required."]);
        }

        var action = await actionRepository.GetForUpdateAsync(request.ActionId, cancellationToken);
        if (action is null || action.EventId != request.EventId || action.TenantId != tenantContext.TenantId)
        {
            return Failure(request.ActionId, "Public action could not be updated.", ["Public action was not found for this event."]);
        }

        var @event = await eventRepository.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (@event is null || @event.TenantId != tenantContext.TenantId)
        {
            return Failure(request.ActionId, "Public action could not be updated.", ["Event was not found in the current tenant."]);
        }

        if (@event.ParticipationConfiguration is null
            || !EventAuthorityRules.IsPublicActionAllowed(
                @event.ParticipationConfiguration.ParticipationHandlingModeId,
                request.Action.KindId))
        {
            return Failure(
                request.ActionId,
                "Public action could not be updated.",
                ["Public action kind is not available for this event's participation mode."]);
        }

        if (action.ConcurrencyStamp != request.Action.ExpectedConcurrencyStamp)
        {
            return Failure(request.ActionId, "Public action could not be updated.", ["Public action changed since it was loaded."]);
        }

        async Task<BaseCommandResponse<Guid>> PersistAsync(CancellationToken ct)
        {
            if (request.Action.IsPrimary
                && await actionRepository.HasOtherPrimaryAsync(request.EventId, request.ActionId, ct))
            {
                return Failure(request.ActionId, "Public action could not be updated.", ["The event already has a primary public action."]);
            }

            action.EventPublicActionKindId = request.Action.KindId;
            action.HealthStateId = (int)EventPublicActionHealthStateEnum.PendingReview;
            action.Label = NormalizeOptional(request.Action.Label);
            action.SortOrder = request.Action.SortOrder;
            action.IsPrimary = request.Action.IsPrimary;
            action.SetDestination(ExternalActionUrl.Create(request.Action.Url));

            await actionRepository.Update(action);
            return Success(action.Id, "Public action updated pending review.");
        }

        return request.Action.IsPrimary
            ? await unitOfWork.ExecuteSerializableAsync(PersistAsync, cancellationToken)
            : await PersistAsync(cancellationToken);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation(errors, message, id);
}
