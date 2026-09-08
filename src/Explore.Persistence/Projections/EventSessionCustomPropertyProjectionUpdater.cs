using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Projections;

public class EventSessionCustomPropertyProjectionUpdater : IEventSessionCustomPropertyProjectionUpdater
{
    private const string ProjectionName = IEventSessionCustomPropertyProjectionUpdater.ProjectionName;
    private const int ProjectionVersion = IEventSessionCustomPropertyProjectionUpdater.ProjectionVersion;

    private static readonly int ProjectionLockKey = ProjectionInfrastructure.ComputeStableKey(ProjectionName);

    private readonly ExploreDbContext _dbContext;
    private readonly ICustomPropertyProjectionDirtyScopeRepository _dirtyScopeRepository;
    private readonly ICustomPropertyProjectionStatusRepository _statusRepository;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ProjectionMetrics _metrics;

    public EventSessionCustomPropertyProjectionUpdater(
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
        var value = await _dbContext.EventSessionCustomPropertyValues
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
                value.EventSessionId,
                value.EventSessionCustomPropertyDefinitionId,
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
        var definition = await _dbContext.EventSessionCustomPropertyDefinitions
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
                definition.EventSessionId,
                definition.Id,
                "rebuild_in_progress",
                cancellationToken);
            _metrics.RecordDirtyScopeSkip(definition.TenantId.ToString(), ProjectionName, "definition", "rebuild_in_progress");
            return;
        }

        var values = await _dbContext.EventSessionCustomPropertyValues
            .Where(v => v.EventSessionCustomPropertyDefinitionId == definitionId)
            .ToListAsync(cancellationToken);

        foreach (var value in values)
        {
            await UpsertProjectionRowAsync(value, definition, cancellationToken);
        }

        _metrics.RecordInlineUpdate(definition.TenantId.ToString(), ProjectionName, "definition", values.Count);
    }

    public async Task RemoveForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        await _dbContext.EventSessionCustomPropertyProjections
            .Where(p => p.EventSessionCustomPropertyDefinitionId == definitionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RefreshForEventSessionAsync(Guid eventSessionId, CancellationToken cancellationToken)
    {
        await _dbContext.EventSessionCustomPropertyProjections
            .Where(p => p.EventSessionId == eventSessionId)
            .ExecuteDeleteAsync(cancellationToken);

        var values = await _dbContext.EventSessionCustomPropertyValues
            .Include(v => v.Definition)
            .Where(v => v.EventSessionId == eventSessionId)
            .ToListAsync(cancellationToken);

        foreach (var value in values)
        {
            if (value.Definition is null)
            {
                continue;
            }

            var row = await BuildProjectionRowAsync(value, value.Definition, cancellationToken);
            _dbContext.EventSessionCustomPropertyProjections.Add(row);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProjectionRebuildResult> RebuildForTenantAsync(
        Guid tenantId,
        int? batchSize,
        CancellationToken cancellationToken)
    {
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

            var sessionIds = await _dbContext.EventSessions
                .Where(s => s.TenantId == tenantId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            foreach (var chunk in ProjectionInfrastructure.Chunk(sessionIds, effectiveBatchSize))
            {
                foreach (var sessionId in chunk)
                {
                    try
                    {
                        await RefreshForEventSessionAsync(sessionId, cancellationToken);
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
                if (row.ScopeType != CustomPropertyProjectionScopeType.EventSession)
                {
                    continue;
                }

                await RefreshForEventSessionAsync(row.ScopeId, cancellationToken);
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
        EventSessionCustomPropertyValue value,
        EventSessionCustomPropertyDefinition definition,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.EventSessionCustomPropertyProjections
            .FirstOrDefaultAsync(p => p.EventSessionCustomPropertyValueId == value.Id, cancellationToken);

        if (existing is null)
        {
            _dbContext.EventSessionCustomPropertyProjections.Add(
                await BuildProjectionRowAsync(value, definition, cancellationToken));
            return;
        }

        await ApplyProjectionFieldsAsync(existing, value, definition, cancellationToken);
    }

    private async Task RemoveProjectionForValueAsync(Guid valueId, CancellationToken cancellationToken)
    {
        await _dbContext.EventSessionCustomPropertyProjections
            .Where(p => p.EventSessionCustomPropertyValueId == valueId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpsertDirtyScopeSkipAsync(
        Guid tenantId,
        Guid eventSessionId,
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
                "Session custom-property projection dirty-scope backlog exceeded the tenant quota. Run rebuild or drain before retrying.",
                CustomPropertyQuotaSettingDefinitions.MaxDirtyScopePendingPerTenant.Key,
                quota,
                pendingCount,
                pendingCount + 1,
                "event_session_custom_property_projection_dirty_scope",
                tenantId);
        }

        await _dirtyScopeRepository.UpsertAsync(
            ProjectionName,
            ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.EventSession,
            eventSessionId,
            definitionId,
            reason,
            cancellationToken);
    }

    private async Task<EventSessionCustomPropertyProjection> BuildProjectionRowAsync(
        EventSessionCustomPropertyValue value,
        EventSessionCustomPropertyDefinition definition,
        CancellationToken cancellationToken)
    {
        var row = new EventSessionCustomPropertyProjection
        {
            EventSessionCustomPropertyDefinitionId = definition.Id,
            EventSessionCustomPropertyValueId = value.Id,
            EventSessionId = value.EventSessionId,
            TenantId = value.TenantId,
            Namespace = definition.Namespace,
            Key = definition.Key,
        };

        await ApplyProjectionFieldsAsync(row, value, definition, cancellationToken);
        return row;
    }

    private async Task ApplyProjectionFieldsAsync(
        EventSessionCustomPropertyProjection row,
        EventSessionCustomPropertyValue value,
        EventSessionCustomPropertyDefinition definition,
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

        var tracked = _dbContext.EventSessionCustomPropertyOptions.Local.FirstOrDefault(o => o.Id == id);
        if (tracked is not null)
        {
            return tracked.Value;
        }

        return await _dbContext.EventSessionCustomPropertyOptions
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
