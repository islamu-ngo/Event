// ABOUTME: Persists encrypted ATProto authentication transients in the instance relational database.
// ABOUTME: Uses non-retrying conditional deletion so only the durably committed single winner receives payload.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
namespace Explore.Persistence.Repositories;

public sealed class AtprotoTransientStoreRepository(ExploreDbContext dbContext, TimeProvider timeProvider)
    : IAtprotoTransientStoreRepository
{
    private const int MaximumDeleteBatchSize = 500;

    public Task<bool> TryCreateAsync(AtprotoTransientRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Purpose == AtprotoTransientPurpose.HealthProbe || !record.TenantId.HasValue || record.TenantId == Guid.Empty)
            throw new ArgumentException("Ordinary transient creation requires an authentication purpose and tenant.", nameof(record));
        return TryInsertAsync(record, cancellationToken);
    }

    public Task<bool> TryCreateHealthProbeAsync(AtprotoTransientRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Purpose != AtprotoTransientPurpose.HealthProbe || record.TenantId.HasValue)
            throw new ArgumentException("Only a dedicated tenantless health-probe record is accepted.", nameof(record));
        return TryInsertAsync(record, cancellationToken);
    }

    public async Task<AtprotoTransientRecord?> ReadOAuthStateAsync(string tokenDigest, CancellationToken cancellationToken = default)
    {
        EnsureRelational();
        ValidateDigest(tokenDigest);
        long now = Now();
        return await dbContext.AtprotoTransientRecords.AsNoTracking().SingleOrDefaultAsync(record =>
            record.Purpose == AtprotoTransientPurpose.OAuthState && record.TokenDigest == tokenDigest &&
            record.TenantId != null && record.ExpiresAtUnixMilliseconds > now, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AtprotoTransientRecord?> ReadAsync(AtprotoTransientPurpose purpose, string tokenDigest, Guid tenantId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticationBinding(purpose, tokenDigest, tenantId);
        long now = Now();
        return await dbContext.AtprotoTransientRecords.AsNoTracking().SingleOrDefaultAsync(record =>
            record.Purpose == purpose && record.TokenDigest == tokenDigest && record.TenantId == tenantId &&
            record.ExpiresAtUnixMilliseconds > now, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AtprotoTransientRecord?> ConsumeAsync(Guid candidateId, AtprotoTransientPurpose purpose, string tokenDigest, Guid tenantId, CancellationToken cancellationToken = default)
    {
        EnsureNoAmbientTransaction();
        EnsureAuthenticationBinding(purpose, tokenDigest, tenantId);
        if (candidateId == Guid.Empty) throw new ArgumentException("A candidate identity is required.", nameof(candidateId));
        long readAt = Now();
        AtprotoTransientRecord? candidate = await dbContext.AtprotoTransientRecords.AsNoTracking().SingleOrDefaultAsync(record =>
            record.Id == candidateId && record.Purpose == purpose && record.TokenDigest == tokenDigest &&
            record.TenantId == tenantId && record.ExpiresAtUnixMilliseconds > readAt, cancellationToken).ConfigureAwait(false);
        if (candidate is null) return null;

        int deleted = await ExecuteConditionalDeleteAsync(
            candidate.Id,
            purpose,
            tokenDigest,
            tenantId,
            Now(),
            cancellationToken).ConfigureAwait(false);
        return deleted == 1 ? candidate : null;
    }

    public async Task<AtprotoTransientRecord?> ReadHealthProbeAsync(Guid candidateId, string tokenDigest, CancellationToken cancellationToken = default)
    {
        EnsureRelational();
        ValidateDigest(tokenDigest);
        if (candidateId == Guid.Empty) throw new ArgumentException("A candidate identity is required.", nameof(candidateId));
        long now = Now();
        return await dbContext.AtprotoTransientRecords.AsNoTracking().SingleOrDefaultAsync(record =>
            record.Id == candidateId && record.Purpose == AtprotoTransientPurpose.HealthProbe
            && record.TokenDigest == tokenDigest && record.TenantId == null
            && record.ExpiresAtUnixMilliseconds > now, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ConsumeHealthProbeAsync(Guid candidateId, string tokenDigest, CancellationToken cancellationToken = default)
    {
        EnsureNoAmbientTransaction();
        ValidateDigest(tokenDigest);
        if (candidateId == Guid.Empty) throw new ArgumentException("A candidate identity is required.", nameof(candidateId));
        int deleted = await ExecuteConditionalDeleteAsync(
            candidateId,
            AtprotoTransientPurpose.HealthProbe,
            tokenDigest,
            tenantId: null,
            Now(),
            cancellationToken).ConfigureAwait(false);
        return deleted == 1;
    }

    public async Task<int> DeleteExpiredAsync(long expiresAtOrBeforeUnixMilliseconds, int batchSize, CancellationToken cancellationToken = default)
    {
        EnsureRelational();
        ValidateBatchSize(batchSize);
        Guid[] ids = await dbContext.AtprotoTransientRecords.AsNoTracking().Where(record =>
            record.ExpiresAtUnixMilliseconds <= expiresAtOrBeforeUnixMilliseconds)
            .OrderBy(record => record.ExpiresAtUnixMilliseconds).ThenBy(record => record.Id)
            .Select(record => record.Id).Take(batchSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return await AtprotoTransientCleanupDelete.ExecuteAsync<AtprotoTransientRecord>(dbContext, ids, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> TryInsertAsync(AtprotoTransientRecord record, CancellationToken cancellationToken)
    {
        EnsureRelational();
        if (record.ExpiresAtUnixMilliseconds <= Now()) return false;
        dbContext.AtprotoTransientRecords.Add(record);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (AtprotoTransientUniqueConflictClassifier.IsTransientLocatorConflict(dbContext, exception))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    private async Task<int> ExecuteConditionalDeleteAsync(
        Guid candidateId,
        AtprotoTransientPurpose purpose,
        string tokenDigest,
        Guid? tenantId,
        long expiresAfter,
        CancellationToken cancellationToken)
    {
        // Execute the parameterized relational command directly: ExecuteDeleteAsync
        // can enter the provider's retry strategy even inside a non-retrying wrapper.
        // A lost commit response must propagate rather than repeat the deletion.
        IEntityType entity = dbContext.Model.FindEntityType(typeof(AtprotoTransientRecord))
            ?? throw new InvalidOperationException("The ATProto transient relational mapping is unavailable.");
        string tableName = entity.GetTableName()
            ?? throw new InvalidOperationException("The ATProto transient table mapping is unavailable.");
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        ISqlGenerationHelper sql = dbContext.GetService<ISqlGenerationHelper>();
        string Table() => sql.DelimitIdentifier(tableName, entity.GetSchema());
        string Column(string property) => sql.DelimitIdentifier(
            entity.FindProperty(property)?.GetColumnName(table)
                ?? throw new InvalidOperationException($"The ATProto transient {property} column mapping is unavailable."));
        string tenantPredicate = tenantId.HasValue
            ? $"{Column(nameof(AtprotoTransientRecord.TenantId))} = {{3}}"
            : $"{Column(nameof(AtprotoTransientRecord.TenantId))} IS NULL";
        string commandText =
            $"DELETE FROM {Table()} " +
            $"WHERE {Column(nameof(AtprotoTransientRecord.Id))} = {{0}} " +
            $"AND {Column(nameof(AtprotoTransientRecord.Purpose))} = {{1}} " +
            $"AND {Column(nameof(AtprotoTransientRecord.TokenDigest))} = {{2}} " +
            $"AND {tenantPredicate} " +
            $"AND {Column(nameof(AtprotoTransientRecord.ExpiresAtUnixMilliseconds))} > {{{(tenantId.HasValue ? 4 : 3)}}}";
        object[] values = tenantId.HasValue
            ? [candidateId, (int)purpose, tokenDigest, tenantId.Value, expiresAfter]
            : [candidateId, (int)purpose, tokenDigest, expiresAfter];
        IRawSqlCommandBuilder commandBuilder = dbContext.GetService<IRawSqlCommandBuilder>();
        RawSqlCommand command = commandBuilder.Build(commandText, values, dbContext.Model);
        var parameters = new RelationalCommandParameterObject(
            dbContext.GetService<IRelationalConnection>(),
            command.ParameterValues,
            readerColumns: null,
            dbContext,
            dbContext.GetService<IRelationalCommandDiagnosticsLogger>(),
            CommandSource.ExecuteSqlRaw);
        return await command.RelationalCommand.ExecuteNonQueryAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureAuthenticationBinding(AtprotoTransientPurpose purpose, string tokenDigest, Guid tenantId)
    {
        EnsureRelational();
        if (purpose is not (AtprotoTransientPurpose.OAuthState or AtprotoTransientPurpose.TenantHandoff)) throw new ArgumentOutOfRangeException(nameof(purpose));
        ValidateDigest(tokenDigest);
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
    }

    private void EnsureNoAmbientTransaction()
    {
        EnsureRelational();
        if (dbContext.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null
            || dbContext.GetService<IDbContextTransactionManager>() is ITransactionEnlistmentManager { EnlistedTransaction: not null })
            throw new InvalidOperationException("ATProto transient consumption owns its commit boundary and rejects ambient transactions.");
    }

    private void EnsureRelational()
    {
        if (!dbContext.Database.IsRelational()) throw new InvalidOperationException("ATProto transient storage requires a relational provider.");
    }

    private static void ValidateDigest(string digest)
    {
        if (digest is null || digest.Length != AtprotoTransientRecord.Sha256DigestLength || digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase hexadecimal SHA-256 digest is required.", nameof(digest));
    }

    private static void ValidateBatchSize(int batchSize)
    {
        if (batchSize is < 1 or > MaximumDeleteBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
    }

    private long Now() => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
}
