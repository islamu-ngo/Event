using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using FluentValidation;
using Explore.Application.Features.Settings;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class UpdateTenantReportingIntakePolicyCommandHandler(
    ITenantContext tenantContext,
    IHumanSettingsMutationPreparation preparation,
    ITypedSettingsOriginalStore originalStore,
    IHierarchicalSettingsResolver settingsResolver,
    IEnumerable<Contracts.Operations.INotificationHandler<SettingChangedNotification>> notificationHandlers)
    : ICommandHandler<UpdateTenantReportingIntakePolicyCommand, BaseCommandResponse<Guid>>
{
    private const string TenantContextMismatchCode = "tenant_context_mismatch";

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
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

        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        PublicationPolicyMutationResult mutation = await preparation.ExecutePreparedAsync(prepared,
            (_, token) => originalStore.ApplyReportingAsync(preparation.CurrentTypedOriginal
                ?? throw new UnauthorizedAccessException("The original reporting owner is required."), token),
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
                await notificationHandlers.HandleAsync(notification, CancellationToken.None);
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
