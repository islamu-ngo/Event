using System.Collections.Immutable;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Retains the originally selected identity/application pair and its source custody during projections.</summary>
internal static class IdentityWriterHumanSourceQueries
{
    internal static Task<HumanPrincipalSourceFacts> ReadPrincipalAsync(
        LocalIdentityExecutionLease execution, HumanCommitSubject subject,
        LocalSessionAuthority? local, CancellationToken cancellationToken = default)
    {
        var owner = new OriginalRead(execution, local is not null);
        return HumanPrincipalSourceProjection.ReadAsync(owner.Application, owner.Identity,
            subject, local, owner.RequireCurrent, cancellationToken);
    }

    internal static async Task<HumanAuthorizationRouteFacts> ReadRouteAsync(
        LocalIdentityExecutionLease execution, HumanCommitSubject subject,
        CancellationToken cancellationToken = default)
    {
        var owner = new OriginalRead(execution, includeIdentity: false);
        if (subject.PrincipalId == Guid.Empty)
            throw new UnauthorizedAccessException("The original principal is required.");
        string[] keys = HumanAuthorizationRouteFacts.Keys.ToArray();
        var system = await owner.Rows(owner.Application.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey))
            .Select(row => new
            {
                row.SettingKey,
                row.Value,
                row.SettingValueTypeId,
                row.IsLocked,
                row.Description,
                row.Category,
                row.AllowedValues
            }), cancellationToken);
        var systemByKey = system.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        Guid? tenant = owner.Application.TenantFilterTenantId;
        var tenantByKey = new Dictionary<string, TenantSettingFacts>(StringComparer.Ordinal);
        if (tenant is { } selected)
        {
            var overrides = await owner.Rows(owner.Application.Set<TenantSetting>().AsNoTracking()
                .Where(row => row.TenantId == selected && keys.Contains(row.SettingKey))
                .Select(row => new { row.SettingKey, row.Value, row.IsLocked }), cancellationToken);
            tenantByKey = overrides.ToDictionary(row => row.SettingKey,
                row => new TenantSettingFacts(row.Value, row.IsLocked), StringComparer.Ordinal);
        }
        owner.RequireCurrent();
        return new(subject.PrincipalId, tenant, keys.Select(key =>
        {
            systemByKey.TryGetValue(key, out var instance);
            tenantByKey.TryGetValue(key, out var scoped);
            return new HumanAuthorizationRouteSettingFact(key, instance is null ? null :
                new SystemSettingFacts(instance.Value, (SettingValueType)instance.SettingValueTypeId, instance.IsLocked,
                    instance.Description, instance.Category, instance.AllowedValues), scoped);
        }).ToImmutableArray());
    }

    private sealed class OriginalRead
    {
        private readonly LocalIdentityExecutionLease _execution;
        private readonly IDbContextTransaction? _applicationTransaction;
        private readonly IDbContextTransaction? _identityTransaction;
        private readonly bool _includeIdentity;
        internal ExploreDbContext Application { get; }
        internal DbContext Identity { get; }

        internal OriginalRead(LocalIdentityExecutionLease execution, bool includeIdentity)
        {
            _execution = execution;
            Application = execution.Application ?? throw new UnauthorizedAccessException("The selected application owner is required.");
            Identity = execution.Identity ?? throw new UnauthorizedAccessException("The selected identity owner is required.");
            _applicationTransaction = Application.Database.CurrentTransaction;
            _identityTransaction = Identity.Database.CurrentTransaction;
            _includeIdentity = includeIdentity;
            RequireCurrent();
        }

        internal void RequireCurrent()
        {
            _execution.RequireCurrent(Application, Identity);
            HumanSecuritySourceGate.RequireCurrent(Application);
            if (_includeIdentity)
                LocalIdentityLifecycleGateCustody.RequireHeld(Identity);
            if (!ReferenceEquals(Application.Database.CurrentTransaction, _applicationTransaction)
                || !ReferenceEquals(Identity.Database.CurrentTransaction, _identityTransaction))
                throw new UnauthorizedAccessException("The original human source transaction changed.");
        }

        internal async Task<ImmutableArray<T>> Rows<T>(IQueryable<T> query, CancellationToken cancellationToken)
        {
            RequireCurrent();
            T[] rows = await query.ToArrayAsync(cancellationToken);
            RequireCurrent();
            return rows.ToImmutableArray();
        }
    }
}
