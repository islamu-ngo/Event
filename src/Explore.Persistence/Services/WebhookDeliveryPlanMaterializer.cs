using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Services;

public sealed class WebhookDeliveryPlanMaterializer(
    ExploreDbContext dbContext,
    IUnitOfWork unitOfWork) : IWebhookDeliveryPlanMaterializer
{
    private const string UniqueViolationSqlState = "23505";

    public async Task<WebhookDeliveryMaterializationResult> MaterializeAsync(
        WebhookDeliveryMaterialization materialization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        ValidateGraph(materialization);
        var hasAmbientTransaction = dbContext.Database.CurrentTransaction is not null;

        try
        {
            if (hasAmbientTransaction)
            {
                return await PersistAsync(materialization, cancellationToken);
            }

            return await unitOfWork.ExecuteInTransactionAsync(
                token => PersistAsync(materialization, token),
                cancellationToken);
        }
        catch (DbUpdateException exception) when (
            !hasAmbientTransaction &&
            exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            dbContext.ChangeTracker.Clear();
            var concurrent = await LoadExistingAsync(materialization.Message, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task<WebhookDeliveryMaterializationResult> PersistAsync(
        WebhookDeliveryMaterialization materialization,
        CancellationToken cancellationToken)
    {
        var existing = await LoadExistingAsync(materialization.Message, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        dbContext.WebhookMessages.Add(materialization.Message);
        dbContext.WebhookDeliveryPlanSnapshots.Add(materialization.DeliveryPlan);
        dbContext.WebhookLocalTargetSnapshots.AddRange(materialization.LocalTargets);
        dbContext.WebhookProviderPublications.AddRange(materialization.ProviderPublications);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new WebhookDeliveryMaterializationResult(
            materialization.Message,
            materialization.DeliveryPlan,
            Created: true);
    }

    private async Task<WebhookDeliveryMaterializationResult?> LoadExistingAsync(
        WebhookMessage requested,
        CancellationToken cancellationToken)
    {
        var existingMessage = await dbContext.WebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                message =>
                    message.TenantId == requested.TenantId &&
                    (message.Id == requested.Id ||
                     (message.EventType == requested.EventType && message.EventId == requested.EventId)),
                cancellationToken);
        if (existingMessage is null)
        {
            return null;
        }

        EnsureSameSemanticMessage(existingMessage, requested);
        var existingPlan = await dbContext.WebhookDeliveryPlanSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                plan => plan.TenantId == existingMessage.TenantId &&
                    plan.WebhookMessageId == existingMessage.Id,
                cancellationToken);
        if (existingPlan is null)
        {
            throw new InvalidOperationException(
                "The existing webhook message has no atomic delivery-plan snapshot.");
        }

        return new WebhookDeliveryMaterializationResult(
            existingMessage,
            existingPlan,
            Created: false);
    }

    private static void EnsureSameSemanticMessage(WebhookMessage existing, WebhookMessage requested)
    {
        if (existing.TenantId != requested.TenantId ||
            existing.ConsumerId != requested.ConsumerId ||
            existing.AggregateId != requested.AggregateId ||
            !string.Equals(existing.EventType, requested.EventType, StringComparison.Ordinal) ||
            !string.Equals(existing.EventId, requested.EventId, StringComparison.Ordinal) ||
            !string.Equals(existing.AggregateKind, requested.AggregateKind, StringComparison.Ordinal) ||
            !string.Equals(existing.PayloadHash, requested.PayloadHash, StringComparison.Ordinal))
        {
            throw new WebhookMaterializationConflictException(
                "The webhook semantic identity already exists with different immutable data.");
        }
    }

    private static void ValidateGraph(WebhookDeliveryMaterialization materialization)
    {
        var message = materialization.Message;
        var plan = materialization.DeliveryPlan;
        if (message.ConsumerId is null ||
            plan.TenantId != message.TenantId ||
            plan.WebhookMessageId != message.Id ||
            plan.WebhookConsumerId != message.ConsumerId)
        {
            throw new ArgumentException(
                "The delivery plan must match the message tenant, message, and consumer.",
                nameof(materialization));
        }

        if (materialization.LocalTargets.Any(target =>
                target.TenantId != message.TenantId ||
                target.WebhookMessageId != message.Id ||
                target.DeliveryPlanSnapshotId != plan.Id) ||
            materialization.ProviderPublications.Any(publication =>
                publication.TenantId != message.TenantId ||
                publication.WebhookMessageId != message.Id ||
                publication.WebhookDeliveryPlanSnapshotId != plan.Id ||
                !string.Equals(publication.RequestHash, message.PayloadHash, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Every delivery target must reference the immutable message and plan.",
                nameof(materialization));
        }
    }
}
