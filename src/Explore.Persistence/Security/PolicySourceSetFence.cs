using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public enum PolicySourceSetPurpose
{
    RuntimeTenant = 1,
    Global = 2,
    ConfigurationImport = 3
}

/// <summary>
/// Borrows the original Serializable owner. PostgreSQL uses an execute-only sentinel
/// capability and compulsory writer triggers; other providers use physical write/range locks.
/// No provider call, connection replacement, transaction creation, or retry occurs here.
/// </summary>
public sealed class PolicySourceSetFence : IPolicySourceSetFence
{
    private readonly ExploreDbContext _context;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;
    private readonly Guid? _tenant;
    private readonly PolicySourceSetPurpose _purpose;
    private readonly PolicySourceSetProviderLock _providerLock;
    private readonly LocalIdentityExecutionLease? _selectedExecution;

    internal PolicySourceSetFence(LocalIdentityExecutionLease execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        _context = execution.Application ?? throw new UnauthorizedAccessException("The selected application owner is required.");
        execution.RequireCurrent(_context, execution.Identity
            ?? throw new UnauthorizedAccessException("The selected identity owner is required."));
        if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction is not null
            || execution.Identity!.Database.CurrentTransaction is not null
            || _context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("policy_source_set_requires_selected_owner");
        HumanSecuritySourceGate.RequireCurrent(_context);
        _tenant = _context.TenantFilterTenantId;
        _connection = RelationalConnectionIdentity.Capture(_context);
        _contextLease = _context.ContextId;
        _purpose = _tenant.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
        _selectedExecution = execution;
        _providerLock = new(_context, _connection, _tenant, _purpose, null);
    }

    internal static void RequireSelectedLease(IPolicySourceSetLease lease, LocalIdentityExecutionLease execution,
        ExploreDbContext context, DbTransaction transaction)
    {
        if (lease is not Lease issued || !issued.BelongsTo(execution, context, transaction))
            throw new UnauthorizedAccessException("The selected owner's original policy source-set lease is required.");
    }

    internal static readonly IReadOnlyList<string> RouteKeys = Array.AsReadOnly(new[]
    {
        GovernanceSettingKeys.Security.AuthorizationProvider,
        GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
        GovernanceSettingKeys.Cerbos.CustomEndpoint,
        GovernanceSettingKeys.Cerbos.GrpcEndpoint,
        GovernanceSettingKeys.Cerbos.Mode,
        GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled
    });

    public PolicySourceSetFence(ExploreDbContext context, PolicySourceSetPurpose purpose,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Enum.IsDefined(purpose) || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("policy_source_set_requires_selected_owner");
        _tenant = context.BindTenantLease();
        if ((purpose == PolicySourceSetPurpose.RuntimeTenant) != _tenant.HasValue
            || (purpose == PolicySourceSetPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("policy_source_set_purpose_mismatch");
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        _purpose = purpose;
        _providerLock = new(context, _connection, _tenant, purpose, enrollment);
    }

    public async Task<IPolicySourceSetLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        DbTransaction transaction = RequireOwner();
        if (_selectedExecution is not null)
            await _providerLock.AcquireSelectedWriterAsync(transaction, cancellationToken);
        else
            await HumanSourceProviderOperations.AcquirePolicySourcesAsync(_context, _providerLock, transaction, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("policy_source_set_transaction_changed");
        return new Lease(this, transaction);
    }

    private DbTransaction RequireOwner()
    {
        if (_selectedExecution is not null)
        {
            _selectedExecution.RequireCurrent(_context, _selectedExecution.Identity
                ?? throw new UnauthorizedAccessException("The selected identity owner is required."));
            HumanSecuritySourceGate.RequireCurrent(_context);
        }
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextLease || _context.IsTenantFilterBypassed
            || _context.TenantFilterTenantId != _tenant
            || (_purpose == PolicySourceSetPurpose.ConfigurationImport) != _context.IsConfigurationInstanceImportOwner
            || !_connection.Matches(_context)
            || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("policy_source_set_requires_original_serializable_owner");
        return transaction;
    }

    private sealed class Lease(PolicySourceSetFence fence, DbTransaction transaction) : IPolicySourceSetLease
    {
        internal bool BelongsTo(LocalIdentityExecutionLease execution, ExploreDbContext context, DbTransaction original) =>
            ReferenceEquals(fence._selectedExecution, execution) && ReferenceEquals(fence._context, context)
            && ReferenceEquals(transaction, original) && ReferenceEquals(transaction, fence.RequireOwner());

        public async Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, fence.RequireOwner()))
                throw new InvalidOperationException("policy_source_set_lease_expired");
            // Re-enter the fixed capability to recheck the actual login/purpose/tenant,
            // not only the managed context. This transaction already owns the singleton.
            if (fence._selectedExecution is not null)
                await fence._providerLock.AcquireSelectedWriterAsync(transaction, cancellationToken);
            else
                await HumanSourceProviderOperations.ValidatePolicySourcesAsync(
                    fence._context, fence._providerLock, transaction, cancellationToken);
            if (!ReferenceEquals(transaction, fence.RequireOwner()))
                throw new InvalidOperationException("policy_source_set_lease_expired");
        }
    }
}
