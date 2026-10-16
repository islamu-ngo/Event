using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class SupportAccessTemporalQueries
{
    internal static Task<SupportAccessSession?> GetActiveForActorAsync(
        ExploreDbContext context, Guid actorUserId, DateTimeOffset nowUtc, CancellationToken token)
    {
        if (context.Database.IsSqlite())
        {
            var sql = new HumanPrincipalSourceSql(context);
            Type type = typeof(SupportAccessSession);
            string started = SqliteSupportAccessClockSql.Ticks(sql.Column(type, nameof(SupportAccessSession.StartedAtUtc)));
            string expires = SqliteSupportAccessClockSql.Ticks(sql.Column(type, nameof(SupportAccessSession.ExpiresAtUtc)));
            return context.SupportAccessSessions.FromSqlRaw(
                $"""
                SELECT * FROM {sql.Table(type)}
                WHERE {sql.Column(type, nameof(SupportAccessSession.ActorUserId))}=@actor
                    AND {sql.Column(type, nameof(SupportAccessSession.StatusId))}=@status
                    AND {sql.Column(type, nameof(SupportAccessSession.EndedAtUtc))} IS NULL
                    AND {started}<=@now AND @now<{expires}
                ORDER BY {started} DESC LIMIT 1
                """,
                new SqliteParameter("actor", actorUserId),
                new SqliteParameter("status", (int)SupportAccessSessionStatusEnum.Active),
                new SqliteParameter("now", SqliteSupportAccessClockSql.Ticks(nowUtc)))
                .FirstOrDefaultAsync(token);
        }
        return context.SupportAccessSessions
            .Where(session => session.ActorUserId == actorUserId)
            .Where(session => session.StatusId == (int)SupportAccessSessionStatusEnum.Active)
            .Where(session => session.EndedAtUtc == null)
            .Where(session => session.StartedAtUtc <= nowUtc && nowUtc < session.ExpiresAtUtc)
            .OrderByDescending(session => session.StartedAtUtc)
            .FirstOrDefaultAsync(token);
    }

    internal static async Task<SupportAccessSession?> GetActiveOwnedSessionAsync(
        ExploreDbContext context, Guid sessionId, Guid actorUserId, Guid? targetTenantId,
        DateTimeOffset nowUtc, CancellationToken token)
    {
        if (context.Database.IsSqlite())
        {
            var session = await context.SupportAccessSessions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == sessionId && row.ActorUserId == actorUserId
                    && (!targetTenantId.HasValue || row.TargetTenantId == targetTenantId.Value), token);
            return session is not null && session.IsActiveAt(nowUtc) ? session : null;
        }
        IQueryable<SupportAccessSession> query = context.SupportAccessSessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Where(session => session.ActorUserId == actorUserId)
            .Where(session => session.StatusId == (int)SupportAccessSessionStatusEnum.Active)
            .Where(session => session.EndedAtUtc == null)
            .Where(session => session.StartedAtUtc <= nowUtc && nowUtc < session.ExpiresAtUtc);

        if (targetTenantId.HasValue)
            query = query.Where(session => session.TargetTenantId == targetTenantId.Value);

        return await query.FirstOrDefaultAsync(token);
    }

    internal static async Task<bool> HasActiveSessionForActorAsync(
        ExploreDbContext context, Guid actorUserId, DateTimeOffset nowUtc, CancellationToken token)
    {
        if (context.Database.IsSqlite())
            return await GetActiveForActorAsync(context, actorUserId, nowUtc, token) is not null;
        return await context.SupportAccessSessions
            .AnyAsync(
                session => session.ActorUserId == actorUserId
                    && session.StatusId == (int)SupportAccessSessionStatusEnum.Active
                    && session.EndedAtUtc == null
                    && session.StartedAtUtc <= nowUtc
                    && nowUtc < session.ExpiresAtUtc,
                token);
    }

    internal static async Task<IReadOnlyList<SupportAccessSession>> ListSessionsForTargetTenantAsync(
        ExploreDbContext context, Guid targetTenantId, int boundedLimit, CancellationToken token)
    {
        if (context.Database.IsSqlite())
        {
            var sql = new HumanPrincipalSourceSql(context);
            Type type = typeof(SupportAccessSession);
            string started = SqliteSupportAccessClockSql.Ticks(sql.Column(type, nameof(SupportAccessSession.StartedAtUtc)));
            return await context.SupportAccessSessions.FromSqlRaw(
                $"""
                SELECT * FROM {sql.Table(type)}
                WHERE {sql.Column(type, nameof(SupportAccessSession.TargetTenantId))}=@tenant
                ORDER BY {started} DESC LIMIT @limit
                """,
                new SqliteParameter("tenant", targetTenantId), new SqliteParameter("limit", boundedLimit))
                .AsNoTracking().ToListAsync(token);
        }
        return await context.SupportAccessSessions
            .AsNoTracking()
            .Where(session => session.TargetTenantId == targetTenantId)
            .OrderByDescending(session => session.StartedAtUtc)
            .Take(boundedLimit)
            .ToListAsync(token);
    }

    internal static async Task<IReadOnlyList<SupportAccessAuditEvent>> ListAuditForSessionAsync(
        ExploreDbContext context, Guid sessionId, int boundedLimit, CancellationToken token)
    {
        if (context.Database.IsSqlite())
        {
            var sql = new HumanPrincipalSourceSql(context);
            Type type = typeof(SupportAccessAuditEvent);
            string occurred = SqliteSupportAccessClockSql.Ticks(sql.Column(type, nameof(SupportAccessAuditEvent.OccurredAtUtc)));
            return await context.SupportAccessAuditEvents.FromSqlRaw(
                $"""
                SELECT * FROM {sql.Table(type)}
                WHERE {sql.Column(type, nameof(SupportAccessAuditEvent.SupportAccessSessionId))}=@session
                ORDER BY {occurred} DESC LIMIT @limit
                """,
                new SqliteParameter("session", sessionId), new SqliteParameter("limit", boundedLimit))
                .AsNoTracking().ToListAsync(token);
        }
        return await context.SupportAccessAuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.SupportAccessSessionId == sessionId)
            .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
            .Take(boundedLimit)
            .ToListAsync(token);
    }

    internal static async Task<IReadOnlyList<SupportAccessAuditEvent>> ListAuditForTargetTenantAsync(
        ExploreDbContext context, Guid targetTenantId, int boundedLimit, CancellationToken token)
    {
        if (context.Database.IsSqlite())
        {
            var sql = new HumanPrincipalSourceSql(context);
            Type type = typeof(SupportAccessAuditEvent);
            string occurred = SqliteSupportAccessClockSql.Ticks(sql.Column(type, nameof(SupportAccessAuditEvent.OccurredAtUtc)));
            return await context.SupportAccessAuditEvents.FromSqlRaw(
                $"""
                SELECT * FROM {sql.Table(type)}
                WHERE {sql.Column(type, nameof(SupportAccessAuditEvent.TargetTenantId))}=@tenant
                ORDER BY {occurred} DESC LIMIT @limit
                """,
                new SqliteParameter("tenant", targetTenantId), new SqliteParameter("limit", boundedLimit))
                .AsNoTracking().ToListAsync(token);
        }
        return await context.SupportAccessAuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.TargetTenantId == targetTenantId)
            .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
            .Take(boundedLimit)
            .ToListAsync(token);
    }
}
