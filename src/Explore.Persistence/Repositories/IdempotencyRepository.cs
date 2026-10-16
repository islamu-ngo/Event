using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class IdempotencyRepository(
    ExploreDbContext dbContext, TimeProvider? clock = null,
    PostgresNativeQueueEnrollment? enrollment = null) : IIdempotencyRepository
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private ExploreDbContext Context => dbContext;

    public async Task<IdempotencyLookup> LookupAsync(
        IdempotencyRequestDescriptor request, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var context = Context;
        if (RequireTenant(context) != request.TenantId)
            throw new UnauthorizedAccessException("Receipt request does not match the selected tenant.");
        bool opened = await OpenAsync(context, cancellationToken);
        try
        {
            var native = await PostgresIdempotencyReceiptContract.TryReadAsync(
                context, RequireEnrollment, request, utcNow, cancellationToken);
            if (native is not null) return native;
            var record = await context.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
                row => row.TenantId == request.TenantId && row.Key == request.Key, cancellationToken);
            if (record is null)
                return new(IdempotencyLookupOutcome.Absent, null);
            if (IdempotencyRequestDescriptor.FromRecord(record) != request)
                return new(IdempotencyLookupOutcome.RequestMismatch, null);
            if (record.ExpiresAt <= utcNow && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
            {
                await ScrubOneAsync(context, record.Id, utcNow, cancellationToken);
                record.StatusCode = IdempotencyRecord.ResponseUnavailableStatusCode;
                record.ResponseBody = null;
                record.ContentType = null;
            }
            return new(IdempotencyLookupOutcome.Existing, record);
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }

    public async Task<IdempotencyClaim> TryClaimAsync(
        IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        var context = Context;
        var proposal = Snapshot(record);
        if (RequireTenant(context) != proposal.TenantId)
            throw new UnauthorizedAccessException("Receipt claim does not match the selected tenant.");
        if (proposal.Id == Guid.Empty)
            proposal.Id = Guid.CreateVersion7();
        if (proposal.StatusCode != IdempotencyRecord.InProgressStatusCode
            || proposal.ResponseBody is not null || proposal.ContentType is not null)
            throw new ArgumentException("A claim must begin without a stored response.", nameof(record));
        bool opened = await OpenAsync(context, cancellationToken);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        bool retained = false;
        try
        {
            IdempotencyLookup lookup;
            bool inserted;
            var native = await PostgresIdempotencyReceiptContract.TryClaimAsync(
                context, RequireEnrollment, proposal, nonce, cancellationToken);
            if (native is not null)
            {
                lookup = native.Lookup;
                inserted = native.IsOwner;
            }
            else
            {
                var request = IdempotencyRequestDescriptor.FromRecord(proposal);
                lookup = await LookupAsync(request, proposal.CreatedAt, cancellationToken);
                inserted = false;
                if (lookup.Outcome == IdempotencyLookupOutcome.Absent)
                {
                    try
                    {
                        await IdempotencyReceiptInsert.InsertAsync(context, proposal, cancellationToken);
                        inserted = true;
                        lookup = new(IdempotencyLookupOutcome.Existing, proposal);
                    }
                    catch (Exception exception) when (IdempotencyReceiptInsert.IsUniqueViolation(exception))
                    {
                        lookup = await LookupAsync(request, proposal.CreatedAt, cancellationToken);
                        if (lookup.Outcome == IdempotencyLookupOutcome.Absent)
                            throw;
                    }
                }
            }
            if (!inserted)
                return new(lookup, null);
            var stored = lookup.Record
                ?? throw new InvalidOperationException("An owning acquisition must retain its receipt.");
            var owner = new ClaimOwner(this, context, stored, nonce, opened);
            retained = true;
            return new(lookup, owner);
        }
        finally
        {
            if (!retained)
            {
                CryptographicOperations.ZeroMemory(nonce);
                if (opened) await context.Database.CloseConnectionAsync();
            }
        }
    }

    public async Task<bool> CompleteAsync(
        IIdempotencyClaimOwner owner, int statusCode, string? responseBody, string? contentType,
        CancellationToken cancellationToken = default)
    {
        if (owner is not ClaimOwner captured || !ReferenceEquals(captured.Repository, this))
            throw new UnauthorizedAccessException("The original receipt acquisition is required.");
        captured.Require();
        if ((statusCode != IdempotencyRecord.ResponseUnavailableStatusCode && statusCode < 100)
            || statusCode < IdempotencyRecord.ResponseUnavailableStatusCode
            || statusCode > 599
            || (statusCode == IdempotencyRecord.ResponseUnavailableStatusCode
                && (responseBody is not null || contentType is not null)))
            throw new ArgumentException("The receipt completion state is invalid.", nameof(statusCode));
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        bool completed;
        var native = await PostgresIdempotencyReceiptContract.TryCompleteAsync(
            captured.Context, RequireEnrollment, captured.Request.TenantId, captured.RecordId,
            captured.Nonce, statusCode, responseBody, contentType, now, cancellationToken);
        if (native is { } nativeCompleted)
            completed = nativeCompleted;
        else
        {
            var request = captured.Request;
            var query = captured.Context.IdempotencyRecords.Where(row =>
                row.Id == captured.RecordId && row.TenantId == request.TenantId && row.Key == request.Key
                && row.UserId == request.UserId && row.RequestMethod == request.RequestMethod
                && row.RequestTarget == request.RequestTarget && row.RequestContentType == request.RequestContentType
                && row.RequestBodyHash == request.RequestBodyHash
                && row.PrincipalFingerprint == request.PrincipalFingerprint
                && row.StatusCode == IdempotencyRecord.InProgressStatusCode && row.ExpiresAt > now);
            if (captured.Context.Database.IsRelational())
                completed = await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.StatusCode, statusCode)
                    .SetProperty(row => row.ResponseBody, responseBody)
                    .SetProperty(row => row.ContentType, contentType), cancellationToken) == 1;
            else
            {
                var row = await query.SingleOrDefaultAsync(cancellationToken);
                completed = row is not null;
                if (row is not null)
                {
                    row.StatusCode = statusCode;
                    row.ResponseBody = responseBody;
                    row.ContentType = contentType;
                    await captured.Context.SaveChangesAsync(cancellationToken);
                }
            }
        }
        captured.Require();
        if (completed) captured.Spent = true;
        return completed;
    }

    public async Task<int> CountExpiredAsync(
        DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        ValidateBudget(batchSize);
        var context = Context;
        Guid tenant = RequireTenant(context);
        bool opened = await OpenAsync(context, cancellationToken);
        try
        {
            var native = await PostgresIdempotencyReceiptContract.TryCountExpiredAsync(
                context, RequireEnrollment, tenant, expiresBeforeUtc, batchSize, cancellationToken);
            if (native is { } nativeCount) return nativeCount;
            return await context.IdempotencyRecords.Where(row => row.TenantId == tenant && row.ExpiresAt <= expiresBeforeUtc
                    && row.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
                .OrderBy(row => row.ExpiresAt).ThenBy(row => row.Id).Take(batchSize).CountAsync(cancellationToken);
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }

    public async Task<int> ScrubExpiredAsync(
        DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        ValidateBudget(batchSize);
        var context = Context;
        Guid tenant = RequireTenant(context);
        bool opened = await OpenAsync(context, cancellationToken);
        try
        {
            var native = await PostgresIdempotencyReceiptContract.TryScrubExpiredAsync(
                context, RequireEnrollment, tenant, expiresBeforeUtc, batchSize, cancellationToken);
            if (native is { } nativeCount) return nativeCount;
            var ids = context.IdempotencyRecords.Where(row => row.TenantId == tenant && row.ExpiresAt <= expiresBeforeUtc
                    && row.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
                .OrderBy(row => row.ExpiresAt).ThenBy(row => row.Id).Take(batchSize).Select(row => row.Id);
            return await context.IdempotencyRecords.Where(row => row.TenantId == tenant && ids.Contains(row.Id)
                    && row.ExpiresAt <= expiresBeforeUtc
                    && row.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.StatusCode, IdempotencyRecord.ResponseUnavailableStatusCode)
                    .SetProperty(row => row.ResponseBody, (string?)null)
                    .SetProperty(row => row.ContentType, (string?)null), cancellationToken);
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }

    private PostgresNativeQueueEnrollment RequireEnrollment() => enrollment
        ?? throw new InvalidOperationException("Native receipt enrollment is required.");

    private static Guid RequireTenant(ExploreDbContext context) => !context.IsTenantFilterBypassed
        && context.BindTenantLease() is { } tenant
        && tenant != Guid.Empty ? tenant
        : throw new UnauthorizedAccessException("Receipt execution requires its selected tenant.");

    private static void ValidateBudget(int budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(budget, IdempotencyCleanupLimits.MaximumRows);
    }

    private static async Task<bool> OpenAsync(ExploreDbContext context, CancellationToken token)
    {
        if (!context.Database.IsRelational()
            || RelationalConnectionIdentity.Capture(context).State == ConnectionState.Open) return false;
        await context.Database.OpenConnectionAsync(token);
        return true;
    }

    private static async Task ScrubOneAsync(ExploreDbContext context, Guid id, DateTime cutoff, CancellationToken token)
    {
        var query = context.IdempotencyRecords.Where(row => row.Id == id && row.ExpiresAt <= cutoff
            && row.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode);
        if (context.Database.IsRelational())
            await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.StatusCode, IdempotencyRecord.ResponseUnavailableStatusCode)
                .SetProperty(row => row.ResponseBody, (string?)null)
                .SetProperty(row => row.ContentType, (string?)null), token);
        else
        {
            var row = await query.SingleOrDefaultAsync(token);
            if (row is not null)
            {
                row.StatusCode = IdempotencyRecord.ResponseUnavailableStatusCode;
                row.ResponseBody = null;
                row.ContentType = null;
                await context.SaveChangesAsync(token);
            }
        }
    }

    private static IdempotencyRecord Snapshot(IdempotencyRecord record) => new()
    {
        Id = record.Id,
        TenantId = record.TenantId,
        Key = record.Key,
        UserId = record.UserId,
        RequestMethod = record.RequestMethod,
        RequestTarget = record.RequestTarget,
        RequestContentType = record.RequestContentType,
        RequestBodyHash = record.RequestBodyHash,
        PrincipalFingerprint = record.PrincipalFingerprint,
        StatusCode = record.StatusCode,
        ResponseBody = record.ResponseBody,
        ContentType = record.ContentType,
        CreatedAt = record.CreatedAt,
        ExpiresAt = record.ExpiresAt
    };

    private sealed class ClaimOwner : IIdempotencyClaimOwner
    {
        private readonly DbContextId _contextId;
        private readonly Guid? _selectedTenant;
        private readonly string? _schema;
        private readonly RelationalConnectionIdentity? _connection;
        private readonly DbTransaction? _transaction;
        private readonly Guid? _transactionId;
        private readonly int? _backend;
        private readonly bool _opened;
        private bool _disposed;
        private bool _connectionClosed;
        internal bool Spent { get; set; }
        internal IdempotencyRepository Repository { get; }
        internal ExploreDbContext Context { get; }
        internal IdempotencyRequestDescriptor Request { get; }
        internal Guid RecordId { get; }
        internal byte[] Nonce { get; }

        internal ClaimOwner(IdempotencyRepository repository, ExploreDbContext context,
            IdempotencyRecord record, byte[] nonce, bool opened)
        {
            Repository = repository;
            Context = context;
            Request = IdempotencyRequestDescriptor.FromRecord(record);
            RecordId = record.Id;
            Nonce = nonce;
            _opened = opened;
            _contextId = context.ContextId;
            _selectedTenant = context.TenantFilterTenantId;
            _schema = context.Model.GetDefaultSchema();
            if (context.Database.IsRelational())
            {
                _connection = RelationalConnectionIdentity.Capture(context);
                _connection.StateChange += ObserveConnection;
                _transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                _transactionId = context.Database.CurrentTransaction?.TransactionId;
                _backend = _connection.BackendProcessId;
            }
        }

        internal void Require()
        {
            if (_disposed || Spent || _connectionClosed || Context.ContextId != _contextId
                || RequireTenant(Context) != Request.TenantId
                || Context.TenantFilterTenantId != _selectedTenant || Context.Model.GetDefaultSchema() != _schema
                || (_connection is not null && (!_connection.Matches(Context)
                    || _connection.State != ConnectionState.Open
                    || !ReferenceEquals(Context.Database.CurrentTransaction?.GetDbTransaction(), _transaction)
                    || Context.Database.CurrentTransaction?.TransactionId != _transactionId
                    || (_connection.BackendProcessId is { } processId && processId != _backend))))
                throw new UnauthorizedAccessException("Original receipt ownership changed.");
        }

        private void ObserveConnection(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) _connectionClosed = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CryptographicOperations.ZeroMemory(Nonce);
            if (_connection is not null) _connection.StateChange -= ObserveConnection;
            if (_opened && Context.ContextId == _contextId
                && _connection!.Matches(Context))
                Context.Database.CloseConnection();
        }
    }
}
