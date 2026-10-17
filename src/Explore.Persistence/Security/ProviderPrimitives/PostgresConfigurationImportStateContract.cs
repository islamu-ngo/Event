using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed instance staging operations on the sealed Global import owner. Session reads
/// lock the persisted entity in the caller's transaction; writes retain its original
/// immutable fields and use the original revision as a compare-and-swap token.
/// Protection and token authorization remain application responsibilities.
/// </summary>
public static partial class PostgresConfigurationImportStateContract
{
    public const string OwnerRole = "event_configuration_import_state_owner";
    public const string MigratorRole = "event_configuration_import_state_migrator";
    public const string GlobalRole = "event_configuration_import_state_global";

    public static async Task CreateSessionAsync(
        ExploreDbContext context, ConfigurationImportSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(context, requireTransaction: true);
        ArgumentNullException.ThrowIfNull(session);
        if (session.TargetScope != ConfigurationImportScope.Instance
            || session.TargetTenantId is not null || session.TargetAuthorityKey != "instance"
            || session.State != ConfigurationImportSessionState.Uploaded || session.Revision != 1
            || session.CreatedAt != session.UpdatedAt || session.CancelledAt is not null
            || session.ConsumedAt is not null || session.PreviewArtifactDigest is not null
            || session.PreviewTargetRevisionDigest is not null || session.PreviewSelectedSectionsDigest is not null
            || session.PreviewMappingDigest is not null || session.PreviewRequiredApprovalDigest is not null
            || session.PreviewApplyMode is not null || session.PreviewExpiresAt is not null)
            throw new ArgumentException("Only fresh instance staging state can be created.", nameof(session));
        RequireUtc(session.CreatedAt);
        RequireUtc(session.ExpiresAt);
        RequireUtc(session.ArtifactExpiresAt);
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT {FunctionSql(context, "create_session")}(@id, @handle, @digest, @length, @artifact_expiry, @token_digest, @created, @expiry)",
            new object[]
            {
                Parameter("id", NpgsqlDbType.Uuid, session.SessionId),
                Parameter("handle", NpgsqlDbType.Uuid, session.ArtifactHandleId),
                Parameter("digest", NpgsqlDbType.Text, session.ArtifactDigest),
                Parameter("length", NpgsqlDbType.Integer, session.ArtifactByteLength),
                Parameter("artifact_expiry", NpgsqlDbType.TimestampTz, session.ArtifactExpiresAt),
                Parameter("token_digest", NpgsqlDbType.Text, session.AccessTokenDigest),
                Parameter("created", NpgsqlDbType.TimestampTz, session.CreatedAt),
                Parameter("expiry", NpgsqlDbType.TimestampTz, session.ExpiresAt)
            }, cancellationToken);
    }

    public static async Task<ConfigurationImportSession?> ReadSessionAsync(
        ExploreDbContext context, Guid sessionId, CancellationToken cancellationToken = default)
    {
        ValidateOwner(context, requireTransaction: true);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        List<ConfigurationImportSession> sessions = await context.Set<ConfigurationImportSession>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "read_session")}(@id)",
                Parameter("id", NpgsqlDbType.Uuid, sessionId))
            .AsTracking().ToListAsync(cancellationToken);
        return sessions.SingleOrDefault();
    }

    public static async Task<ConfigurationImportSession?> ReadCurrentSessionAsync(
        ExploreDbContext context, Guid sessionId, CancellationToken cancellationToken = default)
    {
        ValidateOwner(context, requireTransaction: true);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        List<ConfigurationImportSession> sessions = await context.Set<ConfigurationImportSession>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "read_session")}(@id)",
                Parameter("id", NpgsqlDbType.Uuid, sessionId))
            .AsNoTracking().ToListAsync(cancellationToken);
        return sessions.SingleOrDefault();
    }

    public static async Task WriteSessionAsync(
        ExploreDbContext context, ConfigurationImportSession session, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(context, requireTransaction: true);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedRevision);
        if (session.TargetScope != ConfigurationImportScope.Instance
            || session.TargetTenantId is not null || session.TargetAuthorityKey != "instance")
            throw new ArgumentException("Only an existing instance session can be transitioned.", nameof(session));
        RequireUtc(session.ArtifactExpiresAt);
        RequireUtc(session.CreatedAt);
        RequireUtc(session.UpdatedAt);
        RequireUtc(session.ExpiresAt);
        if (session.CancelledAt is { } cancelled) RequireUtc(cancelled);
        if (session.ConsumedAt is { } consumed) RequireUtc(consumed);
        if (session.PreviewExpiresAt is { } preview) RequireUtc(preview);
        object[] values =
        [
            Parameter("id", NpgsqlDbType.Uuid, session.SessionId),
            Parameter("expected", NpgsqlDbType.Bigint, expectedRevision),
            Parameter("scope", NpgsqlDbType.Integer, (int)session.TargetScope),
            Parameter("tenant", NpgsqlDbType.Uuid, session.TargetTenantId),
            Parameter("authority", NpgsqlDbType.Text, session.TargetAuthorityKey),
            Parameter("handle", NpgsqlDbType.Uuid, session.ArtifactHandleId),
            Parameter("digest", NpgsqlDbType.Text, session.ArtifactDigest),
            Parameter("length", NpgsqlDbType.Integer, session.ArtifactByteLength),
            Parameter("artifact_expiry", NpgsqlDbType.TimestampTz, session.ArtifactExpiresAt),
            Parameter("token_digest", NpgsqlDbType.Text, session.AccessTokenDigest),
            Parameter("state", NpgsqlDbType.Integer, (int)session.State),
            Parameter("created", NpgsqlDbType.TimestampTz, session.CreatedAt),
            Parameter("updated", NpgsqlDbType.TimestampTz, session.UpdatedAt),
            Parameter("expiry", NpgsqlDbType.TimestampTz, session.ExpiresAt),
            Parameter("cancelled", NpgsqlDbType.TimestampTz, session.CancelledAt),
            Parameter("consumed", NpgsqlDbType.TimestampTz, session.ConsumedAt),
            Parameter("preview_artifact", NpgsqlDbType.Text, session.PreviewArtifactDigest),
            Parameter("preview_target", NpgsqlDbType.Text, session.PreviewTargetRevisionDigest),
            Parameter("preview_sections", NpgsqlDbType.Text, session.PreviewSelectedSectionsDigest),
            Parameter("preview_mapping", NpgsqlDbType.Text, session.PreviewMappingDigest),
            Parameter("preview_approval", NpgsqlDbType.Text, session.PreviewRequiredApprovalDigest),
            Parameter("preview_mode", NpgsqlDbType.Integer,
                session.PreviewApplyMode is { } mode ? (int)mode : null),
            Parameter("preview_expiry", NpgsqlDbType.TimestampTz, session.PreviewExpiresAt),
            Parameter("revision", NpgsqlDbType.Bigint, session.Revision)
        ];
        string arguments = string.Join(", ", values.Cast<NpgsqlParameter>()
            .Select(parameter => "@" + parameter.ParameterName));
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT {FunctionSql(context, "write_session")}({arguments})", values, cancellationToken);
        // Native persistence has written every mapped scalar. Do not let a later
        // SaveChanges issue an ordinary table UPDATE for this tracked entity.
        context.Entry(session).State = EntityState.Unchanged;
    }

    public static async Task<ConfigurationImportStoredArtifact?> ReadArtifactAsync(
        ExploreDbContext context, Guid handleId, CancellationToken cancellationToken = default)
    {
        ValidateOwner(context, requireTransaction: false);
        ArgumentOutOfRangeException.ThrowIfEqual(handleId, Guid.Empty);
        List<ConfigurationImportStoredArtifact> artifacts = await context.Set<ConfigurationImportStoredArtifact>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "read_artifact")}(@id)",
                Parameter("id", NpgsqlDbType.Uuid, handleId))
            .AsNoTracking().ToListAsync(cancellationToken);
        return artifacts.SingleOrDefault();
    }

    public static async Task StoreArtifactAsync(
        ExploreDbContext context, ConfigurationImportStoredArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(context, requireTransaction: false);
        ArgumentNullException.ThrowIfNull(artifact);
        RequireUtc(artifact.CreatedAt);
        RequireUtc(artifact.ExpiresAt);
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT {FunctionSql(context, "store_artifact")}(@id, @payload, @digest, @length, @created, @expiry)",
            new object[]
            {
                Parameter("id", NpgsqlDbType.Uuid, artifact.Id),
                Parameter("payload", NpgsqlDbType.Bytea, artifact.ProtectedPayload),
                Parameter("digest", NpgsqlDbType.Text, artifact.Sha256Digest),
                Parameter("length", NpgsqlDbType.Integer, artifact.ByteLength),
                Parameter("created", NpgsqlDbType.TimestampTz, artifact.CreatedAt),
                Parameter("expiry", NpgsqlDbType.TimestampTz, artifact.ExpiresAt)
            }, cancellationToken);
    }

    public static async Task DeleteArtifactAsync(
        ExploreDbContext context, Guid handleId, CancellationToken cancellationToken = default)
    {
        ValidateOwner(context, requireTransaction: true);
        ArgumentOutOfRangeException.ThrowIfEqual(handleId, Guid.Empty);
        await context.Database.ExecuteSqlRawAsync(
            $"SELECT {FunctionSql(context, "delete_artifact")}(@id)",
            new object[] { Parameter("id", NpgsqlDbType.Uuid, handleId) }, cancellationToken);
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.");
    }

    private static void ValidateOwner(ExploreDbContext context, bool requireTransaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null)
            throw new InvalidOperationException("Instance staging requires the sealed Global instance-import owner.");
        if (requireTransaction && context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Session staging requires the active instance-import owner transaction.");
    }
}
