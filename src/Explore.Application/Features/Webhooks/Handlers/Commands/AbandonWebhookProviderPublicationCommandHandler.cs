using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Validators;
using Explore.Application.Lookups;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Commands;

public sealed class AbandonWebhookProviderPublicationCommandHandler(
    IWebhookProviderPublicationRepository repository,
    IWebhookAuditEventWriter auditWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AbandonWebhookProviderPublicationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        AbandonWebhookProviderPublicationCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new AbandonWebhookProviderPublicationCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.PublicationId,
                "webhook_provider_publication_abandon_validation_failed",
                "Provider publication abandonment request failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var publication = await repository.GetByTenantAndIdAsync(
                    request.TenantId,
                    request.PublicationId,
                    token);
                if (publication is null)
                {
                    return Failure(
                        request.PublicationId,
                        "webhook_provider_publication_not_found",
                        "Provider publication was not found.");
                }

                if (publication.ConcurrencyVersion != request.ExpectedConcurrencyVersion)
                {
                    return Conflict(request.PublicationId);
                }

                if (publication.Status is not (WebhookProviderPublicationStatus.ManualReconciliation or
                    WebhookProviderPublicationStatus.DeadLettered))
                {
                    return Failure(
                        request.PublicationId,
                        "webhook_provider_publication_not_abandonable",
                        "Only manual-reconciliation or dead-lettered publications can be abandoned.");
                }

                var previousStatus = NormalizedLookupMetadata
                    .WebhookProviderPublicationStatus(publication.StatusId).Code;
                var previousVersion = publication.ConcurrencyVersion;
                var previousFailureCategory = publication.FailureCategory;
                publication.Abandon(
                    "operator_abandoned",
                    null,
                    timeProvider.GetUtcNow().UtcDateTime);
                await repository.UpdateAsync(publication, token);

                await auditWriter.AppendAsync(
                    new WebhookAuditWriteRequest(
                        publication.TenantId,
                        WebhookAuditAction.ProviderPublicationAbandoned,
                        WebhookAuditTargetKind.ProviderPublication,
                        publication.Id,
                        request.ReasonCode,
                        WebhookAuditOutcome.Succeeded,
                        SafeBeforeJson: JsonSerializer.Serialize(new
                        {
                            status = previousStatus,
                            concurrencyVersion = previousVersion,
                            failureCategory = previousFailureCategory
                        }),
                        SafeAfterJson: JsonSerializer.Serialize(new
                        {
                            status = NormalizedLookupMetadata
                                .WebhookProviderPublicationStatus(publication.StatusId).Code,
                            publication.ConcurrencyVersion,
                            publication.FailureCategory
                        }),
                        ConfigurationVersion: $"publication-v{publication.ConcurrencyVersion}",
                        PrincipalKind: WebhookAuditPrincipalKind.User,
                        PrincipalReference: $"user:{request.ActorUserId:D}"),
                    token);

                return Success(publication.Id, "Provider publication abandoned.");
            }, cancellationToken);
        }
        catch (WebhookProviderPublicationConcurrencyException)
        {
            return Conflict(request.PublicationId);
        }
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Conflict(Guid id) => Failure(
        id,
        "webhook_provider_publication_concurrency_conflict",
        "Provider publication changed. Reload it before applying another operation.");

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string code,
        string message,
        IEnumerable<string>? errors = null) =>
        BaseCommandResponse.Failure(code, message, errors ?? [message], id);
}
