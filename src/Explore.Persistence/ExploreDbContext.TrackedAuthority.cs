using System.Linq.Expressions;
using System.Reflection;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private readonly Dictionary<Guid, NativeAiGraphOwner> _nativeAiGraphOwners = [];
    private NativeCheckoutDispatchOwner? _nativeCheckoutDispatchOwner;

    private sealed record NativeCheckoutDispatchOwner(
        CheckoutDispatchClaim Claim, PaymentAttempt Attempt, int StatusId, PaymentReconciliationEffect? Reconciliation,
        IDbContextTransaction Transaction, RelationalConnectionIdentity Connection);

    internal void RetainNativeCheckoutDispatchOwner(
        CheckoutDispatchClaim claim, PaymentAttempt attempt, PaymentReconciliationEffect? reconciliation)
    {
        // The repository calls this only after the original active claim's
        // compare-and-set succeeds, while its row lock still belongs to this transaction.
        if (!Database.IsNpgsql() || !TenantFilterTenantId.HasValue)
            return;
        if (IsTenantFilterBypassed || TenantFilterTenantId != claim.TenantId
            || attempt.Id != claim.PaymentAttemptId || attempt.TenantId != claim.TenantId
            || attempt.RegistrationOrderId != claim.RegistrationOrderId
            || reconciliation is not null && (reconciliation.TenantId != claim.TenantId
                || reconciliation.PaymentAttemptId != claim.PaymentAttemptId
                || reconciliation.RegistrationOrderId != claim.RegistrationOrderId)
            || Database.CurrentTransaction is not { } transaction)
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        _nativeCheckoutDispatchOwner = new(claim, attempt, attempt.PaymentAttemptStatusId, reconciliation,
            transaction, RelationalConnectionIdentity.Capture(this));
    }

    private bool? NativeCheckoutDispatchForeignKeyVisible(
        EntityEntry entry, IEntityType principal, IReadOnlyList<IProperty> properties, object?[] values)
    {
        if (_nativeCheckoutDispatchOwner is not { } owner
            || principal.ClrType != typeof(Tenant) && principal.ClrType != typeof(PaymentAttemptStatus))
            return null;
        if (!ReferenceEquals(entry.Entity, owner.Reconciliation) && !ReferenceEquals(entry.Entity, owner.Attempt))
            return null;
        if (!Database.IsNpgsql() || IsTenantFilterBypassed || TenantFilterTenantId != owner.Claim.TenantId
            || !ReferenceEquals(Database.CurrentTransaction, owner.Transaction)
            || !owner.Connection.Matches(this) || !owner.Connection.Owns(owner.Transaction.GetDbTransaction())
            || properties.Count != 1 || properties[0].Name != "Id" || values.Length != 1)
            return false;
        // Only the exact fenced objects can supply their already bound tenant or
        // closed domain-state lookup. Database FK constraints still check existence.
        return principal.ClrType == typeof(PaymentAttemptStatus)
            ? ReferenceEquals(entry.Entity, owner.Attempt) && entry.State == EntityState.Modified
                && owner.Attempt.TenantId == owner.Claim.TenantId
                && owner.Attempt.Id == owner.Claim.PaymentAttemptId
                && owner.Attempt.RegistrationOrderId == owner.Claim.RegistrationOrderId
                && values[0] is int status && status == owner.StatusId
                && status == owner.Attempt.PaymentAttemptStatusId
                && Enum.IsDefined((Explore.Domain.Enums.PaymentAttemptStatusEnum)status)
            : owner.Reconciliation is not null
                && ReferenceEquals(entry.Entity, owner.Reconciliation) && entry.State == EntityState.Added
                && owner.Reconciliation.TenantId == owner.Claim.TenantId
                && owner.Reconciliation.PaymentAttemptId == owner.Claim.PaymentAttemptId
                && owner.Reconciliation.RegistrationOrderId == owner.Claim.RegistrationOrderId
                && values[0] is Guid key && key == owner.Claim.TenantId;
    }

    private sealed record NativeAiGraphOwner(
        IDbContextTransaction Transaction, RelationalConnectionIdentity Connection, Guid TenantId, Guid UserId,
        Guid? ActorId, Guid? AdmittedActorId);

    internal void RetainNativeAiGraphOwner(
        Guid conversationId, Guid tenantId, Guid userId, Guid? actorId, Guid? admittedActorId)
    {
        if (!Database.IsNpgsql() || IsTenantFilterBypassed || TenantFilterTenantId != tenantId
            || Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        if (_nativeAiGraphOwners.Values.Any(owner => !ReferenceEquals(owner.Transaction, transaction)))
            _nativeAiGraphOwners.Clear();
        _nativeAiGraphOwners[conversationId] = new(
            transaction, RelationalConnectionIdentity.Capture(this), tenantId, userId, actorId, admittedActorId);
    }

    private bool? NativeAiForeignKeyVisible(EntityEntry entry, IEntityType principal,
        IReadOnlyList<IProperty> properties, object?[] values)
    {
        if (!Database.IsNpgsql()
            || principal.ClrType != typeof(User) && principal.ClrType != typeof(Actor) && principal.ClrType != typeof(Tenant))
            return null;
        Guid? conversationId = entry.Entity switch
        {
            AiConversation root => root.Id,
            AiMessage message => message.ConversationId,
            AiRun run => run.ConversationId,
            AiProposedAction action => action.ConversationId,
            AiToolExecution execution => execution.ConversationId,
            AiConversationReference reference => reference.ConversationId,
            _ => null
        };
        if (conversationId is null)
            return null;
        if (!_nativeAiGraphOwners.TryGetValue(conversationId.Value, out var owner))
            return null;
        // Only a successfully fenced native graph can attest these private FK keys.
        // A different graph, tenant, physical transaction or connection cannot borrow it.
        if (IsTenantFilterBypassed || TenantFilterTenantId != owner.TenantId
            || !ReferenceEquals(Database.CurrentTransaction, owner.Transaction)
            || !owner.Connection.Matches(this)
            || !owner.Connection.Owns(owner.Transaction.GetDbTransaction())
            || properties.Count != 1 || properties[0].Name != "Id"
            || values.Length != 1 || values[0] is not Guid key)
            return false;
        return principal.ClrType == typeof(User) ? key == owner.UserId
            : principal.ClrType == typeof(Actor) ? key == owner.ActorId || key == owner.AdmittedActorId
            : key == owner.TenantId;
    }

    private static readonly MethodInfo PersistedTenantVisibilityMethod = typeof(ExploreDbContext)
        .GetMethod(nameof(IsPersistedTenantVisibleAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Rejects cross-tenant tracked writes inside the existing native save transaction.
    /// This is a lease-integrity check, not a substitute for the caller's action authority.
    /// Global maintenance remains governed by its purpose-specific native owner.
    /// </summary>
    private async Task ValidateTrackedTenantAuthorityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ValidateNativeWebhookClaimEvidenceAsync(cancellationToken);
        ValidateTenantLease();
        if (TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return;

        try
        {
            if (IsTenantFilterBypassed)
                throw new InvalidOperationException("tracked_tenant_authority_mismatch");
            ChangeTracker.DetectChanges();
            foreach (var entry in ChangeTracker.Entries()
                         .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    var key = entry.Metadata.FindPrimaryKey()
                        ?? throw new InvalidOperationException("Tracked authority requires a mapped primary key.");
                    if (!await IsTenantVisibleAsync(entry.Metadata, key.Properties,
                            key.Properties.Select(property => entry.Property(property.Name).OriginalValue).ToArray(),
                            cancellationToken))
                        throw new InvalidOperationException("tracked_tenant_authority_mismatch");
                }

                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    foreach (var foreignKey in entry.Metadata.GetForeignKeys())
                    {
                        if (entry.State != EntityState.Added
                            && !foreignKey.Properties.Any(property => entry.Property(property.Name).IsModified))
                            continue;
                        object?[] values = foreignKey.Properties
                            .Select(property => entry.Property(property.Name).CurrentValue).ToArray();
                        if (values.Any(value => value is null))
                            continue;
                        bool pendingParent = ChangeTracker.Entries().Any(parent =>
                            parent.State == EntityState.Added && parent.Metadata == foreignKey.PrincipalEntityType
                            && foreignKey.PrincipalKey.Properties.Select(property => parent.Property(property.Name).CurrentValue)
                                .SequenceEqual(values));
                        bool visible = pendingParent
                            || (NativeHumanTenantForeignKeyVisible(entry, foreignKey.PrincipalEntityType,
                                    foreignKey.PrincipalKey.Properties, values)
                                ?? NativeAiForeignKeyVisible(entry, foreignKey.PrincipalEntityType,
                                    foreignKey.PrincipalKey.Properties, values)
                                ?? NativeCheckoutDispatchForeignKeyVisible(entry, foreignKey.PrincipalEntityType,
                                    foreignKey.PrincipalKey.Properties, values)
                                ?? NativeWebhookClaimForeignKeyVisible(entry, foreignKey.PrincipalEntityType,
                                    foreignKey.PrincipalKey.Properties, values)
                                ?? NativePaymentReconciliationForeignKeyVisible(entry, foreignKey.PrincipalEntityType,
                                    foreignKey.PrincipalKey.Properties, values)
                                ?? await IsTenantVisibleAsync(foreignKey.PrincipalEntityType,
                                    foreignKey.PrincipalKey.Properties, values, cancellationToken));
                        if (!visible)
                        {
                            // Storage attachments already treat a missing or foreign source
                            // as a retirement conflict. Keep that contract when this earlier
                            // tenant preflight detects the same invisible parent.
                            if (foreignKey.PrincipalEntityType.ClrType == typeof(Explore.Domain.StorageObject))
                                throw StorageReferenceConflict();
                            throw new InvalidOperationException("tracked_tenant_authority_mismatch")
                            {
                                Data =
                                {
                                    ["TrackedEntityType"] = entry.Metadata.ClrType.Name,
                                    ["PrincipalEntityType"] = foreignKey.PrincipalEntityType.ClrType.Name,
                                    ["ForeignKeyProperties"] = string.Join(",", foreignKey.Properties.Select(property => property.Name))
                                }
                            };
                        }
                    }
                }

                if (entry.Metadata.IsOwned())
                    continue;
                var tenantProperty = entry.Metadata.FindProperty("TenantId");
                if (tenantProperty is null || tenantProperty.ClrType != typeof(Guid) && tenantProperty.ClrType != typeof(Guid?))
                    continue;

                var tenant = entry.Property(tenantProperty.Name);
                if (tenant.CurrentValue is Guid current && current != tenantId
                    || tenant.OriginalValue is Guid original && original != tenantId)
                    throw new InvalidOperationException("tracked_tenant_authority_mismatch");

                if (entry.State == EntityState.Added)
                    continue;

                // Attaching a posted object also supplies EF's original values. Only
                // the persisted row can establish the tenant of an existing key.
                var persisted = await entry.GetDatabaseValuesAsync(cancellationToken);
                if (persisted is null
                    || !Equals(persisted[tenantProperty.Name], tenant.CurrentValue)
                    || persisted[tenantProperty.Name] is Guid owner && owner != tenantId)
                    throw new InvalidOperationException("tracked_tenant_authority_mismatch");
            }
        }
        catch
        {
            if (Database.IsRelational())
                PoisonStorageReferenceTransaction();
            throw;
        }
    }

    private Task<bool> IsTenantVisibleAsync(
        IEntityType entityType, IReadOnlyList<IProperty> properties, object?[] values,
        CancellationToken cancellationToken)
    {
        // Owned slots are not independent DbSets. Follow their mapped ownership
        // keys to the actual root, including nested owned policy/value objects.
        while (entityType.FindOwnership() is { } ownership)
        {
            var sourceProperties = properties.ToList();
            values = ownership.Properties.Select(property => values[sourceProperties.IndexOf(property)]).ToArray();
            properties = ownership.PrincipalKey.Properties;
            entityType = ownership.PrincipalEntityType;
        }
        return (Task<bool>)PersistedTenantVisibilityMethod.MakeGenericMethod(entityType.ClrType)
            .Invoke(this, [entityType, properties, values, cancellationToken])!;
    }

    private Task<bool> IsPersistedTenantVisibleAsync<TEntity>(
        IEntityType entityType, IReadOnlyList<IProperty> properties, object?[] values,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        Expression predicate = Expression.Constant(true);
        for (int index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            var column = Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType],
                entity, Expression.Constant(property.Name));
            object? keyValue = values[index];
            Expression<Func<object?>> selectedValue = () => keyValue;
            var value = Expression.Convert(selectedValue.Body, property.ClrType);
            predicate = Expression.AndAlso(predicate, Expression.Equal(column, value));
        }

        var tenantProperty = entityType.FindProperty("TenantId");
        if (tenantProperty is not null
            && (tenantProperty.ClrType == typeof(Guid) || tenantProperty.ClrType == typeof(Guid?)))
        {
            var column = Expression.Call(typeof(EF), nameof(EF.Property), [tenantProperty.ClrType],
                entity, Expression.Constant(tenantProperty.Name));
            Guid? tenantId = TenantFilterTenantId;
            Expression<Func<Guid?>> selectedTenant = () => tenantId;
            Expression tenantMatch = Expression.Equal(Expression.Convert(column, typeof(Guid?)), selectedTenant.Body);
            if (tenantProperty.ClrType == typeof(Guid?))
                tenantMatch = Expression.OrElse(Expression.Equal(column, Expression.Constant(null, typeof(Guid?))), tenantMatch);
            predicate = Expression.AndAlso(predicate, tenantMatch);
        }

        // Tenant ownership can be derived through a parent rather than a TenantId
        // column. Evaluate the mapped tenant filter in SQL, never the identity map.
        // Deleted-row maintenance still needs its separate caller-owned authority.
        var source = entityType.HasSharedClrType ? Set<TEntity>(entityType.Name) : Set<TEntity>();
        return source.AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(Expression.Lambda<Func<TEntity, bool>>(predicate, entity), cancellationToken);
    }
}
