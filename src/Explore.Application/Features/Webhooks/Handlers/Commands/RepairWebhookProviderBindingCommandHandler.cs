using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class RepairWebhookProviderBindingCommandHandler(
    IWebhookConsumerRepository consumerRepository,
    IWebhookConsumerProviderBindingRepository bindingRepository,
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    IWebhookProviderBindingAuthorityService authorityService,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    TimeProvider timeProvider,
    IHumanWebhookMutationPreparation preparation,
    IOriginalOwnerSettlementBoundary settlementBoundary,
    IHumanActionCommitFinalizer finalizer)
    : ICommandHandler<RepairWebhookProviderBindingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
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

        var prepared = machinePrincipalAccessor.Current is null
            ? await preparation.PrepareRepairAsync(request, cancellationToken)
            : null;

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

        if (prepared is not null)
            RequireConsumer(prepared.Consumer, consumer);

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
        var originalBinding = existing is null ? null : BindingFact(existing);
        if (prepared is not null)
        {
            var selected = prepared.Bindings.SingleOrDefault(row =>
                row.ProviderKindId == (int)profile.ProviderKind &&
                row.NormalizedEnvironment == WebhookConsumerProviderBinding.NormalizeIdentity(
                    profile.ProviderEnvironment, nameof(profile.ProviderEnvironment)));
            if (selected != originalBinding)
                throw new UnauthorizedAccessException("The original repair binding observation changed.");
        }
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
        var originalBootstrap = new HumanWebhookInstanceFact(
            bootstrap.Id, (int)bootstrap.Status, bootstrap.Generation, bootstrap.CreatedAt);
        if (prepared is not null)
        {
            RequireBootstrap(originalBootstrap, await bootstrapStateRepository.GetCurrent(cancellationToken));
            RequireProfile(profile);
            await prepared.ValidateAsync(request, cancellationToken);
        }
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
        var occurredAt = timeProvider.GetUtcNow();
        if (prepared is not null)
        {
            await prepared.ValidateAsync(request, cancellationToken);
            RequireProfile(profile);
            RequireBootstrap(originalBootstrap, await bootstrapStateRepository.GetCurrent(cancellationToken));
        }
        if (!ownership.Succeeded && prepared is null)
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
        // Only the authority service's two fixed ownership-business classifications can
        // enter the evidence owner. Retryability, arbitrary provider errors and exceptions
        // are neither original-human admission nor physical rollback acknowledgement.
        if (!ownership.Succeeded && (ownership.IsRetryable || ownership.FailureCategory is not
            ("webhook_provider_binding_mismatched" or "webhook_provider_application_not_found")))
            return Failure(existing?.Id ?? consumer.Id,
                ownership.FailureCategory ?? "webhook_provider_binding_mismatched",
                "The provider application ownership could not be verified.");

        var expectedBindingId = existing?.Id;
        var expectedConcurrencyVersion = existing?.ConcurrencyVersion;
        var expectedVerificationFence = existing?.VerificationFence;
        // Allocate once, outside the physical owner. The human settlement boundary executes
        // one attempt only; no provider operation or allocated identity is replayed on error.
        var pending = prepared is not null && existing is null && ownership.Succeeded
            ? WebhookConsumerProviderBinding.CreatePending(consumer.TenantId, consumer.Id,
                bootstrap.Id, profile.ProviderEnvironment, profile.CapabilityProfile,
                profile.GovernanceAllowedCapabilities)
            : null;

        async Task<BaseCommandResponse<Guid>> Mutate(CancellationToken token)
        {
            if (prepared is not null)
            {
                await prepared.AcquireAsync(request, token);
                var currentConsumer = await consumerRepository.GetByIdForOwnerOperationAsync(
                    request.ConsumerId, forUpdate: false, token);
                RequireConsumer(prepared.Consumer, currentConsumer);
                consumer = currentConsumer!;
                RequireBootstrap(originalBootstrap, await bootstrapStateRepository.GetCurrentForUpdate(token));
                RequireProfile(profile);

                if (!ownership.Succeeded)
                {
                    var audit = await WriteRejectedAuditAsync(request, consumer, existing,
                        expectedApplicationUid, externalApplicationId, ownership.FailureCategory!,
                        profile.CapabilityProfile.ResolutionVersion, token, prepared.PrincipalId);
                    RegisterFinalAdmission(originalBinding, audit.RetentionUntil);
                    return Failure(existing?.Id ?? consumer.Id, ownership.FailureCategory!,
                        "The provider application does not prove typed ownership by this consumer.");
                }
            }

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

                if (prepared is not null && BindingFact(tracked) != originalBinding)
                    throw new UnauthorizedAccessException("The acquired repair binding changed.");
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

                binding = pending ?? WebhookConsumerProviderBinding.CreatePending(
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

            var repairedBinding = BindingFact(binding);
            if (expectedBindingId is null)
            {
                await bindingRepository.CreateAsync(binding, token);
            }
            else
            {
                await bindingRepository.SaveChangesAsync(token);
            }

            var succeededAudit = await auditWriter.AppendAsync(
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
                    EffectiveScopeId: consumer.OwnerId,
                    PrincipalKind: prepared is null ? null : WebhookAuditPrincipalKind.User,
                    PrincipalReference: prepared is null ? null : $"user:{prepared.PrincipalId:D}"),
                token);

            if (prepared is not null)
                RegisterFinalAdmission(repairedBinding, succeededAudit.RetentionUntil);
            return Success(binding.Id, "Webhook provider binding verified.");
        }

        void RegisterFinalAdmission(HumanWebhookBindingFact? expected, DateTime retentionUntil)
        {
            finalizer.Register(async token =>
            {
                // W already excludes competing writers. These tracker-free reads also reject
                // own writes to bootstrap or proof fields that closed transition admission
                // deliberately does not classify as provider-ownership evidence.
                RequireBootstrap(originalBootstrap, await bootstrapStateRepository.GetCurrent(token));
                var current = await bindingRepository.GetByConsumerAsync(consumer.TenantId, consumer.Id,
                    profile.ProviderKind, profile.ProviderEnvironment, token);
                if ((current is null ? null : BindingFact(current)) != expected)
                    throw new UnauthorizedAccessException("The retained provider ownership proof lineage changed.");
            }, now =>
            {
                RequireProfile(profile);
                if (now < occurredAt || now.UtcDateTime >= retentionUntil)
                    throw new UnauthorizedAccessException("Repair evidence is outside its retained time bounds.");
            });
        }

        if (prepared is null)
            return await unitOfWork.ExecuteInTransactionAsync(Mutate, cancellationToken);

        var invocation = new object();
        var settled = await settlementBoundary.ExecuteAsync(invocation, async token =>
            OriginalOwnerDecision<BaseCommandResponse<Guid>>.Commit(await Mutate(token)), cancellationToken);
        // A trusted remote refusal is evidence, not a rollback witness. Its audit is admitted
        // and committed in its own actual owner, with the same original requester and sources.
        return settlementBoundary.Consume(invocation, settled, OriginalOwnerDisposition.Committed);
    }

    private async Task<WebhookAuditEvent> WriteRejectedAuditAsync(
        RepairWebhookProviderBindingCommand request,
        WebhookConsumer consumer,
        WebhookConsumerProviderBinding? binding,
        string expectedApplicationUid,
        string externalApplicationId,
        string failureCategory,
        string capabilityResolutionVersion,
        CancellationToken cancellationToken,
        Guid? principalId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await auditWriter.AppendAsync(
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
                EffectiveScopeId: consumer.OwnerId,
                PrincipalKind: principalId.HasValue ? WebhookAuditPrincipalKind.User : null,
                PrincipalReference: principalId.HasValue ? $"user:{principalId.Value:D}" : null),
            cancellationToken);
    }

    private void RequireProfile(WebhookProviderBindingProfile expected)
    {
        var current = authorityService.ResolveCurrentProfile().Profile;
        if (current is null || current.ProviderKind != expected.ProviderKind
            || current.ProviderEnvironment != expected.ProviderEnvironment
            || current.GovernanceAllowedCapabilities != expected.GovernanceAllowedCapabilities
            || current.CapabilityProfile.ProviderKind != expected.CapabilityProfile.ProviderKind
            || current.CapabilityProfile.ProviderVersion != expected.CapabilityProfile.ProviderVersion
            || current.CapabilityProfile.Capabilities != expected.CapabilityProfile.Capabilities
            || current.CapabilityProfile.ResolutionVersion != expected.CapabilityProfile.ResolutionVersion)
            throw new UnauthorizedAccessException("The original repair provider profile changed.");
    }

    private static void RequireBootstrap(HumanWebhookInstanceFact expected, InstanceBootstrapState? current)
    {
        if (current is null || current.Id != expected.Id || (int)current.Status != expected.Status
            || current.Generation != expected.Generation || current.CreatedAt != expected.CreatedAt)
            throw new UnauthorizedAccessException("The original repair instance identity changed.");
    }

    private static void RequireConsumer(HumanWebhookConsumerFact expected, WebhookConsumer? current)
    {
        if (current is null || expected != new HumanWebhookConsumerFact(current.Id, current.TenantId,
            current.InstanceId, current.OrganizationId, current.GroupId, current.OwnerUserId,
            current.ConfigurationScopeId, current.ConsumerKindId, current.StatusId,
            current.ProviderModeId, current.ConfigurationVersion))
            throw new UnauthorizedAccessException("The original repair consumer changed.");
    }

    private static HumanWebhookBindingFact BindingFact(WebhookConsumerProviderBinding binding) => new(
        binding.Id, binding.TenantId, binding.WebhookConsumerId, binding.InstanceId, binding.ConfigurationScopeId,
        binding.ProviderKindId, binding.ProviderVersion, binding.ProviderEnvironment, binding.NormalizedEnvironment,
        binding.ApplicationUid, binding.NormalizedApplicationUid, binding.ExternalApplicationId,
        binding.NormalizedExternalApplicationId, binding.VerificationStateId, binding.VerifiedTenantId,
        binding.VerifiedWebhookConsumerId, binding.VerifiedAtUtc, (long)binding.Capabilities,
        (long)binding.GovernanceAllowedCapabilities, binding.CapabilityResolutionVersion,
        binding.CapabilitiesResolvedAtUtc, binding.IsEnabled, binding.ConcurrencyVersion, binding.VerificationFence);

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
