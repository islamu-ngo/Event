using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>
/// Retains an actual selected Runtime delivery before a separate creator is selected.
/// This issuer cannot mint a delivery from request identifiers or a copied claim.
/// </summary>
internal sealed class ManagedTenantWorkerCreationAdmission(
    ExploreDbContext context, ExploreDbContextExecutionLease selection,
    IGenericOutboxExecutionAuthority execution, IOutboxRepository outbox, TimeProvider clock)
    : IManagedTenantWorkerCreationAdmission
{
    private Invocation? current;
    private bool attempted;
    private ExploreDbContext Context => context;
    private ExploreDbContextExecutionLease Selection => selection;
    private IGenericOutboxExecutionAuthority Execution => execution;
    private IOutboxRepository Outbox => outbox;
    private TimeProvider Clock => clock;

    public async ValueTask<IManagedTenantWorkerCreationInvocation> AcquireAsync(Guid operationId,
        Guid outboxMessageId, CancellationToken cancellationToken = default)
    {
        if (attempted)
            throw Denied();
        attempted = true;
        selection.RequireOriginalOwner(context);
        var claim = context.GenericOutboxExecutionClaim;
        if (current is not null || operationId == Guid.Empty || outboxMessageId == Guid.Empty
            || selection.HumanScope is not null || selection.PolicyPurpose is not null
            || !context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not null
            || claim is null || claim.Ownership != GenericOutboxOwnership.Instance || claim.TenantId is not null
            || claim.MessageId != outboxMessageId || claim.SourceId != operationId || claim.AggregateId != operationId
            || claim.AggregateType != nameof(ManagedTenantProvisioningOperation)
            || claim.EventType != "ManagedTenantProvisioningProcessRequested"
            || claim.Status != OutboxMessageStatus.Processing)
            throw Denied();

        var connection = RelationalConnectionIdentity.Capture(context);
        bool ownsOpen = connection.State != ConnectionState.Open;
        if (ownsOpen)
            await context.Database.OpenConnectionAsync(cancellationToken);
        var issued = new Invocation(this, claim, connection, ownsOpen);
        current = issued;
        try
        {
            await issued.RequireCurrentAsync(cancellationToken);
            return issued;
        }
        catch
        {
            await issued.DisposeAsync();
            throw;
        }
    }

    /// <summary>Checks private issuance before any creator context is rented.</summary>
    internal void RequireIssued(IManagedTenantWorkerCreationInvocation invocation)
    {
        if (invocation is not Invocation issued || !ReferenceEquals(issued.Owner, this)
            || !ReferenceEquals(current, issued))
            throw Denied();
        issued.RequirePhysicalOwner();
    }

    internal async Task RequireEnvelopeCurrentAsync(IManagedTenantWorkerCreationInvocation invocation,
        CancellationToken cancellationToken)
    {
        RequireIssued(invocation);
        await ((Invocation)invocation).RequireEnvelopeCurrentAsync(cancellationToken);
        RequireIssued(invocation);
    }

    private static UnauthorizedAccessException Denied() => new("managed_worker_creation_not_admitted");

    private sealed class Invocation : IManagedTenantWorkerCreationInvocation
    {
        private readonly GenericOutboxClaim claim;
        private readonly RelationalConnectionIdentity connection;
        private readonly Microsoft.EntityFrameworkCore.DbContextId contextId;
        private readonly int? backend;
        private readonly bool ownsOpen;
        private bool active = true;
        private bool interrupted;

        internal Invocation(ManagedTenantWorkerCreationAdmission owner, GenericOutboxClaim claim,
            RelationalConnectionIdentity connection, bool ownsOpen)
        {
            Owner = owner;
            this.claim = claim;
            this.connection = connection;
            this.ownsOpen = ownsOpen;
            contextId = owner.Context.ContextId;
            backend = connection.BackendProcessId;
            connection.StateChange += ConnectionChanged;
        }

        internal ManagedTenantWorkerCreationAdmission Owner { get; }
        public Guid OperationId => claim.AggregateId;
        public Guid OutboxMessageId => claim.MessageId;

        internal void RequirePhysicalOwner()
        {
            Owner.Selection.RequireOriginalOwner(Owner.Context);
            if (!active || interrupted || !ReferenceEquals(Owner.current, this)
                || Owner.Clock.GetUtcNow().UtcDateTime >= claim.LeaseExpiresAt
                || !ReferenceEquals(Owner.Context.GenericOutboxExecutionClaim, claim)
                || Owner.Context.ContextId != contextId || Owner.Context.Database.CurrentTransaction is not null
                || Owner.Context.IsTenantFilterBypassed || Owner.Context.TenantFilterTenantId is not null
                || !connection.Matches(Owner.Context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend)
                throw Denied();
        }

        public async Task RequireCurrentAsync(CancellationToken cancellationToken = default)
        {
            RequirePhysicalOwner();
            DateTime now = Owner.Clock.GetUtcNow().UtcDateTime;
            if (now >= claim.LeaseExpiresAt)
                throw Denied();
            var message = await Owner.Outbox.GetClaimedGenericAsync(claim.MessageId, claim.LeaseExpiresAt,
                OutboxMessageStatus.Processing, now, cancellationToken) ?? throw Denied();
            if (message.Payload is not null)
                throw Denied();
            await Owner.Execution.RequireAsync(message, cancellationToken);
            RequirePhysicalOwner();
            if (Owner.Clock.GetUtcNow().UtcDateTime >= claim.LeaseExpiresAt)
                throw Denied();
        }

        internal async Task RequireEnvelopeCurrentAsync(CancellationToken cancellationToken)
        {
            RequirePhysicalOwner();
            var message = await Owner.Outbox.GetClaimedGenericAsync(claim.MessageId, claim.LeaseExpiresAt,
                OutboxMessageStatus.Processing, Owner.Clock.GetUtcNow().UtcDateTime, cancellationToken)
                ?? throw Denied();
            if (message.Payload is not null || Owner.Execution is not GenericOutboxExecutionAuthority original)
                throw Denied();
            original.RequireCurrentEnvelope(message);
            RequirePhysicalOwner();
        }

        private void ConnectionChanged(object? sender, StateChangeEventArgs args)
        {
            if (active && args.CurrentState != ConnectionState.Open)
                interrupted = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!active)
                return;
            active = false;
            connection.StateChange -= ConnectionChanged;
            if (ReferenceEquals(Owner.current, this))
                Owner.current = null;
            if (ownsOpen)
                await Owner.Context.Database.CloseConnectionAsync();
        }
    }
}
