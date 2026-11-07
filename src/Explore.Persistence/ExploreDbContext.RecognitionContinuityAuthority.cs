using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private readonly ConditionalWeakTable<ModerationRecognitionContinuityBinding, ContinuityCaptureReference>
        _continuityCaptureReferences = new();

    internal RecognitionContinuityOwner CaptureRecognitionContinuityOwner()
    {
        ValidateTenantLease();
        var transaction = Database.CurrentTransaction;
        if (IsTenantFilterBypassed || transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        var connection = RelationalConnectionIdentity.Capture(this);
        var physical = transaction.GetDbTransaction();
        if (connection.State != ConnectionState.Open || !connection.Owns(physical))
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        return new(ContextId, TenantFilterTenantId, transaction, physical, connection, connection.BackendProcessId);
    }

    internal void RetainRecognitionContinuityReference(RecognitionContinuityOwner owner,
        ModerationRecognitionContinuityBinding binding, DateTimeOffset? currentUntil, TimeProvider clock)
    {
        ValidateTenantLease();
        binding.ValidatePersistedState();
        var now = clock.GetUtcNow();
        if (!owner.Matches(this) || currentUntil is not { } until || until == DateTimeOffset.MaxValue
            || now < binding.BoundAt || now >= until)
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        if (owner.Tenant is not { } tenant || tenant == Guid.Empty) return;
        _continuityCaptureReferences.Add(binding, new(owner, clock, until, ContinuityStamp.From(binding)));
    }

    internal void RevokeRecognitionContinuityReference(ModerationRecognitionContinuityBinding binding) =>
        _continuityCaptureReferences.Remove(binding);

    private bool? NativeRecognitionContinuityForeignKeyVisible(EntityEntry entry, IEntityType principal,
        IReadOnlyList<IProperty> properties, object?[] values)
    {
        if (entry.Entity is not ModerationRecognitionContinuityBinding binding
            || principal.ClrType != typeof(ModerationRecognitionCaptureItem)) return null;
        if (entry.State != EntityState.Added || IsTenantFilterBypassed
            || properties.Count != 1 || properties[0].Name != nameof(ModerationRecognitionCaptureItem.Id)
            || values.Length != 1 || values[0] is not Guid parent || parent != binding.CaptureItemId
            || !_continuityCaptureReferences.TryGetValue(binding, out var owner)) return false;
        var now = owner.Clock.GetUtcNow();
        return owner.Owner.Matches(this)
            && now >= binding.BoundAt && now < owner.Until && ContinuityStamp.From(binding) == owner.Stamp;
    }

    internal sealed record RecognitionContinuityOwner(DbContextId Context, Guid? Tenant,
        IDbContextTransaction Transaction, DbTransaction Physical, RelationalConnectionIdentity Connection,
        int? Backend)
    {
        internal bool Matches(ExploreDbContext context) =>
            !context.IsTenantFilterBypassed && context.ContextId == Context && context.TenantFilterTenantId == Tenant
            && ReferenceEquals(context.Database.CurrentTransaction, Transaction)
            && ReferenceEquals(Transaction.GetDbTransaction(), Physical)
            && Connection.Matches(context) && Connection.State == ConnectionState.Open
            && Connection.BackendProcessId == Backend && Connection.Owns(Physical);
    }

    private sealed record ContinuityCaptureReference(RecognitionContinuityOwner Owner,
        TimeProvider Clock, DateTimeOffset Until, ContinuityStamp Stamp);

    private sealed record ContinuityStamp(Guid Id, Guid Subject, Guid Capture, string Receipt,
        Guid Sanction, Guid Finding, DateTimeOffset BoundAt, DateTimeOffset RetainUntil)
    {
        internal static ContinuityStamp From(ModerationRecognitionContinuityBinding binding) =>
            new(binding.Id, binding.FreshSubjectId, binding.CaptureItemId, binding.CaptureMaterialReceipt,
                binding.SanctionId, binding.FindingId, binding.BoundAt, binding.RetainUntil);
    }
}
