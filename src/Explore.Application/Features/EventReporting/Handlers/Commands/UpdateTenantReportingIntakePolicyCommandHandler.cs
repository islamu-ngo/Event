using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class UpdateTenantReportingIntakePolicyCommandHandler(
    ITenantContext tenantContext,
    IPublicationPolicyMutationBoundary mutationBoundary,
    IUnitOfWork unitOfWork,
    IHierarchicalSettingsResolver settingsResolver,
    IMediator mediator)
    : IRequestHandler<UpdateTenantReportingIntakePolicyCommand, BaseCommandResponse<Guid>>
{
    private const string TenantContextMismatchCode = "tenant_context_mismatch";

    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateTenantReportingIntakePolicyCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new UpdateTenantReportingIntakePolicyCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.TenantId,
                PublicationPolicyMutationFailureCodes.InvalidPolicy,
                "The reporting-intake policy request is invalid.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        if (request.TenantId != tenantContext.TenantId)
        {
            return Failure(
                request.TenantId,
                TenantContextMismatchCode,
                "The reporting-intake policy tenant does not match the current tenant.",
                [TenantContextMismatchCode]);
        }

        DateTime occurredAtUtc = DateTime.UtcNow;
        PublicationPolicyMutationResult mutation = await unitOfWork.ExecuteInTransactionAsync(
            token => mutationBoundary.ApplyTenantAsync(
                new PublicationPolicyTenantMutationRequest(
                    request.TenantId,
                    request.ActorUserId,
                    occurredAtUtc,
                    [new PublicationPolicySettingMutation(
                        GovernanceSettingKeys.EventReporting.IntakeEnabled,
                        PublicationPolicyMutationKind.Set,
                        SettingValueSerializer.Serialize(request.Policy.Enabled),
                        request.TenantId,
                        IsLocked: null)],
                    PublicationPolicyLockedSystemBehavior.Reject),
                token),
            cancellationToken);

        if (!mutation.Success)
        {
            string failureCode = string.IsNullOrWhiteSpace(mutation.FailureCode)
                ? PublicationPolicyMutationFailureCodes.InvalidPolicy
                : mutation.FailureCode;
            return Failure(
                request.TenantId,
                failureCode,
                mutation.Message,
                [failureCode]);
        }

        if (mutation.DeferredNotifications.Length > 0)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, request.TenantId);
            foreach (SettingChangedNotification notification in mutation.DeferredNotifications)
            {
                await mediator.Publish(notification, CancellationToken.None);
            }
        }

        return BaseCommandResponse.Success(
            request.TenantId,
            "The reporting-intake policy was updated.");
    }

    private static BaseCommandResponse<Guid> Failure(
        Guid tenantId,
        string failureCode,
        string message,
        IEnumerable<string> errors)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? PublicationPolicyMutationMessages.InvalidPolicy
            : message;
        string[] normalizedErrors = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .ToArray();
        if (normalizedErrors.Length == 0)
        {
            normalizedErrors = [failureCode];
        }

        return BaseCommandResponse.Failure<Guid>(
            failureCode, normalizedMessage, normalizedErrors, tenantId);
    }
}

public sealed class UpdateTenantReportingIntakePolicyCommandValidator
    : AbstractValidator<UpdateTenantReportingIntakePolicyCommand>
{
    public UpdateTenantReportingIntakePolicyCommandValidator()
    {
        RuleFor(request => request.TenantId).NotEmpty();
        RuleFor(request => request.ActorUserId).NotEmpty();
        RuleFor(request => request.Policy).NotNull();
    }
}
