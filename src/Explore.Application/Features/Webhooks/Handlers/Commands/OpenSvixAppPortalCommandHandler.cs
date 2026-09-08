using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class OpenSvixAppPortalCommandHandler(
    IWebhookProviderPortalService portalService,
    IWebhookAuditEventWriter auditWriter,
    IWebhookConsumerRepository consumerRepository)
    : IRequestHandler<OpenSvixAppPortalCommand, WebhookProviderPortalAccessCommandResponse>
{
    private const string ValidationFailure = "webhook_portal_validation_failed";

    public async Task<WebhookProviderPortalAccessCommandResponse> Handle(
        OpenSvixAppPortalCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return Failure(
                "Webhook provider portal access validation failed.",
                ValidationFailure,
                isRetryable: false,
                validationErrors);
        }

        var consumer = await consumerRepository.GetByIdForOwnerOperationAsync(
            request.ConsumerId,
            forUpdate: false,
            cancellationToken);
        if (consumer is null)
        {
            return Failure(
                "Webhook consumer was not found.",
                "webhook_consumer_not_found",
                isRetryable: false,
                ["Webhook consumer was not found."]);
        }

        var result = await portalService.CreateAccessAsync(
            new WebhookProviderPortalAccessInput(
                request.ConsumerId,
                request.SessionId,
                request.ExpiresInSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null),
            cancellationToken);

        if (!result.Succeeded)
        {
            var failureCategory = string.IsNullOrWhiteSpace(result.FailureCategory)
                ? "webhook_provider_portal_failed"
                : result.FailureCategory;

            await CreateAuditAsync(
                request,
                consumer,
                result,
                "provider_failure",
                failureCategory);

            return Failure(
                ResolveFailureMessage(failureCategory),
                failureCategory,
                result.IsRetryable,
                [result.SafeDetail ?? failureCategory]);
        }

        await CreateAuditAsync(request, consumer, result, "issued", failureCategory: null);

        var access = new WebhookProviderPortalAccessDto
        {
            Url = result.Url!,
            Token = result.Token,
            ExpiresAt = result.ExpiresAt!.Value
        };

        return WebhookProviderPortalAccessCommandResponse.Success(
            access,
            "Webhook provider portal access created.",
            isRetryable: false);
    }

    private async Task CreateAuditAsync(
        OpenSvixAppPortalCommand request,
        WebhookConsumer consumer,
        WebhookProviderPortalAccessResult result,
        string outcome,
        string? failureCategory)
    {
        await auditWriter.AppendAsync(
            new WebhookAuditWriteRequest(
                consumer.TenantId,
                result.Succeeded
                    ? WebhookAuditAction.PortalAccessIssued
                    : WebhookAuditAction.PortalAccessRejected,
                WebhookAuditTargetKind.Consumer,
                request.ConsumerId,
                failureCategory ?? "portal_access_requested",
                result.Succeeded ? WebhookAuditOutcome.Succeeded : WebhookAuditOutcome.Failed,
                SafeAfterJson: JsonSerializer.Serialize(new
                {
                    consumerId = request.ConsumerId,
                    providerBindingId = result.ProviderBindingId,
                    provider = "svix",
                    capabilityPolicyVersion = result.CapabilityPolicyVersion,
                    result = outcome,
                    failureCategory
                }),
                ConfigurationVersion: result.CapabilityPolicyVersion,
                EffectiveScopeKind: consumer.Ownership.AuditScopeKind,
                EffectiveScopeId: consumer.OwnerId),
            CancellationToken.None);
    }

    private static List<string> Validate(OpenSvixAppPortalCommand request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            errors.Add("SessionId is required.");
        }

        if (request.ConsumerId == Guid.Empty)
        {
            errors.Add("ConsumerId is required.");
        }

        if (request.ExpiresInSeconds is <= 0)
        {
            errors.Add("ExpiresInSeconds must be greater than zero when provided.");
        }

        return errors;
    }

    private static WebhookProviderPortalAccessCommandResponse Failure(
        string message,
        string failureCode,
        bool isRetryable,
        List<string> errors)
    {
        var failure = BaseCommandResponse.Failure<WebhookProviderPortalAccessDto>(
            failureCode,
            message,
            errors);
        return WebhookProviderPortalAccessCommandResponse.Failure(failure, isRetryable);
    }

    private static string ResolveFailureMessage(string failureCategory) => failureCategory switch
    {
        "svix_provider_not_enabled" => "Svix webhook provider is not enabled.",
        "svix_app_portal_disabled" => "Svix App Portal is disabled.",
        "webhook_consumer_not_found" => "Webhook consumer was not found.",
        "webhook_consumer_disabled" => "Webhook consumer is disabled.",
        "webhook_provider_binding_unverified" => "Webhook provider binding is not verified.",
        "webhook_provider_binding_mismatched" => "Webhook provider binding does not match this tenant and consumer.",
        "webhook_provider_capability_unavailable" => "Webhook provider portal capability is unavailable.",
        "webhook_portal_session_required" => "Webhook provider portal session id is required.",
        "svix_auth_token_secret_missing" => "Svix auth token secret reference is not configured.",
        "svix_auth_token_unresolved" => "Svix auth token secret could not be resolved.",
        "svix_auth_failed" => "Svix rejected the configured credentials.",
        "svix_request_rejected" => "Svix rejected the App Portal request.",
        "svix_provider_unavailable" => "Svix is temporarily unavailable.",
        _ => "Webhook provider portal access could not be created."
    };
}
