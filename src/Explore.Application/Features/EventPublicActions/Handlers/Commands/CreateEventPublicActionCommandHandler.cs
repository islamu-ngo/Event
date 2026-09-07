using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventPublicAction.Validators;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class CreateEventPublicActionCommandHandler(
    IEventRepository eventRepository,
    IEventPublicActionRepository actionRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateEventPublicActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateEventPublicActionCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ManageEventPublicActionDtoValidator(requireConcurrencyStamp: false);
        var validation = await validator.ValidateAsync(request.Action, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure("Public action failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (currentUserService.UserId is null)
        {
            return Failure("Public action could not be created.", ["An authenticated user is required."]);
        }

        var @event = await eventRepository.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (@event is null || @event.TenantId != tenantContext.TenantId)
        {
            return Failure("Public action could not be created.", ["Event was not found in the current tenant."]);
        }

        if (@event.ParticipationConfiguration is null
            || !EventAuthorityRules.IsPublicActionAllowed(
                @event.ParticipationConfiguration.ParticipationHandlingModeId,
                request.Action.KindId))
        {
            return Failure(
                "Public action could not be created.",
                ["Public action kind is not available for this event's participation mode."]);
        }

        var action = new EventPublicAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantContext.TenantId,
            EventId = request.EventId,
            EventPublicActionKindId = request.Action.KindId,
            HealthStateId = (int)EventPublicActionHealthStateEnum.PendingReview,
            Label = NormalizeOptional(request.Action.Label),
            SortOrder = request.Action.SortOrder,
            IsPrimary = request.Action.IsPrimary
        };
        action.SetDestination(ExternalActionUrl.Create(request.Action.Url));

        async Task<BaseCommandResponse<Guid>> PersistAsync(CancellationToken ct)
        {
            if (request.Action.IsPrimary
                && await actionRepository.HasOtherPrimaryAsync(request.EventId, excludedActionId: null, ct))
            {
                return Failure("Public action could not be created.", ["The event already has a primary public action."]);
            }

            var created = await actionRepository.Create(action);
            return Success(created.Id, "Public action created pending review.");
        }

        return request.Action.IsPrimary
            ? await unitOfWork.ExecuteSerializableAsync(PersistAsync, cancellationToken)
            : await PersistAsync(cancellationToken);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
