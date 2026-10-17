using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Services;

/// <summary>
/// Fixed operator-document writer on the original ordinary Global owner. It never
/// grants setup access to scalar repositories or borrows native import/Email purposes.
/// </summary>
public sealed class OperatorIdentitySettingsWriter(
    ExploreDbContext context, IUnitOfWork unitOfWork, IServiceProvider services,
    IOperatorIdentityMutationAuthority authority, IHumanCommitScopePreparation human,
    IHumanSettingsMutationPreparation preparation)
    : IOperatorIdentitySettingsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IOperatorIdentityMutationSource? selected;
    private OperatorIdentitySettingSnapshot? observed;
    private string? pendingSnapshot;
    private Action? requireSelection;
    private PostgresNativeQueueEnrollment? nativeEnrollment;
    private OperatorIdentityImportAudit? preparedAudit;
    private string? preparedAuditPayload;
    private bool auditPrepared;

    public DateTime NormalizeEffectTimestamp(DateTime timestamp) =>
        PostgresOperatorIdentityEditorContract.NormalizeEffectTimestamp(context, timestamp);

    public async Task<string?> NormalizeAuditPayloadAsync(OperatorIdentityImportAudit? audit, CancellationToken cancellationToken)
    {
        if (selected is null || requireSelection is null || auditPrepared || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("Operator audit representation requires its original idle observation.");
        requireSelection();
        authority.RequireIssued(selected);
        await selected.RequireSourcesAsync(cancellationToken);
        string? payload = audit is null ? null : JsonSerializer.Serialize(audit);
        if (nativeEnrollment is not null && payload is not null)
        {
            string canonical = await PostgresOperatorIdentityEditorContract.RepresentAuditPayloadAsync(
                context, nativeEnrollment, payload, cancellationToken);
            using var original = JsonDocument.Parse(payload);
            using var represented = JsonDocument.Parse(canonical);
            if (!JsonElement.DeepEquals(original.RootElement, represented.RootElement))
                throw new UnauthorizedAccessException("Native representation changed the original operator audit.");
            payload = canonical;
        }
        requireSelection();
        await selected.RequireSourcesAsync(cancellationToken);
        if (context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("Operator audit representation must finish before the physical attempt.");
        selected.RequireTime();
        preparedAudit = audit;
        preparedAuditPayload = payload;
        auditPrepared = true;
        return payload;
    }

    public async Task<OperatorIdentitySettingSnapshot?> ObserveAsync(IOperatorIdentityMutationSource source,
        IUnitOfWork originalOwner, CancellationToken cancellationToken)
    {
        authority.RequireIssued(source);
        if (selected is not null || context.Database.CurrentTransaction is not null
            || !ReferenceEquals(originalOwner, unitOfWork) || unitOfWork is not EfCoreUnitOfWork original)
            throw new UnauthorizedAccessException("Operator identity requires its idle original Global owner.");
        original.RequireTrustedSetupSettingsOwner(context);
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        var contextId = context.ContextId;
        var connection = RelationalConnectionIdentity.Capture(context);
        int? backend = null;
        requireSelection = () =>
        {
            lease.RequireOriginalOwner(context);
            if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                || lease.SelectedTenantId is not null
                || (lease.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
                || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
                || !connection.Matches(context)
                || backend is not null && (connection.State != ConnectionState.Open
                    || connection.BackendProcessId != backend)
                || source.IsHuman && (human.Subject is null || human.Subject.PrincipalId != source.Actor
                    || lease.HumanScope?.Purpose != HumanAdministratorCommitPurpose.GlobalInstance)
                || !source.IsHuman && (human.Subject is not null || context.HasHumanCommitContextLease))
                throw new UnauthorizedAccessException("Operator identity lost its original ordinary Global selection.");
        };
        requireSelection();
        var native = await PostgresOperatorIdentityEditorContract.TryPrepareAsync(context, connection,
            () => PostgresNativeQueueEnrollment.Bind(services.GetRequiredService<IConfiguration>()), cancellationToken);
        if (native is { } prepared)
        {
            nativeEnrollment = prepared.Enrollment;
            backend = prepared.Backend;
        }
        selected = source;
        await source.RequireSourcesAsync(cancellationToken);
        if (source.IsSetup)
        {
            pendingSnapshot = await PendingSourceAsync(false, cancellationToken);
        }
        observed = await ReadSettingAsync(cancellationToken);
        requireSelection();
        await source.RequireSourcesAsync(cancellationToken);
        source.RequireTime();
        return observed;
    }

    public async Task ExecuteAsync(OperatorIdentityMutation mutation, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(selected, mutation.Source) || requireSelection is null
            || !ReferenceEquals(unitOfWork, mutation.OriginalOwner) || mutation.Before != observed
            || !auditPrepared || mutation.Audit != preparedAudit || mutation.AuditPayload != preparedAuditPayload)
            throw new UnauthorizedAccessException("Operator identity effects require their original observation.");
        mutation.ClaimOnce();
        requireSelection();
        authority.RequireIssued(mutation.Source);
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Operator identity requires its original physical transaction.");
        void RequireOwner()
        {
            requireSelection();
            mutation.RequireActive();
            if (!ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !connection.Owns(transaction)
                || transaction.IsolationLevel != IsolationLevel.Serializable
                || !context.HumanCommitAttemptOwnsTransaction)
                throw new UnauthorizedAccessException("Operator identity requires one non-replaying Serializable attempt.");
        }
        async Task RequireSources(CancellationToken token)
        {
            RequireOwner();
            await mutation.Source.RequireSourcesAsync(token);
            if (mutation.Source.IsSetup)
            {
                await using var setupFence = await RelationalNamedLock.AcquireTransactionAsync(
                    context, "explore:instance-onboarding", token);
                if (nativeEnrollment is null)
                    _ = await RelationalInstanceBootstrapStateLock.LoadCurrentAsync(context, token);
                // The lock loader tracks rows. The source comparison must read actual storage,
                // including changes made later by this owner, rather than reuse tracked values.
                if (await PendingSourceAsync(true, token) != pendingSnapshot)
                    throw new UnauthorizedAccessException("The original operator identity pending snapshot changed.");
            }
            RequireOwner();
        }
        RequireOwner();
        var saved = JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(mutation.After.Value, JsonOptions)
            ?? throw new UnauthorizedAccessException("The operator identity projection is missing.");
        if (saved.OperatorId is null || saved.Revision is null || mutation.After.ValueType != SettingValueType.Json
            || (mutation.Before is null ? mutation.After.CreatedBy : mutation.After.UpdatedBy) != mutation.Actor
            || (mutation.OriginalCommand is ImportInstanceOperatorIdentityCommand) != (mutation.Audit is not null)
            || mutation.OriginalCommand is not (SaveInstanceOperatorIdentityCommand or ImportInstanceOperatorIdentityCommand))
            throw new UnauthorizedAccessException("The operator identity effect or audit does not match its original command.");
        await RequireSources(cancellationToken);
        if (mutation.Source.IsHuman)
            await preparation.RequireCurrentOwnedAsync(SettingScope.Instance, null, null,
                InstanceOperatorIdentitySettingKeys.OperatorIdentity, HumanSettingsMutationKind.SetValue,
                mutation.After.Value, cancellationToken: cancellationToken);
        var current = await ReadSettingAsync(cancellationToken);
        if (current != observed)
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                "The original operator identity source changed before acquisition.", nameof(SystemSetting),
                InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        if (context.ChangeTracker.Entries<SystemSetting>().Any(entry =>
                entry.Entity.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity
                && (entry.State != EntityState.Unchanged || OperatorIdentitySettingSnapshot.From(entry.Entity) != observed)))
            throw new UnauthorizedAccessException("Tracked operator identity state differs from its original storage source.");
        var after = mutation.After;
        string? auditPayload = mutation.AuditPayload;
        if (mutation.Audit is { } expectedAudit
            && (expectedAudit.ActorUserId != mutation.Actor || expectedAudit.CommittedRevision != saved.Revision))
            throw new UnauthorizedAccessException("Operator identity audit and projection diverged.");
        if (nativeEnrollment is not null)
        {
            RequireOwner();
            await PostgresOperatorIdentityEditorContract.WriteAsync(context, nativeEnrollment, mutation, cancellationToken);
        }
        else
        {
            var existing = current is null ? null : await context.SystemSettings.SingleAsync(
                row => row.Id == current.Id, cancellationToken);
            if (existing is null)
            {
                existing = new SystemSetting
                {
                    Id = after.Id,
                    SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                    Value = after.Value,
                    ValueType = after.ValueType,
                    IsLocked = after.IsLocked,
                    AllowedValues = after.AllowedValues,
                    Description = after.Description,
                    Category = after.Category,
                    DisplayOrder = after.DisplayOrder,
                    CreatedAt = after.CreatedAt,
                    CreatedBy = after.CreatedBy,
                    UpdatedAt = after.UpdatedAt,
                    UpdatedBy = after.UpdatedBy
                };
                context.SystemSettings.Add(existing);
            }
            else
            {
                existing.Value = after.Value;
                existing.ValueType = after.ValueType;
                existing.UpdatedAt = after.UpdatedAt;
                existing.UpdatedBy = after.UpdatedBy;
            }
            if (mutation.Audit is not null)
                context.OutboxMessages.Add(new()
                {
                    Id = mutation.AuditId,
                    AggregateType = OperatorIdentityImportAudit.AggregateType,
                    AggregateId = saved.OperatorId.Value,
                    EventType = OperatorIdentityImportAudit.EventType,
                    Payload = auditPayload,
                    Status = OutboxMessageStatus.Pending,
                    CreatedAt = mutation.OccurredAt,
                    MaxRetries = 5
                });
            await context.SaveChangesAsync(cancellationToken);
        }
        async Task RequireEffects(CancellationToken token)
        {
            RequireOwner();
            var row = await ReadSettingAsync(token);
            if (row != after)
                throw new UnauthorizedAccessException("The exact operator identity effect and audit columns changed.");
            // Equality covers the complete JSON projection, not merely its readiness or revision.
            var audit = nativeEnrollment is not null
                ? await PostgresOperatorIdentityEditorContract.ReadAuditAsync(context, nativeEnrollment, mutation.AuditId, token)
                : await context.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(item => item.Id == mutation.AuditId, token);
            if (mutation.Audit is null ? audit is not null : audit is null
                || audit.AggregateType != OperatorIdentityImportAudit.AggregateType
                || audit.AggregateId != saved.OperatorId || audit.EventType != OperatorIdentityImportAudit.EventType
                || audit.Payload != auditPayload || audit.Status != OutboxMessageStatus.Pending
                || audit.CreatedAt != mutation.OccurredAt || audit.MaxRetries != 5 || audit.RetryCount != 0
                || audit.ProcessedAt is not null || audit.NextRetryAt is not null || audit.LastError is not null
                || audit.DeadLetteredAt is not null)
                throw new UnauthorizedAccessException("The exact operator identity import audit changed.");
            RequireOwner();
        }
        await RequireEffects(cancellationToken);
        await RequireSources(cancellationToken);
        mutation.Source.RequireTime();
        // Existing physical owners execute these source callbacks before all final time checks.
        context.RegisterTrustedSetupSettingsAdmission(async token =>
        {
            await RequireEffects(token);
            await RequireSources(token);
        }, () => { RequireOwner(); mutation.Source.RequireTime(); });
    }

    private Task<InstanceBootstrapState?> CurrentSetupAsync(CancellationToken token) =>
        context.InstanceBootstrapStates.AsNoTracking().OrderByDescending(row => row.Generation)
            .ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefaultAsync(token);

    private async Task<string?> PendingSourceAsync(bool acquire, CancellationToken token)
    {
        if (nativeEnrollment is not null)
            return await PostgresOperatorIdentityEditorContract.ReadPendingAsync(context, nativeEnrollment, acquire, token);
        var pending = await CurrentSetupAsync(token);
        if (pending is { Status: not InstanceBootstrapStatus.Pending })
            throw new UnauthorizedAccessException("Interactive identity editing requires pending setup.");
        return JsonSerializer.Serialize(pending);
    }

    private async Task<OperatorIdentitySettingSnapshot?> ReadSettingAsync(CancellationToken token)
    {
        if (nativeEnrollment is not null)
            return await PostgresOperatorIdentityEditorContract.ReadSettingAsync(context, nativeEnrollment, token);
        var row = await context.SystemSettings.AsNoTracking().SingleOrDefaultAsync(
            item => item.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity, token);
        return row is null ? null : OperatorIdentitySettingSnapshot.From(row);
    }
}
