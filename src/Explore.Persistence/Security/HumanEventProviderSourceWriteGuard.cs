using System.Collections.Immutable;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Rejects a selected operation's EF write intent outside its original observation. Tracked values
/// are used only to reject writes, never as current source evidence or final admission. Physical
/// source reservations exclude other writers; the lease still rereads persisted rows after flush.
/// </summary>
internal sealed class HumanEventProviderSourceWriteGuard(
    ExploreDbContext context, DbTransaction transaction, HumanEventProviderSourceFacts observed)
{
    private readonly HashSet<Guid> _createdBindings = [];
    private readonly HashSet<(HumanEventProviderSource Source, Guid Id)> _createdRows = [];
    private readonly HashSet<Guid> _pendingBindings = [];
    private readonly HashSet<(HumanEventProviderSource Source, Guid Id)> _pendingRows = [];
    private readonly HashSet<(HumanEventProviderSource Source, Guid Id)> _pendingDeletes = [];
    private readonly DbContextId _contextId = context.ContextId;

    internal ImmutableArray<(HumanEventProviderSource Source, Guid Id)> CreatedRows => [.. _createdRows];

    internal void Attach()
    {
        context.SavingChanges += OnSavingChanges;
        context.SavedChanges += OnSavedChanges;
        context.SaveChangesFailed += OnSaveChangesFailed;
    }

    internal void Detach()
    {
        context.SavingChanges -= OnSavingChanges;
        context.SavedChanges -= OnSavedChanges;
        context.SaveChangesFailed -= OnSaveChangesFailed;
    }

    private void ClearPending()
    {
        _pendingBindings.Clear();
        _pendingRows.Clear();
        _pendingDeletes.Clear();
    }

    private void OnSavedChanges(object? sender, SavedChangesEventArgs args)
    {
        _createdBindings.UnionWith(_pendingBindings);
        _createdRows.UnionWith(_pendingRows);
        _createdRows.ExceptWith(_pendingDeletes);
        foreach (var row in _pendingDeletes.Where(row => row.Source == HumanEventProviderSource.Binding))
            _createdBindings.Remove(row.Id);
        ClearPending();
    }

    private void OnSaveChangesFailed(object? sender, SaveChangesFailedEventArgs args) => ClearPending();

    private void OnSavingChanges(object? sender, SavingChangesEventArgs args)
    {
        if (context.ContextId != _contextId
            || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
        {
            Detach();
            return;
        }
        ClearPending();
        context.ChangeTracker.DetectChanges();
        var target = observed.Target;
        var entries = context.ChangeTracker.Entries().Where(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        foreach (var entry in entries)
        {
            var source = HumanEventProviderSourceSql.Sources.SingleOrDefault(source => source.Type == entry.Metadata.ClrType);
            if (source.Type is null) continue;
            Guid id = (Guid)entry.Property("Id").CurrentValue!;
            string tenantProperty = source.Source == HumanEventProviderSource.SecretBinding ? "ScopeId" : "TenantId";
            if (entry.Property(tenantProperty).CurrentValue is not Guid tenant || tenant != target.TenantId)
                throw Rejected();
            if (entry.State != EntityState.Added)
            {
                if (!observed.Rows.Any(row => row.Source == source.Source && row.Id == id)
                    && !_createdRows.Contains((source.Source, id)))
                    throw Rejected();
                if (entry.State == EntityState.Deleted)
                    _pendingDeletes.Add((source.Source, id));
                continue;
            }
            if (source.Source == HumanEventProviderSource.Binding)
            {
                if (target.Operation != HumanEventProviderOperation.CreateBinding
                    || (Guid)entry.Property("RegistrationFormId").CurrentValue! != target.FormId
                    || (Guid)entry.Property("RegistrationFormVersionId").CurrentValue! != target.FormVersionId
                    || (Guid)entry.Property("RegistrationProviderConnectionId").CurrentValue! != target.ConnectionId)
                    throw Rejected();
                _pendingBindings.Add(id);
            }
        }
        foreach (var entry in entries.Where(entry => entry.State == EntityState.Added))
        {
            var source = HumanEventProviderSourceSql.Sources.SingleOrDefault(source => source.Type == entry.Metadata.ClrType);
            if (source.Type is null) continue;
            if (entry.Metadata.FindProperty("EventId") is not null
                && entry.Property("EventId").CurrentValue is Guid eventId && eventId != target.EventId)
                throw Rejected();
            if (entry.Metadata.FindProperty("RegistrationProviderBindingId") is not null)
            {
                Guid? bindingId = entry.Property("RegistrationProviderBindingId").CurrentValue as Guid?;
                if (bindingId != target.BindingId && (!bindingId.HasValue
                    || !_createdBindings.Contains(bindingId.Value) && !_pendingBindings.Contains(bindingId.Value)))
                    throw Rejected();
            }
            if (source.Source == HumanEventProviderSource.Schema
                && (target.Operation != HumanEventProviderOperation.ImportVersion
                    || entry.Property("RegistrationProviderConnectionId").CurrentValue as Guid? != target.ConnectionId))
                throw Rejected();
            if (source.Source is HumanEventProviderSource.Message or HumanEventProviderSource.Effect)
            {
                string property = source.Source == HumanEventProviderSource.Message ? "ProviderMessageId" : "ProviderDecisionId";
                if (target.Operation != HumanEventProviderOperation.ManualImport || target.BindingId is null
                    || entry.Property(property).CurrentValue is not string decision
                    || !decision.StartsWith(target.BindingId.Value.ToString("N") + ":", StringComparison.Ordinal))
                    throw Rejected();
            }
            if (source.Source is HumanEventProviderSource.Connection or HumanEventProviderSource.ApprovedOrigin
                or HumanEventProviderSource.Receipt or HumanEventProviderSource.SecretBinding
                or HumanEventProviderSource.Workflow or HumanEventProviderSource.Requirement or HumanEventProviderSource.Submission)
                throw Rejected();
            _pendingRows.Add((source.Source, (Guid)entry.Property("Id").CurrentValue!));
        }
    }

    private static InvalidOperationException Rejected() => new("human_event_provider_write_outside_original_source");
}
