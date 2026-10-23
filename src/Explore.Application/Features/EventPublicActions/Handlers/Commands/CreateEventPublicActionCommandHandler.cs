using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventPublicAction.Validators;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;
using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class CreateEventPublicActionCommandHandler(
    IEventPublicActionRepository actionRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    RequestAuthorization<CreateEventPublicActionCommand> authorization,
    ILogger<CreateEventPublicActionCommandHandler> logger)
    : ICommandHandler<CreateEventPublicActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        CreateEventPublicActionCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ManageEventPublicActionDtoValidator(requireConcurrencyStamp: false);
        var validation = await validator.ValidateAsync(request.Action, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure("Public action failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure("Public action could not be created.", ["An authenticated user is required."]);
        }

        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var @event = await actionRepository.GetAuthorizedEventForMutationAsync(
                tenantContext.TenantId, userId, request.EventId, token);
            if (@event is null)
                return BaseCommandResponse.Authorization<Guid>();

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

            if (request.Action.IsPrimary
                && await actionRepository.HasOtherPrimaryAsync(request.EventId, excludedActionId: null, token))
            {
                return Failure("Public action could not be created.", ["The event already has a primary public action."]);
            }

            token.ThrowIfCancellationRequested();
            var created = await actionRepository.Create(action);
            token.ThrowIfCancellationRequested();
            return Success(created.Id, "Public action created pending review.");
        }, cancellationToken);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
