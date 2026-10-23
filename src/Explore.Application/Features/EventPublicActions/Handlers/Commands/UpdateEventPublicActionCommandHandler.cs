using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventPublicAction.Validators;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;
using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class UpdateEventPublicActionCommandHandler(
    IEventPublicActionRepository actionRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    RequestAuthorization<UpdateEventPublicActionCommand> authorization,
    ILogger<UpdateEventPublicActionCommandHandler> logger)
    : ICommandHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateEventPublicActionCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ManageEventPublicActionDtoValidator(requireConcurrencyStamp: true);
        var validation = await validator.ValidateAsync(request.Action, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(request.ActionId, "Public action failed validation.", validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure(request.ActionId, "Public action could not be updated.", ["An authenticated user is required."]);
        }

        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var @event = await actionRepository.GetAuthorizedEventForMutationAsync(
                tenantContext.TenantId, userId, request.EventId, token);
            if (@event is null)
                return BaseCommandResponse.Authorization<Guid>();

            var action = await actionRepository.GetForUpdateAsync(request.ActionId, token);
            if (action is null || action.EventId != request.EventId || action.TenantId != tenantContext.TenantId)
            {
                return Failure(request.ActionId, "Public action could not be updated.", ["Public action was not found for this event."]);
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

            if (request.Action.IsPrimary
                && await actionRepository.HasOtherPrimaryAsync(request.EventId, request.ActionId, token))
            {
                return Failure(request.ActionId, "Public action could not be updated.", ["The event already has a primary public action."]);
            }

            action.EventPublicActionKindId = request.Action.KindId;
            action.HealthStateId = (int)EventPublicActionHealthStateEnum.PendingReview;
            action.Label = NormalizeOptional(request.Action.Label);
            action.SortOrder = request.Action.SortOrder;
            action.IsPrimary = request.Action.IsPrimary;
            action.SetDestination(ExternalActionUrl.Create(request.Action.Url));

            token.ThrowIfCancellationRequested();
            await actionRepository.Update(action);
            token.ThrowIfCancellationRequested();
            return Success(action.Id, "Public action updated pending review.");
        }, cancellationToken);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation(errors, message, id);
}
