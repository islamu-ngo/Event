using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// One request's checkpoint business evidence, retained on its original selected physical
/// context. Only the original preparation issuer enters and acquires the committing owner.
/// Neither transport success nor the journal can certify authorization or settlement.
/// </summary>
public sealed class RegistrationProviderDeferredCheckpointJournal(
    Func<ExploreDbContext> resolveContext,
    Func<IHumanEventProviderMutationPreparation> resolvePreparation)
    : IRegistrationProviderDeferredCheckpointJournal
{
    private ExploreDbContext context => resolveContext();
    private readonly object _gate = new();
    private readonly List<ObservedEffect> _effects = [];
    private Stage _stage;
    private Custody? _custody;

    public async Task<IRegistrationProviderCheckpointObservation<T>> ObserveAsync<T>(
        HumanEventProviderSourceTarget target, IPreparedHumanEventProviderMutation prepared,
        Func<CancellationToken, Task<T>> observe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(observe);
        lock (_gate)
        {
            if (_stage != Stage.Idle) throw Rejected();
            _stage = Stage.Starting;
        }
        try
        {
            var command = target.OriginalCommand as ICommand<BaseCommandResponse<Guid>> ?? throw Rejected();
            if (target.Operation is not (HumanEventProviderOperation.Poll
                or HumanEventProviderOperation.PublishBinding or HumanEventProviderOperation.ImportVersion))
                throw Rejected();
            RequireIdle(target.TenantId);
            var issuer = resolvePreparation();
            await prepared.ValidateAsync(command, cancellationToken);
            RequireIdle(target.TenantId);
            Guid connectionId = target.Operation == HumanEventProviderOperation.ImportVersion
                ? target.ConnectionId ?? throw Rejected()
                : await context.RegistrationProviderBindings.AsNoTracking()
                    .Where(row => row.TenantId == target.TenantId && row.Id == target.BindingId)
                    .Select(row => row.RegistrationProviderConnectionId).SingleAsync(cancellationToken);
            var connection = await ReadConnectionAsync(target.TenantId, connectionId, cancellationToken);
            RequireIdle(target.TenantId);
            var custody = new Custody(command, prepared, issuer, target, context.ContextId,
                RelationalConnectionIdentity.Capture(context), context.HumanCommitAuditSubject ?? throw Rejected(),
                ConnectionImage.From(connection));
            lock (_gate)
            {
                _custody = custody;
                _stage = Stage.Observing;
            }
            // The callback includes provider, credential and storage reads, all outside leases.
            T result = await observe(cancellationToken);
            ImmutableArray<ObservedEffect> effects;
            lock (_gate)
            {
                RequireCustody(custody, inOwner: false);
                _stage = Stage.Sealed;
                effects = [.. _effects];
                _effects.Clear();
            }
            await prepared.ValidateAsync(command, cancellationToken);
            RequireCustody(custody, inOwner: false);
            if (ConnectionImage.From(await ReadConnectionAsync(target.TenantId, connectionId, cancellationToken))
                != custody.Connection)
                throw Rejected();
            RequireCustody(custody, inOwner: false);
            return new Observation<T>(this, custody, result, effects);
        }
        catch
        {
            lock (_gate)
            {
                _stage = Stage.Consumed;
                _effects.Clear();
            }
            throw;
        }
    }

    public bool TryDefer(Guid tenantId, Guid connectionId, RegistrationProviderCheckpointEffect effect,
        DateTime observedAtUtc)
    {
        lock (_gate)
        {
            if (!Enum.IsDefined(effect) || observedAtUtc.Kind != DateTimeKind.Utc
                || observedAtUtc <= DateTime.UnixEpoch)
                throw Rejected();
            if (_stage == Stage.Idle)
            {
                // A worker keeps its existing immediate persistence semantics. A selected human
                // cannot accidentally fall through because the journal was omitted or miswired.
                if (context.HasHumanCommitContextLease || context.HumanCommitAuditSubject is not null)
                    throw Rejected();
                return false;
            }
            var custody = _custody ?? throw Rejected();
            if (_stage != Stage.Observing || tenantId != custody.Target.TenantId
                || connectionId != custody.Connection.Id)
                throw Rejected();
            RequireCustody(custody, inOwner: false);
            _effects.Add(new(effect, observedAtUtc));
            return true;
        }
    }

    public async Task<TResult> ExecuteAsync<T, TResult>(
        ICommand<BaseCommandResponse<Guid>> command, IPreparedHumanEventProviderMutation prepared,
        IRegistrationProviderCheckpointObservation<T> observation,
        Func<T, CancellationToken, Task<TResult>> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (observation is not Observation<T> retained || !ReferenceEquals(retained.Issuer, this))
            throw Rejected();
        lock (_gate)
        {
            if (_stage != Stage.Sealed || !ReferenceEquals(_custody, retained.Custody))
                throw Rejected();
            // Spend before any validation/await: rejection, conflict and unknown acknowledgment
            // all leave the remote result non-replayable.
            _stage = Stage.Consumed;
        }
        var custody = retained.Custody;
        if (!ReferenceEquals(command, custody.Command) || !ReferenceEquals(prepared, custody.Prepared)
            || !ReferenceEquals(resolvePreparation(), custody.PreparationIssuer))
            throw Rejected();
        RequireCustody(custody, inOwner: false);
        return await custody.PreparationIssuer.ExecutePreparedAsync(command, prepared, async token =>
        {
            var transaction = RequireCustody(custody, inOwner: true)!;
            var current = await ReadConnectionAsync(custody.Target.TenantId, custody.Connection.Id, token);
            if (ConnectionImage.From(current) != custody.Connection) throw Rejected();
            var tracked = await context.RegistrationProviderConnections.SingleAsync(
                row => row.TenantId == custody.Target.TenantId && row.Id == custody.Connection.Id, token);
            if (ConnectionImage.From(tracked) != custody.Connection) throw Rejected();
            RequireSameTransaction(custody, transaction);
            foreach (var effect in retained.Effects)
            {
                switch (effect.Kind)
                {
                    case RegistrationProviderCheckpointEffect.AccessValidated:
                        tracked.RecordAccessValidated(effect.ObservedAtUtc);
                        break;
                    case RegistrationProviderCheckpointEffect.CredentialRefreshed:
                        tracked.RecordCredentialRefresh(effect.ObservedAtUtc);
                        break;
                    default:
                        throw Rejected();
                }
            }
            if (!retained.Effects.IsEmpty)
                await context.SaveChangesAsync(token);
            RequireSameTransaction(custody, transaction);
            TResult result = await mutation(retained.Result, token);
            RequireSameTransaction(custody, transaction);
            return result;
        }, cancellationToken);
    }

    private Task<RegistrationProviderConnection> ReadConnectionAsync(Guid tenantId, Guid connectionId,
        CancellationToken cancellationToken) =>
        context.RegistrationProviderConnections.AsNoTracking()
            .SingleAsync(row => row.TenantId == tenantId && row.Id == connectionId, cancellationToken);

    private void RequireIdle(Guid tenantId)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsRelational() || !context.HasHumanCommitContextLease
            || context.HumanCommitAuditSubject is null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.BindTenantLease() != tenantId
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw Rejected();
    }

    private DbTransaction? RequireCustody(Custody custody, bool inOwner)
    {
        context.ValidateTenantLease();
        if (context.ContextId != custody.ContextId || !context.HasHumanCommitContextLease
            || !ReferenceEquals(context.HumanCommitAuditSubject, custody.Subject)
            || context.TenantFilterTenantId != custody.Target.TenantId || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner
            || !custody.PhysicalConnection.Matches(context)
            || System.Transactions.Transaction.Current is not null)
            throw Rejected();
        if (!inOwner)
        {
            if (context.Database.CurrentTransaction is not null) throw Rejected();
            return null;
        }
        if (!context.HumanCommitAttemptOwnsTransaction
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !custody.PhysicalConnection.Owns(transaction))
            throw Rejected();
        return transaction;
    }

    private void RequireSameTransaction(Custody custody, DbTransaction transaction)
    {
        if (!ReferenceEquals(RequireCustody(custody, inOwner: true), transaction)) throw Rejected();
    }

    private static InvalidOperationException Rejected() =>
        new("registration_provider_checkpoint_custody_rejected");

    private enum Stage { Idle, Starting, Observing, Sealed, Consumed }
    private sealed record ObservedEffect(RegistrationProviderCheckpointEffect Kind, DateTime ObservedAtUtc);
    private sealed record Custody(ICommand<BaseCommandResponse<Guid>> Command,
        IPreparedHumanEventProviderMutation Prepared, IHumanEventProviderMutationPreparation PreparationIssuer,
        HumanEventProviderSourceTarget Target, DbContextId ContextId, RelationalConnectionIdentity PhysicalConnection,
        HumanCommitSubject Subject, ConnectionImage Connection);
    private sealed class Observation<T>(RegistrationProviderDeferredCheckpointJournal issuer, Custody custody,
        T result, ImmutableArray<ObservedEffect> effects) : IRegistrationProviderCheckpointObservation<T>
    {
        internal RegistrationProviderDeferredCheckpointJournal Issuer { get; } = issuer;
        internal Custody Custody { get; } = custody;
        internal ImmutableArray<ObservedEffect> Effects { get; } = effects;
        public T Result { get; } = result;
        public override string ToString() => nameof(IRegistrationProviderCheckpointObservation<T>);
    }

    private sealed record ConnectionImage(Guid Id, Guid TenantId, Guid Stamp, string Name,
        int Kind, int DeploymentKind, string Provider, string Deployment, string ApiVersion,
        string AdapterPolicyVersion, string ConformanceEvidenceRevision, string ManagementUrl,
        string PublicUrl, string Workspace, string Scopes, string Identity, string PubSub,
        Guid? ApiSecret, Guid? WebhookSecret, DateTime? RefreshedAt, DateTime? ValidatedAt, bool Deleted)
    {
        internal static ConnectionImage From(RegistrationProviderConnection value) => new(value.Id,
            value.TenantId, value.ConcurrencyStamp, value.Name, value.ProviderKindId, value.DeploymentKindId,
            value.ProviderCode, value.ProviderDeploymentCode, value.ApiVersion, value.AdapterPolicyVersion,
            value.ConformanceEvidenceRevision, value.ManagementApiBaseUrl, value.PublicBaseUrl,
            value.ProviderWorkspaceId, value.GrantedOAuthScopes, value.ProviderIdentity,
            value.PubSubConfigurationReference, value.ApiTokenSecretBindingId, value.WebhookSecretBindingId,
            value.LastCredentialRefreshAt, value.LastAccessValidatedAt, value.IsDeleted);
    }
}
