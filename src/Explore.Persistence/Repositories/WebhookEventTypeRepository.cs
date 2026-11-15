using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class WebhookEventTypeRepository(
    IGlobalExploreDbContextFactory factory,
    PostgresNativeQueueEnrollment? enrollment = null) : IWebhookEventTypeRepository
{
    public Task<WebhookEventType> CreateAsync(WebhookEventType eventType, CancellationToken cancellationToken) =>
        ExecuteAsync(async (context, requireOriginal) =>
        {
            if (eventType.Id == Guid.Empty)
            {
                eventType.Id = Guid.CreateVersion7();
            }

            await context.WebhookEventTypes.AddAsync(eventType, cancellationToken);
            requireOriginal();
            await context.SaveChangesAsync(cancellationToken);
            context.Entry(eventType).State = EntityState.Detached;
            return eventType;
        }, cancellationToken);

    public Task<WebhookEventType?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
        ExecuteAsync((context, _) => context.WebhookEventTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name == name, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<WebhookEventType>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<WebhookEventType>>(async (context, _) => await context.WebhookEventTypes
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .OrderBy(e => e.GroupName)
            .ThenBy(e => e.Name)
            .ToListAsync(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<WebhookEventType>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<WebhookEventType>>(async (context, _) => await context.WebhookEventTypes
            .AsNoTracking()
            .Where(e => names.Contains(e.Name))
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<WebhookEventType>> GetEnabledAsync(CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<WebhookEventType>>(async (context, _) => await context.WebhookEventTypes
            .AsNoTracking()
            .Where(e => e.IsEnabled)
            .OrderBy(e => e.GroupName)
            .ThenBy(e => e.Name)
            .ToListAsync(cancellationToken), cancellationToken);

    public async Task UpdateAsync(WebhookEventType eventType, CancellationToken cancellationToken) =>
        _ = await ExecuteAsync(async (context, requireOriginal) =>
        {
            var stored = await context.WebhookEventTypes.SingleAsync(row => row.Id == eventType.Id, cancellationToken);
            requireOriginal();
            stored.GroupName = eventType.GroupName;
            stored.Description = eventType.Description;
            stored.SchemaJson = eventType.SchemaJson;
            stored.SchemaVersion = eventType.SchemaVersion;
            stored.IsPublic = eventType.IsPublic;
            stored.IsEnabled = eventType.IsEnabled;
            stored.PayloadRetentionDays = eventType.PayloadRetentionDays;
            stored.UpdatedAt = eventType.UpdatedAt;
            stored.UpdatedBy = eventType.UpdatedBy;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private async Task<T> ExecuteAsync<T>(Func<ExploreDbContext, Action, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("webhook_catalogue_original_global_required");
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
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
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
                || System.Transactions.Transaction.Current is not null
                || interrupted || !connection.Matches(context)
                || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("webhook_catalogue_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await PostgresWebhookCataloguePermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            T result = await operation(context, RequireOriginal);
            RequireOriginal();
            await PostgresWebhookCataloguePermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            return result;
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
