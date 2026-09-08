using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class RepairWebhookProviderBindingCommandHandler(
    IWebhookConsumerRepository consumerRepository,
    IWebhookConsumerProviderBindingRepository bindingRepository,
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    IWebhookProviderBindingAuthorityService authorityService,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    TimeProvider timeProvider)
    : IRequestHandler<RepairWebhookProviderBindingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        RepairWebhookProviderBindingCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new RepairWebhookProviderBindingCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.ConsumerId,
                "webhook_provider_binding_repair_validation_failed",
                "Webhook provider binding repair request failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        if (!HasAuthenticatedActor())
        {
            return Failure(
                request.ConsumerId,
                "webhook_provider_binding_repair_actor_required",
                "An authenticated operator identity is required.");
        }

        var consumer = await consumerRepository.GetByIdForOwnerOperationAsync(
            request.ConsumerId,
            forUpdate: false,
            cancellationToken);
        if (consumer is null)
        {
            return Failure(
                request.ConsumerId,
                "webhook_consumer_not_found",
                "Webhook consumer was not found.");
        }

        if (consumer.Status != WebhookConsumerStatus.Active ||
            consumer.ProviderMode is not (WebhookProviderMode.Svix or WebhookProviderMode.Composite))
        {
            return Failure(
                consumer.Id,
                "webhook_provider_binding_repair_not_available",
                "Webhook provider binding repair is not available for this consumer.");
        }

        var profileResult = authorityService.ResolveCurrentProfile();
        if (profileResult.Profile is not { } profile)
        {
            return Failure(
                consumer.Id,
                profileResult.FailureCategory ?? "webhook_provider_profile_unavailable",
                "The self-hosted webhook provider profile is unavailable.");
        }

        var bootstrap = await bootstrapStateRepository.GetCurrent(cancellationToken);
        if (bootstrap is not { Status: InstanceBootstrapStatus.Completed } || bootstrap.Id == Guid.Empty)
        {
            return Failure(
                consumer.Id,
                "webhook_instance_identity_unavailable",
                "The immutable instance identity is unavailable.");
        }

        var existing = await bindingRepository.GetByConsumerAsync(
            consumer.TenantId,
            request.ConsumerId,
            profile.ProviderKind,
            profile.ProviderEnvironment,
            cancellationToken);
        if (existing?.VerificationState is WebhookProviderBindingVerificationState.Rejected or
            WebhookProviderBindingVerificationState.Revoked)
        {
            return Failure(
                existing.Id,
                "webhook_provider_binding_repair_not_available",
                "Rejected or revoked provider bindings cannot be repaired.");
        }

        var externalApplicationId = request.ExternalApplicationId.Trim();
        var expectedApplicationUid = WebhookConsumerProviderBinding.CreateApplicationUid(
            bootstrap.Id,
            consumer.Id);
        var ownership = await authorityService.VerifyOwnershipAsync(
            new WebhookProviderBindingOwnershipRequest(
                consumer.Ownership,
                consumer.Id,
                expectedApplicationUid,
                externalApplicationId,
                profile.ProviderKind,
                profile.ProviderEnvironment,
                profile.CapabilityProfile.ProviderVersion,
                profile.CapabilityProfile.ResolutionVersion),
            cancellationToken);
        var occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        if (!ownership.Succeeded)
        {
            await WriteRejectedAuditAsync(
                request,
                consumer,
                existing,
                expectedApplicationUid,
                externalApplicationId,
                ownership.FailureCategory ?? "webhook_provider_binding_mismatched",
                profile.CapabilityProfile.ResolutionVersion,
                cancellationToken);
            return Failure(
                existing?.Id ?? consumer.Id,
                ownership.FailureCategory ?? "webhook_provider_binding_mismatched",
                "The provider application does not prove typed ownership by this consumer.");
        }

        var expectedBindingId = existing?.Id;
        var expectedConcurrencyVersion = existing?.ConcurrencyVersion;
        var expectedVerificationFence = existing?.VerificationFence;
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            WebhookConsumerProviderBinding binding;
            if (expectedBindingId is { } bindingId)
            {
                var tracked = await bindingRepository.GetByTenantAndIdForUpdateAsync(
                    consumer.TenantId,
                    bindingId,
                    token);
                if (tracked is null ||
                    tracked.WebhookConsumerId != consumer.Id ||
                    tracked.ConcurrencyVersion != expectedConcurrencyVersion ||
                    tracked.VerificationFence != expectedVerificationFence)
                {
                    return Failure(
                        bindingId,
                        "webhook_provider_binding_repair_conflict",
                        "Webhook provider binding changed before repair could be committed.");
                }

                binding = tracked;
            }
            else
            {
                var racedBinding = await bindingRepository.GetByConsumerAsync(
                    consumer.TenantId,
                    consumer.Id,
                    profile.ProviderKind,
                    profile.ProviderEnvironment,
                    token);
                if (racedBinding is not null)
                {
                    return Failure(
                        racedBinding.Id,
                        "webhook_provider_binding_repair_conflict",
                        "Webhook provider binding changed before repair could be committed.");
                }

                binding = WebhookConsumerProviderBinding.CreatePending(
                    consumer.TenantId,
                    consumer.Id,
                    bootstrap.Id,
                    profile.ProviderEnvironment,
                    profile.CapabilityProfile,
                    profile.GovernanceAllowedCapabilities);
            }

            var previousState = binding.VerificationState;
            var previousApplicationIdHash = HashIdentity(binding.ExternalApplicationId);
            binding.RepairAndVerifyOwnership(
                bootstrap.Id,
                consumer.TenantId,
                consumer.Id,
                externalApplicationId,
                profile.CapabilityProfile,
                profile.GovernanceAllowedCapabilities,
                occurredAt);

            if (expectedBindingId is null)
            {
                await bindingRepository.CreateAsync(binding, token);
            }
            else
            {
                await bindingRepository.SaveChangesAsync(token);
            }

            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    binding.TenantId,
                    WebhookAuditAction.ProviderBindingRepairSucceeded,
                    WebhookAuditTargetKind.ProviderBinding,
                    binding.Id,
                    request.ReasonCode,
                    WebhookAuditOutcome.Succeeded,
                    SafeBeforeJson: JsonSerializer.Serialize(new
                    {
                        verificationState = previousState.ToString(),
                        externalApplicationIdHash = previousApplicationIdHash
                    }),
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        consumerId = binding.WebhookConsumerId,
                        binding.ApplicationUid,
                        externalApplicationIdHash = HashIdentity(binding.ExternalApplicationId),
                        verificationState = binding.VerificationState.ToString(),
                        binding.ProviderVersion,
                        binding.CapabilityResolutionVersion,
                        binding.ConcurrencyVersion,
                        binding.VerificationFence
                    }),
                    ConfigurationVersion: $"binding-v{binding.ConcurrencyVersion}:fence-{binding.VerificationFence}",
                    EffectiveScopeKind: consumer.Ownership.AuditScopeKind,
                    EffectiveScopeId: consumer.OwnerId),
                token);

            return Success(binding.Id, "Webhook provider binding verified.");
        }, cancellationToken);
    }

    private async Task WriteRejectedAuditAsync(
        RepairWebhookProviderBindingCommand request,
        WebhookConsumer consumer,
        WebhookConsumerProviderBinding? binding,
        string expectedApplicationUid,
        string externalApplicationId,
        string failureCategory,
        string capabilityResolutionVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await auditWriter.AppendAsync(
            new WebhookAuditWriteRequest(
                consumer.TenantId,
                WebhookAuditAction.ProviderBindingRepairRejected,
                binding is null
                    ? WebhookAuditTargetKind.Consumer
                    : WebhookAuditTargetKind.ProviderBinding,
                binding?.Id ?? consumer.Id,
                request.ReasonCode,
                WebhookAuditOutcome.Rejected,
                SafeAfterJson: JsonSerializer.Serialize(new
                {
                    consumerId = consumer.Id,
                    providerBindingId = binding?.Id,
                    expectedApplicationUid,
                    requestedApplicationIdHash = HashIdentity(externalApplicationId),
                    failureCategory
                }),
                ConfigurationVersion: capabilityResolutionVersion,
                EffectiveScopeKind: consumer.Ownership.AuditScopeKind,
                EffectiveScopeId: consumer.OwnerId),
            cancellationToken);
    }

    private bool HasAuthenticatedActor()
    {
        if (currentUserService.UserId is not null)
        {
            return true;
        }

        if (machinePrincipalAccessor.Current is not null)
        {
            return true;
        }

        return false;
    }

    private static string? HashIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string code,
        string message,
        IEnumerable<string>? errors = null) =>
        BaseCommandResponse.Failure(code, message, errors ?? [message], id);
}
