using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Data-only original membership and revision images for the concrete settings
/// source owners. This observation cannot issue a human admission or effect receipt.
/// </summary>
internal sealed class SettingsDiscoveryDisclosureObservation
{
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease selection;
    private readonly DbContextId contextId;
    private readonly RelationalConnectionIdentity connection;
    private readonly int? backend;
    private DbTransaction? transaction;
    private ImmutableDictionary<Guid, RevisionImage> revisions = ImmutableDictionary<Guid, RevisionImage>.Empty;
    private Guid[] audience = [];

    private SettingsDiscoveryDisclosureObservation(ExploreDbContext context, ExploreDbContextExecutionLease selection,
        OrdinarySettingsMutation? ordinary, UpdateSettingBatchCommand? mixed, HumanCommitSubject subject)
    {
        this.context = context;
        this.selection = selection;
        Ordinary = ordinary;
        Mixed = mixed;
        Subject = subject;
        TenantId = selection.SelectedTenantId;
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        backend = connection.BackendProcessId;
    }

    internal OrdinarySettingsMutation? Ordinary { get; }
    internal UpdateSettingBatchCommand? Mixed { get; }
    internal HumanCommitSubject Subject { get; }
    internal Guid? TenantId { get; }
    internal IReadOnlyList<Guid> Audience => audience;
    internal IReadOnlyDictionary<Guid, RevisionImage> Revisions => revisions;

    internal static async Task<SettingsDiscoveryDisclosureObservation?> ObserveAsync(
        ExploreDbContext context, ExploreDbContextExecutionLease selection, OrdinarySettingsMutation mutation,
        HumanCommitSubject subject, CancellationToken token)
    {
        if (!mutation.Effects.Any(effect => effect.Scope != SettingScope.User
                && EventDiscoveryDisclosureMutationScope.IsDisclosureSetting(effect.Key))) return null;
        var observation = new SettingsDiscoveryDisclosureObservation(context, selection, mutation, null, subject);
        await observation.ReadOriginalAsync(token);
        return observation;
    }

    internal static async Task<SettingsDiscoveryDisclosureObservation?> ObserveAsync(
        ExploreDbContext context, ExploreDbContextExecutionLease selection, UpdateSettingBatchCommand mutation,
        HumanCommitSubject subject, CancellationToken token)
    {
        if (!mutation.Values.Keys.Any(EventDiscoveryDisclosureMutationScope.IsDisclosureSetting)) return null;
        var observation = new SettingsDiscoveryDisclosureObservation(context, selection, null, mutation, subject);
        await observation.ReadOriginalAsync(token);
        return observation;
    }

    private async Task ReadOriginalAsync(CancellationToken token)
    {
        RequireOwner();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("Discovery settings observation must precede the original transaction.");
        await RequirePermissionsAsync(token);
        audience = TenantId is { } selected ? [selected] : await ReadAudienceAsync(token);
        revisions = await ReadRevisionsAsync(audience, token);
        RequireCoverage(audience, revisions);
        RequireOwner();
    }

    internal async Task BindAsync(CancellationToken token)
    {
        RequireOwner();
        if (transaction is not null || System.Transactions.Transaction.Current is not null
            || !context.HumanCommitAttemptOwnsTransaction
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } current
            || current.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(current))
            throw new UnauthorizedAccessException("Discovery settings observation requires one original physical attempt.");
        transaction = current;
        // The original source write reservation precedes this complete membership
        // comparison. Never reuse an idle empty audience as an in-transaction proof.
        await RequireAudienceAsync(token);
    }

    internal async Task RequireAudienceAsync(CancellationToken token)
    {
        RequireAcquired();
        await RequirePermissionsAsync(token);
        if (TenantId is null)
        {
            var currentAudience = await ReadAudienceAsync(token);
            if (!audience.SequenceEqual(currentAudience))
                throw new UnauthorizedAccessException("The original complete discovery audience changed.");
        }
        RequireAcquired();
    }

    internal bool SameOriginal(SettingsDiscoveryDisclosureObservation other) =>
        ReferenceEquals(context, other.context) && ReferenceEquals(Subject, other.Subject) && TenantId == other.TenantId
        && (Ordinary is not null ? ReferenceEquals(Ordinary, other.Ordinary)
            : Mixed is not null && ReferenceEquals(Mixed, other.Mixed))
        && audience.SequenceEqual(other.audience) && revisions.Count == other.revisions.Count
        && revisions.All(pair => other.revisions.TryGetValue(pair.Key, out var value) && pair.Value == value);

    internal void RequireAcquired()
    {
        RequireOwner();
        if (transaction is null || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
            || !connection.Owns(transaction) || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Discovery settings lost the original physical transaction.");
    }

    private void RequireOwner()
    {
        selection.RequireOriginalOwner(context);
        if (context.ContextId != contextId || !connection.Matches(context) || connection.State != ConnectionState.Open
            || connection.BackendProcessId != backend || context.TenantFilterTenantId != TenantId
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || !ReferenceEquals(selection.HumanScope?.Subject, Subject)
            || !ReferenceEquals(context.HumanCommitAuditSubject, Subject))
            throw new UnauthorizedAccessException("Discovery settings lost the original selected human owner.");
        EventDiscoveryEnrollmentGate.RequireCurrent(context);
    }

    private async Task RequirePermissionsAsync(CancellationToken token)
    {
        RequireOwner();
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(context,
            selection.HumanScope?.Enrollment, globalOnly: TenantId is null, token);
        RequireOwner();
    }

    private async Task<Guid[]> ReadAudienceAsync(CancellationToken token)
    {
        // Tenant has no query filters or soft-delete contract. Project ONLY Id,
        // without an active/status predicate: all physical memberships participate.
        var ids = await context.Tenants.AsNoTracking().Select(row => row.Id)
            .Take(EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants + 1).ToArrayAsync(token);
        if (ids.Length > EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants || ids.Contains(Guid.Empty))
            throw new InvalidOperationException("discovery_disclosure_fanout_exceeded");
        return ids.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
    }

    internal async Task<ImmutableDictionary<Guid, RevisionImage>> ReadRevisionsAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken token)
    {
        var keys = ids.ToArray();
        var rows = await context.Set<EventDiscoveryRevision>().AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
            .Where(row => keys.Contains(row.TenantId))
            .Select(row => new RevisionImage(row.Id, row.TenantId, row.IdentityEpoch, row.DisclosureEpoch)).ToListAsync(token);
        if (rows.Select(row => row.TenantId).Distinct().Count() != rows.Count)
            throw new UnauthorizedAccessException("Discovery revision membership is not unique.");
        return rows.ToImmutableDictionary(row => row.TenantId);
    }

    internal static void RequireCoverage(IReadOnlyCollection<Guid> ids, IReadOnlyDictionary<Guid, RevisionImage> rows)
    {
        if (rows.Count != ids.Count || ids.Any(id => !rows.TryGetValue(id, out var row)
                || row.Id == Guid.Empty || row.TenantId != id || row.IdentityEpoch < 0 || row.DisclosureEpoch < 0))
            throw new UnauthorizedAccessException("Every physical discovery audience member requires its existing canonical revision.");
    }

    internal sealed record RevisionImage(Guid Id, Guid TenantId, long IdentityEpoch, long DisclosureEpoch);
}
