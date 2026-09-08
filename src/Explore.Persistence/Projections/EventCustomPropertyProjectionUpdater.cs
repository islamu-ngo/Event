using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Projections;

public class EventCustomPropertyProjectionUpdater : IEventCustomPropertyProjectionUpdater
{
    private const string ProjectionName = IEventCustomPropertyProjectionUpdater.ProjectionName;
    private const int ProjectionVersion = IEventCustomPropertyProjectionUpdater.ProjectionVersion;

    // Stable int key for PostgreSQL advisory locks. Derived from FNV-1a of ProjectionName.
    private static readonly int ProjectionLockKey = ProjectionInfrastructure.ComputeStableKey(ProjectionName);

    private readonly ExploreDbContext _dbContext;
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;
    private readonly ICustomPropertyProjectionStatusRepository _statusRepository;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ProjectionMetrics _metrics;

    public EventCustomPropertyProjectionUpdater(
        ExploreDbContext dbContext,
        ICustomPropertyProjectionDirtyScopeRepository dirtyScopeRepository,
        ICustomPropertyProjectionStatusRepository statusRepository,
        ICustomPropertyQuotaResolver quotaResolver,
        ProjectionMetrics metrics)
    {
        _dbContext = dbContext;
        _dirtyScopeRepository = dirtyScopeRepository;
        _statusRepository = statusRepository;
        _quotaResolver = quotaResolver;
        _metrics = metrics;
    }

    public async Task UpdateForValueAsync(Guid valueId, CancellationToken cancellationToken)
    {
        var value = await _dbContext.EventCustomPropertyValues
            .Include(v => v.Definition)
            .FirstOrDefaultAsync(v => v.Id == valueId, cancellationToken);

        if (value is null || value.Definition is null)
        {
            await RemoveProjectionForValueAsync(valueId, cancellationToken);
            return;
        }

        if (!await TryAcquireSharedLockAsync(value.TenantId, cancellationToken))
        {
            await UpsertDirtyScopeSkipAsync(
                value.TenantId,
                value.EventId,
                value.EventCustomPropertyDefinitionId,
                "rebuild_in_progress",
                cancellationToken);
            _metrics.RecordDirtyScopeSkip(value.TenantId.ToString(), ProjectionName, "value", "rebuild_in_progress");
            return;
        }

        await UpsertProjectionRowAsync(value, value.Definition, cancellationToken);
        _metrics.RecordInlineUpdate(value.TenantId.ToString(), ProjectionName, "value");
    }

    public async Task UpdateForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.EventCustomPropertyDefinitions
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);

        if (definition is null)
        {
            await RemoveForDefinitionAsync(definitionId, cancellationToken);
            return;
        }

        if (!await TryAcquireSharedLockAsync(definition.TenantId, cancellationToken))
        {
            await UpsertDirtyScopeSkipAsync(
                definition.TenantId,
                definition.EventId,
                definition.Id,
                "rebuild_in_progress",
                cancellationToken);
            _metrics.RecordDirtyScopeSkip(definition.TenantId.ToString(), ProjectionName, "definition", "rebuild_in_progress");
            return;
        }

        var values = await _dbContext.EventCustomPropertyValues
            .Where(v => v.EventCustomPropertyDefinitionId == definitionId)
            .ToListAsync(cancellationToken);

        foreach (var value in values)
        {
            await UpsertProjectionRowAsync(value, definition, cancellationToken);
        }

        _metrics.RecordInlineUpdate(definition.TenantId.ToString(), ProjectionName, "definition", values.Count);
    }

    public async Task RemoveForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        await _dbContext.EventCustomPropertyProjections
            .Where(p => p.EventCustomPropertyDefinitionId == definitionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RefreshForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await _dbContext.EventCustomPropertyProjections
            .Where(p => p.EventId == eventId)
            .ExecuteDeleteAsync(cancellationToken);

        var values = await _dbContext.EventCustomPropertyValues
            .Include(v => v.Definition)
            .Where(v => v.EventId == eventId)
            .ToListAsync(cancellationToken);

        foreach (var value in values)
        {
            if (value.Definition is null)
            {
                continue;
            }

            var row = await BuildProjectionRowAsync(value, value.Definition, cancellationToken);
            _dbContext.EventCustomPropertyProjections.Add(row);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectionRebuildResult> RebuildForTenantAsync(
        Guid tenantId,
        int? batchSize,
        CancellationToken cancellationToken)
    {
        // D1 baseline: single transaction with xact-scoped advisory lock. D2 may introduce
        // session-scoped locks + per-batch commits + live "Rebuilding" status visibility.
        var effectiveBatchSize = batchSize ?? await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.ProjectionRebuildBatchSize.Key,
            tenantId,
            cancellationToken);

        return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var startedAt = DateTimeOffset.UtcNow;
            long rowsProcessed = 0;
            long rowsFailed = 0;

            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken);

            if (!await TryAcquireExclusiveLockAsync(tenantId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ProjectionRebuildResult(false, 0, 0, 0);
            }

            var eventIds = await _dbContext.Events
                .Where(e => e.TenantId == tenantId)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            foreach (var chunk in ProjectionInfrastructure.Chunk(eventIds, effectiveBatchSize))
            {
                foreach (var eventId in chunk)
                {
                    try
                    {
                        await RefreshForEventAsync(eventId, cancellationToken);
                        rowsProcessed++;
                    }
                    catch (DbUpdateException)
                    {
                        rowsFailed++;
                    }
                }
            }

            var drained = await DrainPendingScopesAsync(tenantId, effectiveBatchSize, cancellationToken);

            await _statusRepository.UpsertAsync(new CustomPropertyProjectionStatus
            {
                ProjectionName = ProjectionName,
                ProjectionVersion = ProjectionVersion,
                TenantId = tenantId,
                State = rowsFailed == 0
                    ? CustomPropertyProjectionState.Idle
                    : CustomPropertyProjectionState.Failed,
                LastRebuildStartedAt = startedAt,
                LastRebuildCompletedAt = DateTimeOffset.UtcNow,
                RowsProcessed = rowsProcessed,
                RowsFailed = rowsFailed,
                LastCheckpoint = null,
                LastErrorMessage = null,
            }, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ProjectionRebuildResult(true, rowsProcessed, rowsFailed, drained);
        });
    }

    public async Task<int> DrainDirtyScopesForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var batchSize = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.ProjectionRebuildBatchSize.Key,
            tenantId,
            cancellationToken);

        return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database
                .BeginTransactionAsync(cancellationToken);

            if (!await TryAcquireExclusiveLockAsync(tenantId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return 0;
            }

            var drained = await DrainPendingScopesAsync(tenantId, batchSize, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return drained;
        });
    }

    private async Task<int> DrainPendingScopesAsync(Guid tenantId, int batchSize, CancellationToken cancellationToken)
    {
        var drainedTotal = 0;
        while (true)
        {
            var pending = await _dirtyScopeRepository.GetPendingAsync(
                ProjectionName,
                ProjectionVersion,
                tenantId,
                batchSize,
                cancellationToken);

            if (pending.Count == 0)
            {
                break;
            }

            var processedIds = new List<long>(pending.Count);
            foreach (var row in pending)
            {
                if (row.ScopeType != CustomPropertyProjectionScopeType.Event)
                {
                    continue;
                }

                await RefreshForEventAsync(row.ScopeId, cancellationToken);
                processedIds.Add(row.Id);
            }

            if (processedIds.Count > 0)
            {
                await _dirtyScopeRepository.MarkDrainedAsync(processedIds, DateTimeOffset.UtcNow, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            drainedTotal += processedIds.Count;

            if (pending.Count < batchSize)
            {
                break;
            }
        }

        return drainedTotal;
    }

    private async Task UpsertProjectionRowAsync(
        EventCustomPropertyValue value,
        EventCustomPropertyDefinition definition,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.EventCustomPropertyProjections
            .FirstOrDefaultAsync(p => p.EventCustomPropertyValueId == value.Id, cancellationToken);

        if (existing is null)
        {
            _dbContext.EventCustomPropertyProjections.Add(
                await BuildProjectionRowAsync(value, definition, cancellationToken));
            return;
        }

        await ApplyProjectionFieldsAsync(existing, value, definition, cancellationToken);
    }

    private async Task RemoveProjectionForValueAsync(Guid valueId, CancellationToken cancellationToken)
    {
        await _dbContext.EventCustomPropertyProjections
            .Where(p => p.EventCustomPropertyValueId == valueId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpsertDirtyScopeSkipAsync(
        Guid tenantId,
        Guid eventId,
        Guid? definitionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var pendingCount = await _dirtyScopeRepository.CountPendingAsync(
            ProjectionName,
            ProjectionVersion,
            tenantId,
            cancellationToken);

        var quota = await _quotaResolver.GetIntAsync(
            CustomPropertyQuotaSettingDefinitions.MaxDirtyScopePendingPerTenant.Key,
            tenantId,
            cancellationToken);

        if (pendingCount >= quota)
        {
            throw new QuotaExceededException(
                "Custom-property projection dirty-scope backlog exceeded the tenant quota. Run rebuild or drain before retrying.",
                CustomPropertyQuotaSettingDefinitions.MaxDirtyScopePendingPerTenant.Key,
                quota,
                pendingCount,
                pendingCount + 1,
                "event_custom_property_projection_dirty_scope",
                tenantId);
        }

        await _dirtyScopeRepository.UpsertAsync(
            ProjectionName,
            ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.Event,
            eventId,
            definitionId,
            reason,
            cancellationToken);
    }

    private async Task<EventCustomPropertyProjection> BuildProjectionRowAsync(
        EventCustomPropertyValue value,
        EventCustomPropertyDefinition definition,
        CancellationToken cancellationToken)
    {
        var row = new EventCustomPropertyProjection
        {
            EventCustomPropertyDefinitionId = definition.Id,
            EventCustomPropertyValueId = value.Id,
            EventId = value.EventId,
            TenantId = value.TenantId,
            Namespace = definition.Namespace,
            Key = definition.Key,
        };

        await ApplyProjectionFieldsAsync(row, value, definition, cancellationToken);
        return row;
    }

    private async Task ApplyProjectionFieldsAsync(
        EventCustomPropertyProjection row,
        EventCustomPropertyValue value,
        EventCustomPropertyDefinition definition,
        CancellationToken cancellationToken)
    {
        row.PropertyType = definition.PropertyType;
        row.ExposureLevel = definition.ExposureLevel;
        row.IsSearchable = definition.IsSearchable;
        row.IsFilterable = definition.IsFilterable;
        row.IsExportable = definition.IsExportable;
        row.IsModerationRelevant = definition.IsModerationRelevant;
        row.IsAnalyticsRelevant = definition.IsAnalyticsRelevant;
        row.Ordinal = value.Ordinal;
        row.OptionId = value.OptionId;
        row.TextValue = value.TextValue;
        row.NumberValue = value.NumberValue;
        row.BooleanValue = value.BooleanValue;
        row.DateTimeValue = value.DateTimeValue;
        row.Namespace = definition.Namespace;
        row.Key = definition.Key;
        row.UpdatedAt = DateTime.UtcNow;

        var optionText = await ResolveOptionTextAsync(value.OptionId, cancellationToken);

        row.NormalizedValue = CustomPropertyProjectionNormalizer.Compute(
            definition.PropertyType,
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateTimeValue,
            optionText);
    }

    private async Task<string?> ResolveOptionTextAsync(Guid? optionId, CancellationToken cancellationToken)
    {
        if (optionId is not { } id)
        {
            return null;
        }

        var tracked = _dbContext.EventCustomPropertyOptions.Local.FirstOrDefault(o => o.Id == id);
        if (tracked is not null)
        {
            return tracked.Value;
        }

        return await _dbContext.EventCustomPropertyOptions
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => o.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<bool> TryAcquireSharedLockAsync(Guid tenantId, CancellationToken cancellationToken)
        => ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(_dbContext, ProjectionLockKey, tenantId, false, cancellationToken);

    private Task<bool> TryAcquireExclusiveLockAsync(Guid tenantId, CancellationToken cancellationToken)
        => ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(_dbContext, ProjectionLockKey, tenantId, true, cancellationToken);
}
