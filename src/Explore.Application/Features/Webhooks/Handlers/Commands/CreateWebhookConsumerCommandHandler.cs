using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class CreateWebhookConsumerCommandHandler(
    IWebhookConsumerRepository consumerRepository,
    IWebhookOwnershipScopeResolver ownershipScopeResolver,
    IWebhookProviderCapabilityResolver capabilityResolver,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateWebhookConsumerCommand, BaseCommandResponse<Guid>>
{
    private const int MaxNameLength = 200;

    public async Task<BaseCommandResponse<Guid>> Handle(
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

        var ownershipResolution = await ownershipScopeResolver.ResolveAsync(
            request.ConsumerKindId,
            request.OwnerId,
            cancellationToken);
        if (ownershipResolution.Scope is not { } ownership)
        {
            return Failure(
                "Webhook owner resolution failed.",
                ownershipResolution.FailureCode ?? "webhook_owner_resolution_failed",
                [ownershipResolution.Error ?? "Webhook owner could not be resolved."]);
        }

        var name = request.Name.Trim();
        var existing = await consumerRepository.GetByOwnerAndNameAsync(
            ownership,
            name,
            cancellationToken);

        if (existing is not null)
        {
            return Failure(
                "Webhook consumer name is already in use.",
                "webhook_consumer_name_conflict",
                ["A webhook consumer with this name already exists for the selected owner."]);
        }

        var consumer = WebhookConsumer.Create(
            ownership,
            name,
            (WebhookProviderMode)request.ProviderModeId,
            DateTime.UtcNow);

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var created = await consumerRepository.CreateAsync(consumer, token);
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
                    EffectiveScopeId: ownership.OwnerId),
                token);

            return BaseCommandResponse.Success(created.Id, "Webhook consumer created.");
        }, cancellationToken);
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
