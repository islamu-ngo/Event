using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class CreateWebhookConsumerCommandHandler(
    IWebhookConsumerRepository consumerRepository,
    IWebhookOwnershipScopeResolver ownershipScopeResolver,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    IMachinePrincipalAccessor machinePrincipalAccessor,
    IHumanCommitScopePreparation humanScope,
    IHumanWebhookMutationPreparation preparation,
    ITenantContext tenantContext,
    ITenantUserRepository tenantUserRepository,
    TimeProvider timeProvider,
    IHumanActionCommitFinalizer finalizer)
    : ICommandHandler<CreateWebhookConsumerCommand, BaseCommandResponse<Guid>>
{
    private const int MaxNameLength = 200;

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        CreateWebhookConsumerCommand request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Failure(
                "Webhook consumer validation failed.",
                "webhook_consumer_validation_failed",
                errors);
        }

        var capabilityResolution = capabilityResolver.Resolve((WebhookProviderMode)request.ProviderModeId);
        if (!capabilityResolution.IsProviderModeAvailable)
        {
            return Failure(
                "Webhook provider mode is unavailable.",
                "webhook_provider_mode_capability_unavailable",
                [capabilityResolution.UnavailableReasonCode ?? "Webhook provider capability authority is unavailable."]);
        }

        var machine = machinePrincipalAccessor.Current;
        HumanCommitSubject? subject = null;
        if (machine is null)
        {
            await humanScope.PrepareAsync(cancellationToken);
            subject = humanScope.Subject
                ?? throw new UnauthorizedAccessException("An original authenticated human is required.");
        }

        var consumerId = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var name = request.Name.Trim();
        WebhookOwnershipScope? originalOwnership = null;

        async Task<BaseCommandResponse<Guid>> MutateAsync(CancellationToken token)
        {
            WebhookOwnershipScope ownership;
            if (subject is not null && request.ConsumerKindId == (int)WebhookConsumerKind.User)
            {
                // The ordinary resolver reads claim attribution. Human creation instead
                // retains the canonical account resolved before this acquired owner.
                if (request.OwnerId.HasValue && request.OwnerId != subject.PrincipalId)
                    return Failure("Webhook owner resolution failed.", "webhook_owner_mismatch",
                        ["Users can select only their own webhook ownership scope."]);
                var member = await tenantUserRepository.GetByTenantAndUserAsync(
                    tenantContext.TenantId, subject.PrincipalId, token);
                if (member is null || member.IsDeleted || member.StatusId != (int)TenantUserStatusEnum.Active)
                    return Failure("Webhook owner resolution failed.", "webhook_owner_not_found",
                        ["An active user membership is required in the current tenant."]);
                if (member.TenantId != tenantContext.TenantId || member.UserId != subject.PrincipalId)
                    throw new UnauthorizedAccessException("Webhook membership must retain the original human and tenant.");
                ownership = WebhookOwnershipScope.Create(WebhookConsumerKind.User,
                    member.TenantId, null, null, null, subject.PrincipalId);
            }
            else
            {
                var resolution = await ownershipScopeResolver.ResolveAsync(
                    request.ConsumerKindId, request.OwnerId, token);
                if (resolution.Scope is not { } resolved)
                    return Failure("Webhook owner resolution failed.",
                        resolution.FailureCode ?? "webhook_owner_resolution_failed",
                        [resolution.Error ?? "Webhook owner could not be resolved."]);
                ownership = resolved;
            }

            originalOwnership ??= ownership;
            if (ownership != originalOwnership)
                throw new UnauthorizedAccessException("A retried consumer must retain its original ownership.");

            var existing = await consumerRepository.GetByOwnerAndNameAsync(ownership, name, token);
            if (existing is not null)
                return Failure("Webhook consumer name is already in use.", "webhook_consumer_name_conflict",
                    ["A webhook consumer with this name already exists for the selected owner."]);

            // Reconstruct after whole-owner rollback; never reuse a tracked product.
            var consumer = WebhookConsumer.Create(ownership, name, (WebhookProviderMode)request.ProviderModeId, now);
            consumer.Id = consumerId;
            consumer.CreatedBy = subject?.PrincipalId;
            var created = await consumerRepository.CreateAsync(consumer, token);
            // CreateConsumer's W selects owner contributors, not the new consumer.
            void RequireCreated(WebhookConsumer? current)
            {
                if (current is null || current.Id != consumerId || current.Ownership != ownership
                    || current.ConfigurationScopeId != (ownership.TenantId ?? ownership.InstanceId)
                    || current.CreatedAt != now || current.Name != name
                    || current.Status != WebhookConsumerStatus.Active
                    || current.ProviderModeId != request.ProviderModeId || current.ConfigurationVersion != 1
                    || subject is not null && current.CreatedBy != subject.PrincipalId)
                    throw new UnauthorizedAccessException("The created consumer must retain its original typed ownership.");
            }
            RequireCreated(created);
            if (subject is not null)
                finalizer.Register(async finalToken =>
                    RequireCreated(await consumerRepository.GetByIdForOwnerOperationAsync(
                        consumerId, forUpdate: false, finalToken)), _ => { });
            await auditWriter.AppendAsync(
                new WebhookAuditWriteRequest(
                    created.TenantId,
                    WebhookAuditAction.ConsumerCreated,
                    WebhookAuditTargetKind.Consumer,
                    created.Id,
                    "consumer_created",
                    WebhookAuditOutcome.Succeeded,
                    SafeAfterJson: JsonSerializer.Serialize(new
                    {
                        consumerKind = created.ConsumerKind.ToString(),
                        status = created.Status.ToString(),
                        providerMode = created.ProviderMode.ToString(),
                        created.ConfigurationVersion,
                        ownerKind = created.ConsumerKind.ToString(),
                        ownerId = created.OwnerId
                    }),
                    ConfigurationVersion: $"consumer-v{created.ConfigurationVersion}",
                    EffectiveScopeKind: ownership.AuditScopeKind,
                    EffectiveScopeId: ownership.OwnerId,
                    PrincipalKind: subject is not null ? WebhookAuditPrincipalKind.User : WebhookAuditPrincipalKind.Machine,
                    PrincipalReference: subject is not null ? $"user:{subject.PrincipalId:D}"
                        : $"machine:{machine!.OwnerType}:{machine.OwnerId:D}"),
                token);

            return BaseCommandResponse.Success(created.Id, "Webhook consumer created.");
        }

        return machine is null
            ? await preparation.ExecuteAsync(request, MutateAsync, cancellationToken)
            : await unitOfWork.ExecuteInTransactionAsync(MutateAsync, cancellationToken);
    }

    private static List<string> Validate(CreateWebhookConsumerCommand request)
    {
        var errors = new List<string>();

        if (request.OwnerId == Guid.Empty)
        {
            errors.Add("OwnerId must not be empty when provided.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("Name is required.");
        }
        else if (request.Name.Trim().Length > MaxNameLength)
        {
            errors.Add($"Name must be {MaxNameLength} characters or fewer.");
        }

        if (!Enum.IsDefined(typeof(WebhookConsumerKind), request.ConsumerKindId))
        {
            errors.Add("ConsumerKindId is invalid.");
        }

        if (!Enum.IsDefined(typeof(WebhookProviderMode), request.ProviderModeId))
        {
            errors.Add("ProviderModeId is invalid.");
        }

        return errors;
    }

    private static BaseCommandResponse<Guid> Failure(
        string message,
        string failureCode,
        List<string> errors)
        => BaseCommandResponse.Failure<Guid>(failureCode, message, errors);
}
