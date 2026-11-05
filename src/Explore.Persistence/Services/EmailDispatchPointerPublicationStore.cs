using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed class EmailDispatchPointerPublicationStore(
    IGlobalExploreDbContextFactory factory,
    PostgresNativeQueueEnrollment? enrollment = null) : IEmailDispatchPointerPublicationStore
{
    public Task<IReadOnlyList<EmailDispatchPointerCandidate>> GetPublishBatchAsync(
        int batchSize, DateTime now, DateTime retryAttemptsBefore, CancellationToken cancellationToken) =>
        WithOwnerAsync<IReadOnlyList<EmailDispatchPointerCandidate>>(async (context, requireReady) =>
        {
            await requireReady();
            bool processorPaused = await context.EmailDispatchProcessorStates
                .AsNoTracking()
                .AnyAsync(state => state.ProcessorCode == "smtp" && state.IsPaused, cancellationToken);
            await requireReady();

            var pausedTenantIds = context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailPointerPublication)
                .AsNoTracking()
                .Where(control => control.IsPaused)
                .Select(control => control.TenantId);
            var rows = await context.EmailDispatchOutbox
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailPointerPublication)
                .AsNoTracking()
                .Where(row => row.ContentRedactedAt == null
                    && (row.Status == EmailDispatchStatus.Pending || row.Status == EmailDispatchStatus.RetryScheduled)
                    && (row.NextAttemptAt == null || row.NextAttemptAt <= now)
                    && (row.RabbitMqLastPublishAttemptAt == null || row.RabbitMqLastPublishAttemptAt <= retryAttemptsBefore)
                    && !processorPaused
                    && !pausedTenantIds.Contains(row.TenantId))
                .OrderBy(row => row.CreatedAt)
                .Take(batchSize)
                .Select(row => new EmailDispatchPointerCandidate(row.Id, new EmailDispatchPointer(
                    row.TenantId, row.PublishEventId, row.Kind, row.SourceType,
                    row.SourceId, row.EventId, row.RegistrationOrderId)))
                .ToListAsync(cancellationToken);
            await requireReady();
            return rows;
        }, cancellationToken);

    public async Task MarkPublishSucceededAsync(
        EmailDispatchPointerCandidate candidate, DateTime publishedAt, CancellationToken cancellationToken)
    {
        await WithOwnerAsync(async (context, requireReady) =>
        {
            await requireReady();
            int updated = await MarkerRows(context, candidate)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.RabbitMqLastPublishedAt, publishedAt)
                    .SetProperty(row => row.RabbitMqLastPublishAttemptAt, publishedAt)
                    .SetProperty(row => row.RabbitMqPublishAttemptCount, row => row.RabbitMqPublishAttemptCount + 1)
                    .SetProperty(row => row.RabbitMqLastPublishFailureCategory, (string?)null)
                    .SetProperty(row => row.UpdatedAt, publishedAt), cancellationToken);
            await requireReady();
            return updated;
        }, cancellationToken);
    }

    public async Task MarkPublishFailedAsync(
        EmailDispatchPointerCandidate candidate, string failureCategory, DateTime attemptedAt,
        CancellationToken cancellationToken)
    {
        string storedCategory = failureCategory.Length > 100 ? failureCategory[..100] : failureCategory;
        await WithOwnerAsync(async (context, requireReady) =>
        {
            await requireReady();
            int updated = await MarkerRows(context, candidate)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.RabbitMqLastPublishAttemptAt, attemptedAt)
                    .SetProperty(row => row.RabbitMqPublishAttemptCount, row => row.RabbitMqPublishAttemptCount + 1)
                    .SetProperty(row => row.RabbitMqLastPublishFailureCategory, storedCategory)
                    .SetProperty(row => row.UpdatedAt, attemptedAt), cancellationToken);
            await requireReady();
            return updated;
        }, cancellationToken);
    }

    private static IQueryable<EmailDispatchOutbox> MarkerRows(
        ExploreDbContext context, EmailDispatchPointerCandidate candidate) =>
        context.EmailDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailPointerPublication)
            .Where(row => row.Id == candidate.OutboxId && row.TenantId == candidate.Pointer.TenantId
                && (row.Status == EmailDispatchStatus.Pending || row.Status == EmailDispatchStatus.RetryScheduled));

    private async Task<T> WithOwnerAsync<T>(
        Func<ExploreDbContext, Func<Task>, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("email_pointer_publication_ambient_transaction");
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        void RequireLease()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
                || System.Transactions.Transaction.Current is not null
                || context.GetService<IDbContextTransactionManager>() is ITransactionEnlistmentManager
                    { EnlistedTransaction: not null })
                throw new UnauthorizedAccessException("email_pointer_publication_original_global_required");
        }
        RequireLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        await context.Database.OpenConnectionAsync(cancellationToken);
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, System.Data.StateChangeEventArgs change)
        {
            if (change.CurrentState != System.Data.ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            RequireLease();
            if (interrupted || !connection.Matches(context)
                || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("email_pointer_publication_original_owner_changed");
        }
        async Task RequireReady()
        {
            RequireOriginal();
            await PostgresEmailPointerPublicationPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
        }
        connection.StateChange += Changed;
        try
        {
            return await operation(context, RequireReady);
        }
        finally
        {
            connection.StateChange -= Changed;
            if (context.ContextId == contextId && !interrupted && connection.Matches(context)
                && connection.State == System.Data.ConnectionState.Open && connection.BackendProcessId == backend)
                await context.Database.CloseConnectionAsync();
        }
    }
}
