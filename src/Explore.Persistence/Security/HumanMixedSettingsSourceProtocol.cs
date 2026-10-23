using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using Explore.Application.Responses;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Security;

internal sealed class HumanMixedSettingsSourceProtocol : IHumanMixedSettingsSourceProtocol
{
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease selection;
    private readonly RelationalConnectionIdentity connection;
    private readonly DbContextId contextId;
    private readonly string dataSource;
    private readonly string database;
    private readonly int? backend;
    private readonly UpdateSettingBatchCommand? original;
    private readonly TypedSettingsOriginal? typedOriginal;
    private readonly ITypedSettingsOriginalStore? typedStore;
    private readonly PostgresNativeQueueEnrollment? enrollment;
    private DbTransaction? transaction;
    private string? nativeOriginal;
    private string? data;
    private int state;

    internal HumanMixedSettingsSourceProtocol(ExploreDbContext context, ExploreDbContextExecutionLease selection,
        HumanSupportForwardingObservation forwarding, UpdateSettingBatchCommand original)
        : this(context, selection, forwarding)
    {
        this.original = original;
    }

    internal HumanMixedSettingsSourceProtocol(ExploreDbContext context, ExploreDbContextExecutionLease selection,
        HumanSupportForwardingObservation forwarding, TypedSettingsOriginal original, ITypedSettingsOriginalStore store)
        : this(context, selection, forwarding)
    {
        typedOriginal = original;
        typedStore = store;
    }

    private HumanMixedSettingsSourceProtocol(ExploreDbContext context, ExploreDbContextExecutionLease selection,
        HumanSupportForwardingObservation forwarding)
    {
        this.context = context;
        this.selection = selection;
        Subject = selection.HumanScope!.Subject;
        Lifetime = selection.HumanLifetime!;
        Forwarding = forwarding;
        TenantId = selection.SelectedTenantId;
        enrollment = selection.HumanScope.Enrollment;
        connection = RelationalConnectionIdentity.Capture(context);
        contextId = context.ContextId;
        if (connection.State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Mixed source construction requires the original open physical connector.");
        dataSource = connection.DataSource;
        database = connection.Database;
        backend = connection.BackendProcessId;
        RequireSelection();
    }

    public HumanCommitSubject Subject { get; }
    public HumanAuthenticationLifetime Lifetime { get; }
    public HumanSupportForwardingObservation Forwarding { get; }
    public Guid? TenantId { get; }
    public string OriginalData => data ?? throw new InvalidOperationException("The original mixed image has not been observed.");

    public async Task<HumanEmailSourceFacts> ObserveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
            throw new InvalidOperationException("The original mixed source can only be observed once.");
        RequireSelection();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Mixed first observation requires the idle original owner.");
        try
        {
            var observed = await ReadAsync(false, cancellationToken);
            data = observed.Data;
            nativeOriginal = observed.Native;
            RequireSelection();
            return observed.Facts;
        }
        catch { state = 4; throw; }
    }

    public async Task<HumanEmailSourceFacts> AcquireAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref state, 2, 1) != 1)
            throw new InvalidOperationException("Mixed source acquisition belongs to one physical attempt.");
        try
        {
            transaction = RequireTransaction();
            var acquired = await ReadAsync(true, cancellationToken);
            if (typedOriginal?.PlanAssignment is { } plan
                && MixedSettingsDataImage.PlanReadTypes.Any(type =>
                    !JsonNode.DeepEquals(JsonNode.Parse(OriginalData)![type.Name], JsonNode.Parse(acquired.Data)![type.Name])))
                throw new TenantPlanOriginalRejectedException(BaseCommandResponse.Failure(
                    "tenant_plan_selection_changed", "tenant_plan_selection_changed",
                    ["tenant_plan_selection_changed"], plan.AssignmentId));
            if (!MixedSettingsDataImage.Same(OriginalData, acquired.Data))
                throw new UnauthorizedAccessException("Original mixed row, absence, metadata or projection sources changed.");
            if (context.Database.IsNpgsql() && TenantId is { } selectedTenant
                && acquired.Facts.Principal.SelectedTenantId == selectedTenant
                && acquired.Facts.Principal.Tenants.Count(row => row.Id == selectedTenant && row.IsActiveState) == 1)
                context.RetainNativeHumanTenantOwner(Subject, selectedTenant);
            state = 3;
            return acquired.Facts;
        }
        catch (PostgresException exception) when (typedOriginal?.PlanAssignment is not null
            && exception.MessageText == "typed_settings_plan_selection_changed")
        {
            state = 4;
            throw new TenantPlanOriginalRejectedException(BaseCommandResponse.Failure(
                "tenant_plan_selection_changed", "tenant_plan_selection_changed",
                ["tenant_plan_selection_changed"], typedOriginal.PlanAssignment.AssignmentId));
        }
        catch { state = 4; throw; }
    }

    public async Task<HumanEmailSourceFacts> RequireOwnedAsync(CancellationToken cancellationToken)
    {
        if (state != 3 || !ReferenceEquals(transaction, RequireTransaction()))
            throw new UnauthorizedAccessException("The original acquired mixed source is required.");
        var current = await ReadAsync(false, cancellationToken);
        if (typedOriginal is not null)
            typedStore!.RequireObservedData(typedOriginal, current.Data);
        if (!ReferenceEquals(transaction, RequireTransaction()))
            throw new UnauthorizedAccessException("The mixed source owner changed while reading.");
        return current.Facts;
    }

    private void RequireSelection()
    {
        selection.RequireOriginalOwner(context);
        if (selection.PolicyPurpose != PolicyMutationPurpose.MixedSettings
            || selection.HumanScope?.Purpose != (TenantId.HasValue
                ? HumanAdministratorCommitPurpose.MixedSettingsTenant : HumanAdministratorCommitPurpose.MixedSettingsInstance)
            || !ReferenceEquals(selection.HumanScope.Subject, Subject)
            || !ReferenceEquals(selection.HumanLifetime, Lifetime) || !ReferenceEquals(Forwarding.Subject, Subject)
            || context.ContextId != contextId || !connection.Matches(context)
            || connection.State != ConnectionState.Open || connection.DataSource != dataSource || connection.Database != database
            || connection.BackendProcessId is { } processId && processId != backend
            || context.HumanCommitContextLease != contextId || !ReferenceEquals(context.HumanCommitAuditSubject, Subject)
            || context.TenantFilterTenantId != TenantId || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || TenantId.HasValue && Forwarding.ResolvedTenantId != TenantId)
            throw new UnauthorizedAccessException("Original mixed physical selection changed.");
    }

    private DbTransaction RequireTransaction()
    {
        RequireSelection();
        if (connection.State != ConnectionState.Open || System.Transactions.Transaction.Current is not null
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } selected
            || selected.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(selected))
            throw new UnauthorizedAccessException("Mixed effects require their original Serializable connection.");
        return selected;
    }

    private Task<(HumanEmailSourceFacts Facts, string Data, string? Native)> ReadAsync(
        bool acquire, CancellationToken token)
    {
        RequireSelection();
        return HumanMixedSettingsSourceQueries.ReadAsync(context, enrollment, Subject, Lifetime, Forwarding,
            TenantId, original, typedOriginal, nativeOriginal, acquire, RequireSelection, token);
    }
}
