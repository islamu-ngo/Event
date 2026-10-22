using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Settings;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>Admits fixed federation reads without selecting a replacement owner or executing their queries.</summary>
internal static class AtprotoFederationReadAdmission
{
    internal static string[] CopyKeys(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        string[] selected = keys.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Any(key => !AtprotoFederationSettingPolicy.IsDedicatedKey(key)))
            throw new ArgumentException("A dedicated federation setting key is required.", nameof(keys));
        return selected;
    }

    internal static void ValidateScopedInstance(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RequireBoundTenantLease();
        if (context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("federation_read_original_owner_required");
    }

    internal static void ValidateScopedTenant(ExploreDbContext context, Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("An exact tenant is required.", nameof(tenantId));
        ValidateScopedInstance(context);
        if (context.TenantFilterTenantId != tenantId)
            throw new UnauthorizedAccessException("federation_read_selected_tenant_required");
    }

    internal static Task<ReadLease> OpenScopedInstanceAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment? enrollment, CancellationToken cancellationToken) =>
        OpenAsync(context, enrollment, false, null, cancellationToken);

    internal static Task<ReadLease> OpenScopedTenantAsync(ExploreDbContext context, Guid tenantId,
        PostgresNativeQueueEnrollment? enrollment, CancellationToken cancellationToken) =>
        OpenAsync(context, enrollment, false, tenantId, cancellationToken);

    internal static Task<ReadLease> OpenGlobalAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment? enrollment, CancellationToken cancellationToken) =>
        OpenAsync(context, enrollment, true, null, cancellationToken);

    private static async Task<ReadLease> OpenAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        bool globalOnly, Guid? requiredTenant, CancellationToken cancellationToken)
    {
        if (requiredTenant is { } tenantId) ValidateScopedTenant(context, tenantId);
        else ValidateScopedInstance(context);
        if (globalOnly && context.TenantFilterTenantId is not null)
            throw new UnauthorizedAccessException("federation_read_global_owner_required");
        if (globalOnly && context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("federation_read_independent_global_owner_required");
        if (System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("federation_read_ambient_transaction_not_supported");
        PostgresAtprotoFederationSettingPermissions.ValidateModel(context);
        var lease = new ReadLease(context, enrollment, globalOnly);
        try
        {
            await lease.OpenAsync(cancellationToken);
            await PostgresAtprotoFederationSettingPermissions.RequireReadAsync(
                context, enrollment, globalOnly, cancellationToken);
            lease.Validate();
            return lease;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    /// <summary>Requires exclusive in-flight context lifetime; does not claim arbitrary concurrent repool detection.</summary>
    internal sealed class ReadLease : IAsyncDisposable
    {
        private readonly ExploreDbContext _context;
        private readonly ITenantContext? _tenantIdentity;
        private readonly Guid? _tenantId;
        private readonly DbConnection? _connection;
        private readonly DbTransaction? _transaction;
        private readonly PostgresNativeQueueEnrollment? _enrollment;
        private readonly bool _globalOnly;
        private bool _opened;
        private int? _backend;

        internal ReadLease(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment, bool globalOnly)
        {
            _context = context;
            _enrollment = enrollment;
            _globalOnly = globalOnly;
            _tenantIdentity = context.TenantContext;
            _tenantId = context.TenantFilterTenantId;
            if (context.Database.IsRelational())
            {
                _connection = context.Database.GetDbConnection();
                _transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            }
        }

        internal async Task OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_connection is not null)
            {
                if (_connection.State == ConnectionState.Closed)
                {
                    _opened = true;
                    await _context.Database.OpenConnectionAsync(cancellationToken);
                }
                if (_connection is NpgsqlConnection postgres && postgres.State == ConnectionState.Open)
                    _backend = postgres.ProcessID;
            }
            Validate();
        }

        internal async Task ValidateAsync(CancellationToken cancellationToken)
        {
            Validate();
            await PostgresAtprotoFederationSettingPermissions.RequireReadAsync(
                _context, _enrollment, _globalOnly, cancellationToken);
            Validate();
        }

        internal void Validate()
        {
            ValidateScopedInstance(_context);
            if (!ReferenceEquals(_tenantIdentity, _context.TenantContext) || _tenantId != _context.TenantFilterTenantId
                || System.Transactions.Transaction.Current is not null)
                throw new UnauthorizedAccessException("federation_read_owner_changed");
            if (_connection is not null && (
                !ReferenceEquals(_connection, _context.Database.GetDbConnection()) || _connection.State != ConnectionState.Open
                || !ReferenceEquals(_transaction, _context.Database.CurrentTransaction?.GetDbTransaction())
                || (_transaction is not null && !ReferenceEquals(_transaction.Connection, _connection))
                || (_backend is { } backend && _connection is NpgsqlConnection postgres && postgres.ProcessID != backend)))
                throw new UnauthorizedAccessException("federation_read_physical_owner_changed");
        }

        public async ValueTask DisposeAsync()
        {
            if (!_opened) return;
            _opened = false;
            if (ReferenceEquals(_connection, _context.Database.GetDbConnection()))
                await _context.Database.CloseConnectionAsync();
            else if (_connection is not null)
                await _connection.CloseAsync();
        }
    }
}
