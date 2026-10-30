using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Domain.Learning;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Owns one exact guidance document effect. Source absence, instance control and the accepted
/// semantic write image remain fenced until the original physical settlement receipt is consumed.
/// </summary>
internal sealed class HumanLearningPolicyMutationStore(
    ExploreDbContext context,
    ExploreDbContextExecutionLease selection,
    IInstanceTenantSettingsReader settings,
    IHumanLearningPolicyMutationPreparation preparation,
    IOriginalOwnerSettlementBoundary owner,
    IHumanActionCommitFinalizer finalizer,
    TimeProvider clock,
    PostgresNativeQueueEnrollment? enrollment = null) : IHumanLearningPolicyMutationStore, IDisposable
{
    private readonly DbContextId _contextId = context.ContextId;
    private readonly object? _human = selection.HumanScope;
    private readonly object? _lifetime = selection.HumanLifetime;
    private UpdateLearningGuidancePolicyCommand? _command;
    private Snapshot? _original;
    private RelationalConnectionIdentity? _connection;
    private int? _backend;
    private DbTransaction? _transaction;
    private bool _interrupted;
    private int _observed;
    private int _used;
    private IHumanLearningPolicyMutation? _completed;
    private string? _acceptedDocumentImage;
    private Dictionary<object, string>? _acceptedTracked;

    public async Task<ILearningPolicyOriginalSources> ObserveAsync(
        UpdateLearningGuidancePolicyCommand command, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Interlocked.Exchange(ref _observed, 1) != 0)
            throw new InvalidOperationException("Guidance permits one original source observation.");
        RequireSelection(tenantId, clock.GetUtcNow());
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("Guidance observation requires its idle original owner.");
        await context.Database.OpenConnectionAsync(cancellationToken);
        _connection = RelationalConnectionIdentity.Capture(context);
        _backend = _connection.BackendProcessId;
        _connection.StateChange += ConnectionChanged;
        _command = command;
        await PostgresLearningPermissions.RequireGuidanceOwnerAsync(
            context, enrollment, tenantId, cancellationToken);
        _original = await CaptureAsync(tenantId, cancellationToken);
        RequireConnection(tenantId, clock.GetUtcNow());
        return _original.Source;
    }

    public async Task<ResolvedLearningGuidancePolicy> ExecuteAsync(
        IHumanLearningPolicyMutation mutation, CancellationToken cancellationToken)
    {
        RequireMutation(mutation);
        if (Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("Guidance permits one physical settlement attempt.");
        await mutation.RequireOriginalAsync(cancellationToken);
        var invocation = new object();
        bool settled = false;
        try
        {
            var receipt = await owner.ExecuteAsync(invocation, async token =>
            {
                _transaction = context.Database.CurrentTransaction?.GetDbTransaction()
                    ?? throw new UnauthorizedAccessException("Guidance requires its original physical transaction.");
                RequirePhysicalOwner(mutation);
                if (context.ChangeTracker.HasChanges())
                    throw new UnauthorizedAccessException("Guidance requires a clean write inventory.");
                await mutation.AcquireAsync(token);
                await new LearningSourceProviderLock(context, _connection!, mutation.TenantId)
                    .AcquireAsync(_transaction, token);
                var acquired = await CaptureAsync(mutation.TenantId, token);
                if (acquired.DocumentImage != _original!.DocumentImage || acquired.Governance != _original.Governance)
                    throw Conflict("Original guidance sources or their absence changed.");
                if (!acquired.Source.AllowTenantEdits)
                    throw new AuthorizationException("Instance policy does not permit tenant guidance changes.");
                if (acquired.Source.Document?.ConcurrencyStamp != mutation.OriginalCommand.Input.ExpectedConcurrencyStamp)
                    throw Conflict("The observed guidance document stamp is stale.");
                RequirePhysicalOwner(mutation);
                finalizer.Register(async finalToken =>
                {
                    await RequireCompleteAsync(mutation, finalToken);
                    await mutation.VerifyAsync(finalToken);
                    RequirePhysicalOwner(mutation);
                }, now =>
                {
                    mutation.RequireCurrentAt(now);
                    RequirePhysicalOwner(mutation);
                });

                string payload = LearningGuidancePolicyCodec.Encode(mutation.Policy);
                var after = TenantSettingsDocument.Create(mutation.TenantId,
                    SettingsDocumentKeys.Tenant.LearningGuidance, LearningGuidancePolicyCodec.SchemaVersion,
                    LearningGuidancePolicyCodec.DefaultsVersion, payload);
                after.Id = acquired.Row?.Id ?? mutation.CandidateDocumentId;
                after.ConcurrencyStamp = mutation.CandidateStamp;
                after.CreatedAt = acquired.Row?.CreatedAt ?? mutation.ChangedAtUtc;
                after.CreatedBy = acquired.Row?.CreatedBy ?? mutation.SubjectId;
                if (acquired.Row is not null)
                {
                    // Preserve nullable birth attribution rather than rewriting another owner's history.
                    after.CreatedBy = acquired.Row.CreatedBy;
                    after.UpdatedAt = mutation.ChangedAtUtc;
                    after.UpdatedBy = mutation.SubjectId;
                }
                _acceptedDocumentImage = DocumentImage(after);
                if (acquired.Row is null)
                {
                    context.TenantSettingsDocuments.Add(after);
                    if (context.ChangeTracker.Entries().Any(entry =>
                            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                            && !ReferenceEquals(entry.Entity, after)))
                        throw new UnauthorizedAccessException("Guidance cannot include another write effect.");
                    _acceptedTracked = TrackedImage();
                    // A nonempty candidate stamp and explicit audit values survive the one normal
                    // SaveChanges preparation. Never invoke PrepareTrackedEntities a second time.
                    await context.SaveChangesAsync(token);
                }
                else
                {
                    _acceptedTracked = TrackedImage();
                    int affected = await context.TenantSettingsDocuments
                        .Where(row => row.Id == after.Id && row.TenantId == mutation.TenantId
                            && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance
                            && row.ConcurrencyStamp == acquired.Row.ConcurrencyStamp)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(row => row.PayloadJson, payload)
                            .SetProperty(row => row.ConcurrencyStamp, mutation.CandidateStamp)
                            .SetProperty(row => row.UpdatedAt, (DateTime?)mutation.ChangedAtUtc)
                            .SetProperty(row => row.UpdatedBy, (Guid?)mutation.SubjectId), token);
                    if (affected != 1) throw Conflict("The original guidance document changed.");
                }
                _completed = mutation;
                var completed = await RequireCompleteAsync(mutation, token);
                return OriginalOwnerDecision<ResolvedLearningGuidancePolicy>.Commit(new(
                    completed.Policy, LearningGuidancePolicyCodec.SchemaVersion,
                    LearningGuidancePolicyCodec.DefaultsVersion, completed.Source.Document!.ConcurrencyStamp,
                    completed.PolicySourceStamp, completed.Source.AllowTenantEdits));
            }, cancellationToken);
            var result = owner.Consume(invocation, receipt, OriginalOwnerDisposition.Committed);
            settled = true;
            await mutation.RequireOriginalAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (settled && exception is not HumanMutationOutcomeUnknownException)
        {
            throw new HumanMutationOutcomeUnknownException(exception);
        }
    }

    private async Task<Snapshot> RequireCompleteAsync(IHumanLearningPolicyMutation mutation, CancellationToken token)
    {
        RequirePhysicalOwner(mutation);
        await PostgresLearningPermissions.RequireGuidanceOwnerAsync(context, enrollment, mutation.TenantId, token);
        if (!ReferenceEquals(_completed, mutation) || _acceptedDocumentImage is null
            || _acceptedTracked is null || context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("The exact guidance write is incomplete.");
        var tracked = TrackedImage();
        if (tracked.Count != _acceptedTracked.Count || tracked.Any(pair =>
                !_acceptedTracked.TryGetValue(pair.Key, out var expected) || pair.Value != expected))
            throw new UnauthorizedAccessException("The accepted guidance tracked image changed.");
        var current = await CaptureAsync(mutation.TenantId, token);
        if (current.DocumentImage != _acceptedDocumentImage || current.Governance != _original!.Governance
            || !current.Source.AllowTenantEdits)
            throw Conflict("The accepted guidance image or original instance control changed.");
        RequirePhysicalOwner(mutation);
        return current;
    }

    private async Task<Snapshot> CaptureAsync(Guid tenantId, CancellationToken token)
    {
        var hierarchy = await settings.ReadAsync(tenantId, [LearningSourceRetention.AllowTenantEditsKey], token);
        var control = hierarchy.System.SingleOrDefault(row => row.SettingKey == LearningSourceRetention.AllowTenantEditsKey);
        var governance = control is null ? null : new Governance(
            control.Id, control.SettingKey, control.Value, control.IsLocked, control.SettingValueTypeId,
            control.AllowedValues, control.Description, control.Category, control.DisplayOrder);
        bool allowEdits = true;
        if (control is not null)
        {
            using var value = JsonDocument.Parse(control.Value);
            allowEdits = value.RootElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidOperationException("The stored instance guidance control is malformed.")
            };
        }
        var row = await context.TenantSettingsDocuments.AsNoTracking().SingleOrDefaultAsync(
            value => value.TenantId == tenantId && value.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance, token);
        var document = row is null ? null : new LearningGuidanceDocumentSource(
            row.Id, row.TenantId, row.SchemaVersion, row.DefaultsVersion, row.PayloadJson, row.ConcurrencyStamp);
        var policy = LearningGuidancePolicyCodec.Decode(document);
        string stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            DefaultsVersion = LearningGuidancePolicyCodec.DefaultsVersion,
            Document = document,
            Governance = governance
        }))));
        return new(new OriginalSources(document, allowEdits), governance, row, row is null ? null : DocumentImage(row),
            stamp, policy);
    }

    private static string DocumentImage(TenantSettingsDocument row) => JsonSerializer.Serialize(new
    {
        row.Id, row.TenantId, row.DocumentKey, row.SchemaVersion, row.DefaultsVersion,
        Payload = LearningGuidancePolicyCodec.Encode(LearningGuidancePolicyCodec.Decode(
            new(row.Id, row.TenantId, row.SchemaVersion, row.DefaultsVersion, row.PayloadJson, row.ConcurrencyStamp))),
        row.ConcurrencyStamp,
        CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc), row.CreatedBy,
        UpdatedAt = row.UpdatedAt is { } updated ? DateTime.SpecifyKind(updated, DateTimeKind.Utc) : (DateTime?)null,
        row.UpdatedBy
    });

    private Dictionary<object, string> TrackedImage() => context.ChangeTracker.Entries().ToDictionary(
        entry => entry.Entity,
        entry => entry.Entity is TenantSettingsDocument document ? DocumentImage(document)
            : JsonSerializer.Serialize(entry.Properties.OrderBy(property => property.Metadata.Name, StringComparer.Ordinal)
                .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue,
                    StringComparer.Ordinal)),
        ReferenceEqualityComparer.Instance);

    private void RequireSelection(Guid tenantId, DateTimeOffset now)
    {
        context.RequireBoundTenantLease();
        selection.RequireOriginalOwner(context);
        if (tenantId == Guid.Empty || selection.SelectedTenantId != tenantId || context.ContextId != _contextId
            || !ReferenceEquals(selection.HumanScope, _human) || !ReferenceEquals(selection.HumanLifetime, _lifetime)
            || selection.HumanScope?.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant
            || selection.PolicyPurpose != PolicyMutationPurpose.Standard || selection.HumanLifetime?.IsValidAt(now) != true
            || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
            || context.HumanCommitContextLease != _contextId
            || !ReferenceEquals(context.HumanCommitAuditSubject, selection.HumanScope.Subject))
            throw new UnauthorizedAccessException("Guidance requires its original selected Runtime human owner.");
    }

    private void RequireConnection(Guid tenantId, DateTimeOffset now)
    {
        RequireSelection(tenantId, now);
        if (_connection is null || _interrupted || !_connection.Matches(context)
            || _connection.State != ConnectionState.Open || _connection.BackendProcessId != _backend)
            throw new UnauthorizedAccessException("The original guidance connection changed.");
    }

    private void RequireMutation(IHumanLearningPolicyMutation mutation)
    {
        RequireConnection(mutation.TenantId, clock.GetUtcNow());
        if (!ReferenceEquals(preparation.CurrentMutation, mutation) || !ReferenceEquals(_command, mutation.OriginalCommand)
            || _original is null || !ReferenceEquals(_original.Source, mutation.OriginalSources)
            || selection.HumanScope?.Subject.PrincipalId != mutation.SubjectId)
            throw new UnauthorizedAccessException("Only this issuer's original guidance action may write.");
    }

    private void RequirePhysicalOwner(IHumanLearningPolicyMutation mutation)
    {
        RequireMutation(mutation);
        if (_transaction is null || _transaction.IsolationLevel != IsolationLevel.Serializable
            || System.Transactions.Transaction.Current is not null
            || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), _transaction)
            || !_connection!.Owns(_transaction))
            throw new UnauthorizedAccessException("The original Serializable guidance owner changed.");
    }

    private static ConcurrencyConflictException Conflict(string message) => new("learning_policy_changed", message);
    private void ConnectionChanged(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState != ConnectionState.Open) _interrupted = true;
    }
    public void Dispose()
    {
        if (_connection is not null) _connection.StateChange -= ConnectionChanged;
    }

    private sealed record OriginalSources(LearningGuidanceDocumentSource? Document, bool AllowTenantEdits)
        : ILearningPolicyOriginalSources;
    private sealed record Governance(Guid Id, string SettingKey, string Value, bool IsLocked,
        int SettingValueTypeId, string? AllowedValues, string? Description, string? Category, int DisplayOrder);
    private sealed record Snapshot(OriginalSources Source, Governance? Governance, TenantSettingsDocument? Row,
        string? DocumentImage, string PolicySourceStamp, LearningGuidancePolicy Policy);
}
