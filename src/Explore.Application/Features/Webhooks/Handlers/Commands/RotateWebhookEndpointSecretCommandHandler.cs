using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Lookups;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class RotateWebhookEndpointSecretCommandHandler(
    IWebhookEndpointRepository endpointRepository,
    IWebhookConsumerRepository consumerRepository,
    IWebhookProviderPublicationRepository providerPublicationRepository,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    TimeProvider timeProvider)
    : IRequestHandler<RotateWebhookEndpointSecretCommand, BaseCommandResponse<Guid>>
{
    private const int DefaultPreviousSecretValidForSeconds = 86_400;
    private const int MaxPreviousSecretValidForSeconds = 2_592_000;

    public async Task<BaseCommandResponse<Guid>> Handle(
        RotateWebhookEndpointSecretCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(request, out var normalizedSecretRef, out var previousSecretValidForSeconds);
        if (validationErrors.Count > 0)
        {
            return Failure("webhook_endpoint_secret_validation_failed", validationErrors);
        }

        if (!TryResolveActor(out _, out _))
        {
            return Failure(
                "webhook_endpoint_configuration_actor_required",
                ["An authenticated operator identity is required."]);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var endpoint = await endpointRepository.GetByIdForOwnerOperationAsync(
                request.EndpointId,
                forUpdate: true,
                transactionCancellationToken);
            if (endpoint is null || endpoint.Status == WebhookEndpointStatus.Archived)
            {
                return Failure("webhook_endpoint_not_found", ["Webhook endpoint was not found."]);
            }

            if (endpoint.ConfigurationVersion != request.ExpectedConfigurationVersion)
            {
                return Failure(
                    "webhook_endpoint_configuration_conflict",
                    ["Webhook endpoint configuration changed. Reload it before rotating its signing credential."]);
            }

            var consumer = endpoint.Consumer ?? await consumerRepository.GetByIdForOwnerOperationAsync(
                endpoint.ConsumerId,
                forUpdate: false,
                transactionCancellationToken);
            if (consumer is null)
            {
                return Failure(
                    "webhook_endpoint_management_unavailable",
                    ["Webhook consumer was not found."]);
            }

            if (!WebhookEndpointCapabilityPolicy.CanManageLocalEndpoint(
                    capabilityResolver,
                    consumer.ProviderMode,
                    out var capabilityFailure))
            {
                return Failure(
                    "webhook_endpoint_management_unavailable",
                    [capabilityFailure]);
            }

            if (string.Equals(endpoint.SecretRef, normalizedSecretRef, StringComparison.Ordinal))
            {
                return Failure("webhook_endpoint_secret_unchanged", ["New secret reference must differ from the current secret reference."]);
            }

            var uncertainPublicationCount = await providerPublicationRepository.CountUncertainByConsumerAsync(
                consumer.TenantId,
                consumer.Id,
                transactionCancellationToken);
            if (uncertainPublicationCount > 0 && !request.AcknowledgeUncertainProviderPublications)
            {
                return Failure(
                    "webhook_endpoint_configuration_uncertain_publications",
                    [$"Acknowledge {uncertainPublicationCount} unknown or manual-reconciliation provider publication(s) before rotating configuration."]);
            }

            var eligibleTargets = await endpointRepository.GetEligiblePendingTargetsForUpdateAsync(
                endpoint.TenantId,
                endpoint.Id,
                transactionCancellationToken);
            var previousConfigurationVersion = endpoint.ConfigurationVersion;
            var previousCredentialVersion = endpoint.SecretVersion;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var previousSecretValidUntil = previousSecretValidForSeconds == 0
                ? now
                : now.AddSeconds(previousSecretValidForSeconds);
            endpoint.RotateSigningCredential(normalizedSecretRef, previousSecretValidUntil, now);

            var decision = (WebhookPendingWorkDecision)request.PendingWorkDecisionId;
            var migratedTargetCount = 0;
            if (decision == WebhookPendingWorkDecision.MigrateEligible)
            {
                foreach (var target in eligibleTargets.Where(target =>
                    target.EndpointConfigurationVersion < endpoint.ConfigurationVersion))
                {
                    target.MigratePendingConfiguration(endpoint, new DateTimeOffset(now));
                    migratedTargetCount++;
                }
            }

            var persisted = await endpointRepository.UpdateAsync(endpoint, transactionCancellationToken);
            var pendingWorkDecisionCode = NormalizedLookupMetadata
                .WebhookPendingWorkDecision(request.PendingWorkDecisionId).Code;
            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    endpoint.TenantId,
                    WebhookAuditAction.EndpointSecretRotated,
                    WebhookAuditTargetKind.Endpoint,
                    endpoint.Id,
                    $"pending_work_{pendingWorkDecisionCode.ToLowerInvariant()}",
                    WebhookAuditOutcome.Succeeded,
                    SafeBeforeJson: JsonSerializer.Serialize(new
                    {
                        configurationVersion = previousConfigurationVersion,
                        credentialVersion = previousCredentialVersion
                    }),
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        configurationVersion = endpoint.ConfigurationVersion,
                        credentialVersion = endpoint.SecretVersion,
                        credentialActivatedAt = endpoint.SecretActivatedAt,
                        previousCredentialValidUntil = endpoint.PreviousSecretValidUntil,
                        pendingWorkDecision = pendingWorkDecisionCode,
                        eligiblePendingTargetCount = eligibleTargets.Count,
                        migratedTargetCount,
                        uncertainProviderPublicationCount = uncertainPublicationCount,
                        uncertainProviderPublicationsAcknowledged = request.AcknowledgeUncertainProviderPublications,
                        outcome = "applied"
                    }),
                    ConfigurationVersion: $"endpoint-v{endpoint.ConfigurationVersion}",
                    EffectiveScopeKind: consumer.Ownership.AuditScopeKind,
                    EffectiveScopeId: consumer.OwnerId),
                transactionCancellationToken);

            if (migratedTargetCount > 0)
            {
                await auditWriter.AppendAsync(
                    new WebhookAuditWriteRequest(
                        endpoint.TenantId,
                        WebhookAuditAction.PendingWorkMigrated,
                        WebhookAuditTargetKind.Endpoint,
                        endpoint.Id,
                        "eligible_local_targets_migrated",
                        WebhookAuditOutcome.Succeeded,
                        SafeAfterJson: JsonSerializer.Serialize(new
                        {
                            pendingWorkDecision = pendingWorkDecisionCode,
                            migratedTargetCount,
                            endpointConfigurationVersion = endpoint.ConfigurationVersion
                        }),
                        ConfigurationVersion: $"endpoint-v{endpoint.ConfigurationVersion}",
                        EffectiveScopeKind: consumer.Ownership.AuditScopeKind,
                        EffectiveScopeId: consumer.OwnerId),
                    transactionCancellationToken);
            }

            var warning = uncertainPublicationCount > 0
                ? $" {uncertainPublicationCount} uncertain provider publication(s) remain on their original snapshots."
                : string.Empty;
            return BaseCommandResponse.Success(persisted.Id, $"Webhook endpoint signing credential rotated; {migratedTargetCount} eligible pending target(s) migrated.{warning}");
        }, cancellationToken);
    }

    private static List<string> Validate(
        RotateWebhookEndpointSecretCommand request,
        out string normalizedSecretRef,
        out int previousSecretValidForSeconds)
    {
        var errors = new List<string>();
        normalizedSecretRef = request.NewSecretRef.Trim();
        previousSecretValidForSeconds = request.PreviousSecretValidForSeconds ?? DefaultPreviousSecretValidForSeconds;

        if (request.EndpointId == Guid.Empty)
        {
            errors.Add("Endpoint id is required.");
        }

        if (string.IsNullOrWhiteSpace(normalizedSecretRef) || normalizedSecretRef.Length > 500)
        {
            errors.Add("New secret reference is required and must be 500 characters or fewer.");
        }

        if (request.PreviousSecretValidForSeconds is < 0 or > MaxPreviousSecretValidForSeconds)
        {
            errors.Add("Previous secret validity must be between 0 and 2592000 seconds.");
        }

        if (request.ExpectedConfigurationVersion < 1)
        {
            errors.Add("Expected configuration version must be positive.");
        }

        if (!Enum.IsDefined(typeof(WebhookPendingWorkDecision), request.PendingWorkDecisionId))
        {
            errors.Add("Pending work decision is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.PendingWorkReason) || request.PendingWorkReason.Trim().Length > 500)
        {
            errors.Add("Pending work reason is required and must be 500 characters or fewer.");
        }

        return errors;
    }

    private bool TryResolveActor(out string actorReference, out Guid? actorUserId)
    {
        if (currentUserService.UserId is { } userId)
        {
            actorReference = $"user:{userId:D}";
            actorUserId = userId;
            return true;
        }

        if (machinePrincipalAccessor.Current is { } machine)
        {
            actorReference = $"machine:{machine.OwnerType}:{machine.OwnerId:D}";
            actorUserId = null;
            return true;
        }

        actorReference = string.Empty;
        actorUserId = null;
        return false;
    }

    private static BaseCommandResponse<Guid> Failure(string code, IReadOnlyList<string> errors) =>
        BaseCommandResponse.Failure<Guid>(code, errors[0], errors);
}
