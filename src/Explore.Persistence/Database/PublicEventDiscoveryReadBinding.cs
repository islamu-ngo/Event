using System.Data;
using Explore.Application.Contracts.Operations;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

/// <summary>
/// Retains the selected ordinary public reader without creating human mutation authority.
/// Native routines separately attest the enrolled Runtime login and their fixed privileges.
/// </summary>
public sealed class PublicEventDiscoveryReadBinding
{
    private readonly ExploreDbContext _context;
    private readonly ExploreDbContextExecutionLease _lease;
    private readonly DbContextId _contextId;
    private readonly RelationalConnectionIdentity _connection;
    private readonly Guid _tenantId;

    internal PostgresNativeQueueEnrollment Enrollment { get; }

    internal PublicEventDiscoveryReadBinding(ExploreDbContext context,
        ExploreDbContextExecutionLease lease, PostgresNativeQueueEnrollment enrollment)
    {
        _context = context;
        _lease = lease;
        _contextId = context.ContextId;
        _connection = RelationalConnectionIdentity.Capture(context);
        _tenantId = context.BindTenantLease()
            ?? throw new UnauthorizedAccessException("Public discovery requires its selected tenant.");
        Enrollment = enrollment;
        RequireOriginalOwner(context);
    }

    internal void RequireOriginalOwner(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!ReferenceEquals(context, _context) || context.ContextId != _contextId
            || !_connection.Matches(context)
            || _tenantId == Guid.Empty || context.TenantFilterTenantId != _tenantId
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Public discovery cannot substitute its selected owner.");

        // Ordinary request contexts have no mutation lease. An explicitly selected
        // operation may borrow only the same Standard tenant owner, never another purpose.
        if (_lease.Context is not null)
        {
            _lease.RequireOriginalOwner(context);
            if (_lease.PolicyPurpose is { } purpose && purpose != PolicyMutationPurpose.Standard)
                throw new UnauthorizedAccessException("Public discovery requires ordinary Runtime purpose.");
        }
    }

    internal void RequireSerializableOwner(ExploreDbContext context)
    {
        RequireOriginalOwner(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction is null || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("Public discovery requires its original Serializable transaction.");
    }
}

/// <summary>Creates a reader only when a public native operation actually starts.</summary>
public sealed class PublicEventDiscoveryReadBindingFactory
{
    private readonly IServiceProvider _provider;

    internal PublicEventDiscoveryReadBindingFactory(IServiceProvider provider) => _provider = provider;

    internal PublicEventDiscoveryReadBinding Create(ExploreDbContext context)
    {
        if (!context.Database.IsNpgsql()
            || !ReferenceEquals(context, _provider.GetRequiredService<ExploreDbContext>()))
            throw new UnauthorizedAccessException("Public discovery must retain the production-selected native context.");
        return new(context, _provider.GetRequiredService<ExploreDbContextExecutionLease>(),
            PostgresNativeQueueEnrollment.Bind(_provider.GetRequiredService<IConfiguration>()));
    }
}
