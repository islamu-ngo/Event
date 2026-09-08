using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.EventReports;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventReportRepository : GenericRepository<EventReport, Guid>, IEventReportRepository
{
    private const int MaxListLimit = 200;
    private const int MaxPageSize = 100;

    private readonly ExploreDbContext _dbContext;

    public EventReportRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventReport?> GetByIdAsync(
        Guid tenantId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        return await IncludeSafeReportGraph(_dbContext.EventReports
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTrackingWithIdentityResolution())
            .AsSplitQuery()
            .Where(report => report.TenantId == tenantId && report.Id == reportId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<EventReport?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        return await IncludeSafeReportGraph(_dbContext.EventReports
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate))
            .AsSplitQuery()
            .Where(report => report.TenantId == tenantId && report.Id == reportId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task PersistDecisionCaptureAsync(
        EventReport report,
        EventReportDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(decision);
        if (_dbContext.Entry(report).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Decision capture requires the tracked report aggregate.");
        }

        await _dbContext.EventReportDecisions.AddAsync(decision, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<EventReport?> GetByIdWithEvidenceAsync(
        Guid tenantId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        return await IncludeReportGraphWithEvidence(_dbContext.EventReports
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTrackingWithIdentityResolution())
            .AsSplitQuery()
            .Where(report => report.TenantId == tenantId && report.Id == reportId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventReport>> GetByEventAsync(
        Guid tenantId,
        Guid eventId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var boundedLimit = Math.Min(limit, MaxListLimit);

        return await _dbContext.EventReports
            .AsNoTracking()
            .Where(report => report.TenantId == tenantId && report.EventId == eventId)
            .OrderByDescending(report => report.CreatedAt)
            .ThenByDescending(report => report.Id)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<EventReport> Items, int TotalCount)> GetByReporterAsync(
        Guid tenantId,
        Guid reporterUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var (boundedPageNumber, boundedPageSize) = NormalizePaging(pageNumber, pageSize);

        var query = _dbContext.EventReports
            .AsNoTracking()
            .Where(report => report.TenantId == tenantId && report.ReporterUserId == reporterUserId)
            .OrderByDescending(report => report.CreatedAt)
            .ThenByDescending(report => report.Id);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((boundedPageNumber - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(List<EventReport> Items, int TotalCount)> GetReportQueueAsync(
        Guid tenantId,
        int pageNumber,
        int pageSize,
        EventReportQuerySpecification specification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var (boundedPageNumber, boundedPageSize) = NormalizePaging(pageNumber, pageSize);

        IQueryable<EventReport> query = _dbContext.EventReports
            .AsNoTracking()
            .Where(report => report.TenantId == tenantId);

        query = specification.Apply(query);
        query = ApplyDefaultQueueSort(query, specification);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await IncludeSafeReportGraph(query.AsNoTrackingWithIdentityResolution())
            .AsSplitQuery()
            .Skip((boundedPageNumber - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<bool> ExistsByReporterAndEventAsync(
        Guid tenantId,
        Guid eventId,
        Guid? reporterUserId,
        Guid? reporterActorId,
        string? reporterIpHash,
        string? reporterUserAgentHash,
        string reasonCode,
        string? subcategoryCode,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return false;
        }

        string? normalizedSubcategory = string.IsNullOrWhiteSpace(subcategoryCode)
            ? null
            : subcategoryCode.Trim();
        var query = ApplyReporterIdentity(
            _dbContext.EventReports
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking()
                .Where(report => report.TenantId == tenantId
                    && report.EventId == eventId
                    && report.ReasonCode == reasonCode.Trim()
                    && report.SubcategoryCode == normalizedSubcategory
                    && report.CreatedAt >= createdAfterUtc),
            reporterUserId,
            reporterActorId,
            reporterIpHash,
            reporterUserAgentHash);

        return query is not null && await query.AnyAsync(cancellationToken);
    }

    public async Task<int> CountByReporterSinceAsync(
        Guid tenantId,
        Guid? reporterUserId,
        Guid? reporterActorId,
        string? reporterIpHash,
        string? reporterUserAgentHash,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken)
    {
        var query = ApplyReporterIdentity(
            _dbContext.EventReports
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking()
                .Where(report => report.TenantId == tenantId && report.CreatedAt >= createdAfterUtc),
            reporterUserId,
            reporterActorId,
            reporterIpHash,
            reporterUserAgentHash);

        return query is null ? 0 : await query.CountAsync(cancellationToken);
    }

    public async Task<int> CountByReporterAndEventSinceAsync(
        Guid tenantId,
        Guid eventId,
        Guid? reporterUserId,
        Guid? reporterActorId,
        string? reporterIpHash,
        string? reporterUserAgentHash,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken)
    {
        var query = ApplyReporterIdentity(
            _dbContext.EventReports
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AsNoTracking()
                .Where(report => report.TenantId == tenantId
                    && report.EventId == eventId
                    && report.CreatedAt >= createdAfterUtc),
            reporterUserId,
            reporterActorId,
            reporterIpHash,
            reporterUserAgentHash);

        return query is null ? 0 : await query.CountAsync(cancellationToken);
    }

    public async Task<int> CountByEventSinceAsync(
        Guid tenantId,
        Guid eventId,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventReports
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(report => report.TenantId == tenantId
                && report.EventId == eventId
                && report.CreatedAt >= createdAfterUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountByTenantAndStatusesAsync(
        Guid tenantId,
        IReadOnlyCollection<EventReportStatus> statuses,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || statuses.Count == 0)
        {
            return 0;
        }

        return await _dbContext.EventReports
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(report => report.TenantId == tenantId && statuses.Contains(report.Status))
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountCasesByTenantAndStatusesAsync(
        Guid tenantId,
        IReadOnlyCollection<EventReportCaseStatus> statuses,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || statuses.Count == 0)
        {
            return 0;
        }

        return await _dbContext.Set<EventReportCase>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(reportCase => reportCase.TenantId == tenantId && statuses.Contains(reportCase.Status))
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountExternalLinksByTenantAndSyncStateAsync(
        Guid tenantId,
        EventReportSyncState syncState,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return 0;
        }

        return await _dbContext.Set<EventReportExternalLink>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.SyncState == syncState)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountExternalLinksByTenantAndSyncStateBeforeAsync(
        Guid tenantId,
        EventReportSyncState syncState,
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return 0;
        }

        return await _dbContext.Set<EventReportExternalLink>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.SyncState == syncState && link.CreatedAt < olderThanUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountExternalLinksBySyncStateAsync(
        EventReportSyncState syncState,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Set<EventReportExternalLink>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.ControlPlaneModerationReportingOperations)
            .AsNoTracking()
            .Where(link => link.SyncState == syncState)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountExternalLinksBySyncStateBeforeAsync(
        EventReportSyncState syncState,
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Set<EventReportExternalLink>()
            .IgnoreTenantFilter(TenantFilterBypassReasons.ControlPlaneModerationReportingOperations)
            .AsNoTracking()
            .Where(link => link.SyncState == syncState && link.CreatedAt < olderThanUtc)
            .CountAsync(cancellationToken);
    }

    private static IQueryable<EventReport> IncludeSafeReportGraph(IQueryable<EventReport> query)
    {
        return query
            .Include(report => report.Targets)
            .Include(report => report.Cases)
                .ThenInclude(reportCase => reportCase.CurrentDecision)
                    .ThenInclude(decision => decision!.Execution)
            .Include(report => report.Signals)
            .Include(report => report.Decisions)
                .ThenInclude(decision => decision.Execution)
            .Include(report => report.ExternalLinks);
    }

    private static IQueryable<EventReport> IncludeReportGraphWithEvidence(IQueryable<EventReport> query)
    {
        return IncludeSafeReportGraph(query)
            .Include(report => report.EvidenceItems);
    }

    private static IQueryable<EventReport> ApplyDefaultQueueSort(
        IQueryable<EventReport> query,
        EventReportQuerySpecification specification)
    {
        return specification.HasSort
            ? query
            : query
                .OrderByDescending(report => report.Priority)
                .ThenBy(report => report.CreatedAt)
                .ThenBy(report => report.Id);
    }

    private static IQueryable<EventReport>? ApplyReporterIdentity(
        IQueryable<EventReport> query,
        Guid? reporterUserId,
        Guid? reporterActorId,
        string? reporterIpHash,
        string? reporterUserAgentHash)
    {
        if (reporterUserId.HasValue)
        {
            return query.Where(report => report.ReporterUserId == reporterUserId.Value);
        }

        if (reporterActorId.HasValue)
        {
            return query.Where(report => report.ReporterActorId == reporterActorId.Value);
        }

        if (string.IsNullOrWhiteSpace(reporterIpHash) || string.IsNullOrWhiteSpace(reporterUserAgentHash))
        {
            return null;
        }

        var normalizedIpHash = reporterIpHash.Trim();
        var normalizedUserAgentHash = reporterUserAgentHash.Trim();

        return query.Where(report => report.ReporterUserId == null
            && report.ReporterActorId == null
            && report.ReporterIpHash == normalizedIpHash
            && report.ReporterUserAgentHash == normalizedUserAgentHash);
    }

    private static (int PageNumber, int PageSize) NormalizePaging(int pageNumber, int pageSize)
    {
        return (Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, MaxPageSize));
    }
}
