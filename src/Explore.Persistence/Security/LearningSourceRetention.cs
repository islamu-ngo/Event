using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Retains one closed source image on the original selected human owner, without mutation authority.</summary>
internal sealed class LearningSourceRetention(
    ExploreDbContext context,
    ExploreDbContextExecutionLease selection,
    IInstanceTenantSettingsReader settings,
    TimeProvider clock,
    PostgresNativeQueueEnrollment? enrollment = null) : ILearningSourceRetention, IDisposable
{
    internal const string GuidanceDocumentKey = SettingsDocumentKeys.Tenant.LearningGuidance;
    internal const string AllowTenantEditsKey = "learning.guidance.allow_tenant_edits";
    internal static readonly IReadOnlyList<string> Keys = Array.AsReadOnly(
        AiAssistantSettingGroup.SettingKeys.Concat([
            GovernanceSettingKeys.Events.UserSubmissionEnabled,
            GovernanceSettingKeys.TenantDelegation.LockAiAssistant,
            AllowTenantEditsKey
        ]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    private readonly Guid _tenant = selection.SelectedTenantId ?? Guid.Empty;
    private readonly DbContextId _contextId = context.ContextId;
    private readonly object? _human = selection.HumanScope;
    private readonly object? _lifetime = selection.HumanLifetime;
    private RelationalConnectionIdentity? _connection;
    private int? _backend;
    private DbTransaction? _transaction;
    private Observation? _observed;
    private bool _interrupted;
    private int _phase;

    public async Task<ILearningSourceObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _phase, 1, 0) != 0)
            throw new InvalidOperationException("Learning sources permit one original observation.");
        try
        {
            RequireSelection(clock.GetUtcNow());
            if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("Learning observation must precede the original transaction.");
            if (context.Database.GetDbConnection().State != ConnectionState.Open)
                await context.Database.OpenConnectionAsync(cancellationToken);
            _connection = RelationalConnectionIdentity.Capture(context);
            _backend = _connection.BackendProcessId;
            _connection.StateChange += ConnectionChanged;
            var captured = await CaptureAsync(cancellationToken);
            RequireConnection(clock.GetUtcNow());
            return _observed = new(captured.Facts, captured.Image);
        }
        catch { _phase = 4; throw; }
    }

    public async Task<ILearningSourceLease> AcquireAsync(
        ILearningSourceObservation observation, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(observation, _observed) || _observed is null
            || Interlocked.CompareExchange(ref _phase, 2, 1) != 1)
            throw new UnauthorizedAccessException("Only this issuer's original observation may be acquired once.");
        try
        {
            _transaction = RequireOwner(clock.GetUtcNow());
            await new LearningSourceProviderLock(context, _connection!, _tenant)
                .AcquireAsync(_transaction, cancellationToken);
            var current = await CaptureAsync(cancellationToken);
            if (current.Image != _observed.Image)
                throw new ConcurrencyConflictException("learning_source_changed",
                    "The observed learning sources or their absence changed.");
            RequireOwner(clock.GetUtcNow());
            _phase = 3;
            return new Lease(this, _observed.Facts);
        }
        catch { _phase = 4; throw; }
    }

    private async Task<(LearningSourceFacts Facts, string Image)> CaptureAsync(CancellationToken token)
    {
        var hierarchy = await settings.ReadAsync(_tenant, Keys, token);
        var system = hierarchy.System.Where(row => Keys.Contains(row.SettingKey, StringComparer.Ordinal))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenant = hierarchy.Tenant.Where(row => row.TenantId == _tenant
                && row.SettingKey != AllowTenantEditsKey && Keys.Contains(row.SettingKey, StringComparer.Ordinal))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        LearningGuidanceDocumentSource? documentSource;
        if (context.Database.IsNpgsql())
            documentSource = await PostgresMixedSettingsContract.ReadLearningGuidanceAsync(context,
                enrollment ?? throw new InvalidOperationException("The selected PostgreSQL enrollment is required."),
                _tenant, token);
        else
        {
            var document = await context.Set<TenantSettingsDocument>().AsNoTracking().SingleOrDefaultAsync(
                row => row.TenantId == _tenant && row.DocumentKey == GuidanceDocumentKey, token);
            documentSource = document is null ? null : new LearningGuidanceDocumentSource(
                document.Id, document.TenantId, document.SchemaVersion, document.DefaultsVersion,
                document.PayloadJson, document.ConcurrencyStamp);
        }
        object? SystemImage(string key) => system.TryGetValue(key, out var row)
            ? new { row.Id, row.SettingKey, row.Value, row.IsLocked, row.SettingValueTypeId,
                row.AllowedValues, row.Description, row.Category, row.DisplayOrder } : null;
        object? TenantImage(string key) => tenant.TryGetValue(key, out var row)
            ? new { row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked } : null;
        var workspace = Keys.Where(key => key != AllowTenantEditsKey)
            .Select(key => new { Key = key, System = SystemImage(key), Tenant = TenantImage(key) }).ToArray();
        string workspaceImage = JsonSerializer.Serialize(workspace);
        string policyImage = JsonSerializer.Serialize(new
        {
            DefaultsVersion = "learning-guidance:1",
            Document = documentSource,
            Governance = SystemImage(AllowTenantEditsKey)
        });
        var effective = Keys.Select(key => HierarchicalSettingMerge.Resolve(key, system,
                key == AllowTenantEditsKey ? null : tenant))
            .Where(value => value is not null).Select(value => value!)
            .ToDictionary(value => value.Key, StringComparer.Ordinal);
        bool personalCreation = effective.TryGetValue(GovernanceSettingKeys.Events.UserSubmissionEnabled, out var creation)
            && SettingValueSerializer.DeserializeBool(creation.Value, false);
        bool allowTenantEdits = !system.TryGetValue(AllowTenantEditsKey, out var governance)
            || SettingValueSerializer.DeserializeBool(governance.Value, true);
        var facts = new LearningSourceFacts(_tenant, Hash(workspaceImage), Hash(policyImage),
            personalCreation, allowTenantEdits, documentSource, effective);
        return (facts, JsonSerializer.Serialize(new { TenantId = _tenant, Workspace = workspaceImage, Policy = policyImage }));
    }

    private void RequireSelection(DateTimeOffset now)
    {
        selection.RequireOriginalOwner(context);
        if (_tenant == Guid.Empty || selection.SelectedTenantId != _tenant
            || !ReferenceEquals(selection.HumanScope, _human) || !ReferenceEquals(selection.HumanLifetime, _lifetime)
            || selection.HumanScope?.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant
            || selection.PolicyPurpose is not (null or PolicyMutationPurpose.Standard)
            || selection.HumanLifetime?.IsValidAt(now) != true || context.ContextId != _contextId
            || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
            || context.HumanCommitContextLease != _contextId
            || !ReferenceEquals(context.HumanCommitAuditSubject, selection.HumanScope?.Subject))
            throw new UnauthorizedAccessException("The original learning source selection changed.");
    }

    private void RequireConnection(DateTimeOffset now)
    {
        RequireSelection(now);
        if (_connection is null || _interrupted || !_connection.Matches(context)
            || _connection.State != ConnectionState.Open || _connection.BackendProcessId != _backend)
            throw new UnauthorizedAccessException("The original learning source connection changed.");
    }

    private DbTransaction RequireOwner(DateTimeOffset now)
    {
        RequireConnection(now);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (System.Transactions.Transaction.Current is not null
            || transaction is null
            || transaction.IsolationLevel != IsolationLevel.Serializable || !_connection!.Owns(transaction)
            || _transaction is not null && !ReferenceEquals(_transaction, transaction))
            throw new UnauthorizedAccessException("Learning sources require the original Serializable owner.");
        return transaction;
    }

    private async Task ValidateAsync(CancellationToken token)
    {
        if (_phase != 3)
            throw new UnauthorizedAccessException("The acquired original learning source lease is required.");
        RequireOwner(clock.GetUtcNow());
        var current = await CaptureAsync(token);
        if (current.Image != _observed!.Image)
            throw new ConcurrencyConflictException("learning_source_changed", "The retained learning source image changed.");
        RequireOwner(clock.GetUtcNow());
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private void ConnectionChanged(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState != ConnectionState.Open) _interrupted = true;
    }

    public void Dispose()
    {
        _phase = 4;
        if (_connection is not null) _connection.StateChange -= ConnectionChanged;
    }

    private sealed record Observation(LearningSourceFacts Facts, string Image) : ILearningSourceObservation;
    private sealed class Lease(LearningSourceRetention owner, LearningSourceFacts facts) : ILearningSourceLease
    {
        public LearningSourceFacts Facts => facts;
        public Task ValidateAsync(CancellationToken cancellationToken) => owner.ValidateAsync(cancellationToken);
        public void RequireCurrentAt(DateTimeOffset utcNow)
        {
            if (owner._phase != 3)
                throw new UnauthorizedAccessException("The acquired original learning source lease is required.");
            owner.RequireOwner(utcNow);
        }
    }
}
