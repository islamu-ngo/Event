using System.Transactions;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

/// <summary>Retains the configured identity/application context pair selected before lifecycle construction.</summary>
internal sealed class LocalIdentityExecutionLease
{
    private LocalIdentityExecutionContextCustody? applicationCustody;
    private LocalIdentityExecutionContextCustody? identityCustody;
    private Guid? applicationTenant;
    private Guid? identityTenant;
    internal ExploreDbContext? Application { get; private set; }
    internal DbContext? Identity { get; private set; }

    internal void Bind(ExploreDbContext application, DbContext identity)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(identity);
        if (Application is not null || Identity is not null)
            throw new InvalidOperationException("The selected Local identity context pair cannot be replaced.");
        if (Transaction.Current is not null || application.Database.CurrentTransaction is not null
            || identity.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Local identity context selection must precede BEGIN.");

        Guid? selectedApplicationTenant = RequireTenant(application);
        Guid? selectedIdentityTenant = identity is ExploreDbContext selectedIdentity
            ? RequireTenant(selectedIdentity) : null;
        var originalApplication = LocalIdentityExecutionContextCustody.Capture(application);
        var originalIdentity = ReferenceEquals(application, identity)
            ? originalApplication : LocalIdentityExecutionContextCustody.Capture(identity);

        applicationCustody = originalApplication;
        identityCustody = originalIdentity;
        applicationTenant = selectedApplicationTenant;
        identityTenant = selectedIdentityTenant;
        Application = application;
        Identity = identity;
    }

    internal void RequireCurrent(ExploreDbContext application, DbContext identity)
    {
        if (!ReferenceEquals(Application, application) || !ReferenceEquals(Identity, identity)
            || applicationCustody is null || identityCustody is null)
            throw new UnauthorizedAccessException("The original selected Local identity context pair is required.");
        if (RequireTenant(application) != applicationTenant
            || (identity is ExploreDbContext selectedIdentity && RequireTenant(selectedIdentity) != identityTenant))
            throw new UnauthorizedAccessException("The selected Local identity tenant changed.");
        if (Transaction.Current is not null)
            throw new InvalidOperationException("The selected Local identity pair cannot adopt an ambient transaction.");
        applicationCustody.RequireCurrent(application);
        identityCustody.RequireCurrent(identity);
    }

    private static Guid? RequireTenant(ExploreDbContext context)
    {
        context.RequireBoundTenantLease();
        if (context.IsTenantFilterBypassed)
            throw new InvalidOperationException("The selected Local identity owner cannot bypass tenant isolation.");
        return context.TenantFilterTenantId;
    }
}
