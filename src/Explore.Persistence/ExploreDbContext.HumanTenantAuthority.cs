using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private NativeHumanTenantOwner? _nativeHumanTenantOwner;

    private sealed record NativeHumanTenantOwner(
        HumanCommitSubject Subject, IDbContextTransaction Transaction, RelationalConnectionIdentity Connection, Guid TenantId);

    internal void RetainNativeHumanTenantOwner(HumanCommitSubject subject, Guid tenantId)
    {
        if (!Database.IsNpgsql() || IsTenantFilterBypassed || TenantFilterTenantId != tenantId
            || !HasHumanCommitContextLease || !ReferenceEquals(HumanCommitAuditSubject, subject)
            || Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        _nativeHumanTenantOwner = new(subject, transaction, RelationalConnectionIdentity.Capture(this), tenantId);
    }

    private bool? NativeHumanTenantForeignKeyVisible(
        EntityEntry entry, IEntityType principal, IReadOnlyList<IProperty> properties, object?[] values)
    {
        if (entry.Entity is not TenantSettingsDocument || principal.ClrType != typeof(Tenant)
            || _nativeHumanTenantOwner is not { } owner)
            return null;
        return Database.IsNpgsql() && !IsTenantFilterBypassed && TenantFilterTenantId == owner.TenantId
            && HasHumanCommitContextLease && ReferenceEquals(HumanCommitAuditSubject, owner.Subject)
            && ReferenceEquals(Database.CurrentTransaction, owner.Transaction)
            && owner.Connection.Matches(this)
            && owner.Connection.Owns(owner.Transaction.GetDbTransaction())
            && properties.Count == 1 && properties[0].Name == nameof(Tenant.Id)
            && values.Length == 1 && values[0] is Guid key && key == owner.TenantId;
    }
}
