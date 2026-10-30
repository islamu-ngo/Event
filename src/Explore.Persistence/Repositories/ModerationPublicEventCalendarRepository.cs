using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Anonymous current calendar acquisition. Source administrators authenticate provenance,
/// never the viewer. The original privacy and primary transactions surround terminal use.
/// </summary>
public sealed partial class ModerationPublicEventCalendarRepository(
    ExploreDbContext context, ModerationCapabilityRegistry inventory, TimeProvider clock,
    IPrivacyIdentityFenceAuthority privacy) : IModerationPublicEventCalendarRepository
{
    private readonly AsyncLocal<SerializationScope?> serialization = new();
    private ExploreDbContext Context => context;
    private TimeProvider Clock => clock;
    private ModerationPolicyDefinitionSourceOwner Definitions() => new(context, inventory, clock);

    public async Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (serialization.Value is not null)
            throw new InvalidOperationException("Calendar delivery cannot nest or replay its original acquisition.");
        var entered = 0;
        var startedAt = clock.GetUtcNow();
        return await privacy.ExecuteSerializedAsync(async token =>
        {
            // A provider retry must never repeat the caller's externally visible byte delivery.
            if (Interlocked.Exchange(ref entered, 1) != 0)
                throw new InvalidOperationException("Calendar terminal delivery cannot be replayed.");
            var scope = new SerializationScope(startedAt);
            serialization.Value = scope;
            try { return await operation(token); }
            finally
            {
                scope.Active = false;
                try
                {
                    if (scope.Lease is not null) await scope.Lease.DisposeAsync();
                }
                finally { serialization.Value = null; }
            }
        }, cancellationToken);
    }

    public async Task<ModerationPublicEventCalendarAcquisition> AcquireAsync(
        ModerationPublicEventCalendarRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = serialization.Value;
        if (scope is not { Active: true, Lease: null } || request.TenantId == Guid.Empty || request.EventId == Guid.Empty
            || !context.Database.IsRelational() || context.ChangeTracker.Entries().Any()
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != request.TenantId
            || context.ModerationTrustDomainId is not null
            || context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
            return new(ModerationPublicEventCalendarOutcome.Unavailable);
        var primary = context.GetService<IDbContextOptions>().FindExtension<PrimaryDatabasePublicIdentityOptionsExtension>();
        if (primary is null || !ProviderMatches(primary)) return new(ModerationPublicEventCalendarOutcome.Unavailable);
        var priorTrust = context.ModerationTrustDomainId;
        var ownsTransaction = !context.IdentityFenceOwnsTransaction;
        var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : context.Database.CurrentTransaction!;
        var handedOff = false;
        CalendarLease? acquiredLease = null;
        try
        {
            if (!await TransactionCurrentAsync(request, scope, transaction, primary, cancellationToken))
                return new(ModerationPublicEventCalendarOutcome.Unavailable);
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (host is null) return new(ModerationPublicEventCalendarOutcome.NotFound);
            context.ModerationTrustDomainId = host.TrustDomainId;
            // Only discovery happens before human fences. Every source is reread below.
            var discovered = await ControlGraph().Where(row => row.HostBindingId == host.Id
                    && row.TenantId == request.TenantId && row.EventId == request.EventId)
                .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(cancellationToken);
            if (discovered is null) return new(ModerationPublicEventCalendarOutcome.NotFound);
            var provenance = await AcquireProvenanceAsync(request, host, discovered.PublicationId, cancellationToken);
            if (provenance is null) return new(ModerationPublicEventCalendarOutcome.Unavailable);
            var publication = await Definitions().AcquireSelectedAsync(transaction.TransactionId, host.Id,
                provenance.Tenant, discovered.PolicyId, true, cancellationToken);
            if (publication is null || publication.Id != discovered.PublicationId || publication.CatalogueRevision != 2)
                return new(ModerationPublicEventCalendarOutcome.NotFound);
            var policy = ModerationPolicyPublicationProjection.Restore(publication, inventory);
            if (policy is null) return new(ModerationPublicEventCalendarOutcome.Unavailable);
            var acquiredAt = scope.StartedAt;
            var active = true;
            async Task<bool> Current(CancellationToken token) =>
                active && await TransactionCurrentAsync(request, scope, transaction, primary, token)
                && context.ModerationTrustDomainId == host.TrustDomainId
                && await ProvenanceCurrentAsync(provenance, token);
            var session = ModerationSourceReadSession.PublicCalendar(
                new(context, transaction, provenance, policy, true), Current, clock);
            var qualified = await ReadQualifiedAsync(request, session, transaction.TransactionId, acquiredAt, cancellationToken);
            if (qualified is null) return new(ModerationPublicEventCalendarOutcome.NotFound);
            var lease = acquiredLease = new CalendarLease(this, request, scope, transaction, ownsTransaction, priorTrust,
                session, qualified, acquiredAt, () => active = false);
            if (await lease.RevalidateAsync(cancellationToken) != ModerationPublicEventCalendarOutcome.Allowed)
            {
                await lease.DisposeAsync();
                return new(ModerationPublicEventCalendarOutcome.Unavailable);
            }
            scope.Lease = lease;
            handedOff = true;
            return new(ModerationPublicEventCalendarOutcome.Allowed, lease);
        }
        catch (ArgumentException)
        {
            return new(ModerationPublicEventCalendarOutcome.Unavailable);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationPublicEventCalendarOutcome.Unavailable);
        }
        finally
        {
            if (!handedOff)
            {
                if (acquiredLease is not null) await acquiredLease.DisposeAsync();
                else
                {
                    context.ModerationTrustDomainId = priorTrust;
                    if (ownsTransaction) await transaction.DisposeAsync();
                }
            }
        }
    }

    private async Task<bool> TransactionCurrentAsync(
        ModerationPublicEventCalendarRequest request, SerializationScope scope,
        IDbContextTransaction transaction, PrimaryDatabasePublicIdentityOptionsExtension primary, CancellationToken token) =>
        scope.Active && ReferenceEquals(serialization.Value, scope)
        && ReferenceEquals(context.Database.CurrentTransaction, transaction)
        && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && ReferenceEquals(transaction.GetDbTransaction().Connection, context.Database.GetDbConnection())
        && context.Database.GetDbConnection().State == ConnectionState.Open
        && !context.ChangeTracker.Entries().Any()
        && ReferenceEquals(context.GetService<IDbContextOptions>().FindExtension<PrimaryDatabasePublicIdentityOptionsExtension>(), primary)
        && ProviderMatches(primary)
        && await ModerationSourceReadSession.ProviderTenantMatchesAsync(context, request.TenantId, token);

    private bool ProviderMatches(PrimaryDatabasePublicIdentityOptionsExtension primary) =>
        context.Database.ProviderName == (primary.Provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => RelationalNamedLock.PostgreSqlProvider,
            PrimaryDatabaseProvider.Sqlite => RelationalNamedLock.SqliteProvider,
            PrimaryDatabaseProvider.SqlServer => RelationalNamedLock.SqlServerProvider,
            PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => RelationalNamedLock.MySqlProvider,
            _ => null
        });

    private sealed class SerializationScope(DateTimeOffset startedAt)
    {
        internal DateTimeOffset StartedAt { get; } = startedAt;
        internal bool Active { get; set; } = true;
        internal CalendarLease? Lease { get; set; }
    }

    private sealed class CalendarLease(
        ModerationPublicEventCalendarRepository owner, ModerationPublicEventCalendarRequest request,
        SerializationScope scope, IDbContextTransaction transaction, bool ownsTransaction, Guid? priorTrust,
        ModerationSourceReadSession session, QualifiedCalendar original, DateTimeOffset acquiredAt, Action close)
        : IModerationPublicEventCalendarLease
    {
        private bool disposed;
        private readonly CancellationTokenSource expiry = new(
            Remaining(original, acquiredAt, owner.Clock.GetUtcNow()), owner.Clock);
        private bool CanRead => !disposed && scope.Active && !expiry.IsCancellationRequested && owner.Clock.GetUtcNow() < ValidUntil;
        public Event Event => CanRead ? original.Event : throw new ObjectDisposedException(nameof(CalendarLease));
        public EventSession PrimarySession => CanRead ? original.Session : throw new ObjectDisposedException(nameof(CalendarLease));
        public DateTimeOffset ValidUntil => Deadline(original, acquiredAt);
        public CancellationToken ValidityCancellation => expiry.Token;

        private static DateTimeOffset Deadline(QualifiedCalendar value, DateTimeOffset acquired) =>
            value.ValidUntil < acquired.AddSeconds(30) ? value.ValidUntil : acquired.AddSeconds(30);

        private static TimeSpan Remaining(QualifiedCalendar value, DateTimeOffset acquired, DateTimeOffset now) =>
            Deadline(value, acquired) > now ? Deadline(value, acquired) - now : TimeSpan.Zero;

        public async Task<ModerationPublicEventCalendarOutcome> RevalidateAsync(CancellationToken cancellationToken)
        {
            if (disposed || !scope.Active || expiry.IsCancellationRequested || owner.Clock.GetUtcNow() < acquiredAt
                || owner.Clock.GetUtcNow() >= ValidUntil)
                return ModerationPublicEventCalendarOutcome.Unavailable;
            try
            {
                var current = await owner.ReadQualifiedAsync(request, session, transaction.TransactionId,
                    owner.Clock.GetUtcNow(), cancellationToken);
                return !disposed && scope.Active && current is not null
                    && !expiry.IsCancellationRequested
                    && current.Fingerprint == original.Fingerprint && current.ValidUntil == original.ValidUntil
                    && owner.Clock.GetUtcNow() < ValidUntil
                    ? ModerationPublicEventCalendarOutcome.Allowed : ModerationPublicEventCalendarOutcome.Unavailable;
            }
            catch (ArgumentException) { return ModerationPublicEventCalendarOutcome.Unavailable; }
            catch (DbUpdateConcurrencyException) { return ModerationPublicEventCalendarOutcome.Unavailable; }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            close();
            owner.Context.ModerationTrustDomainId = priorTrust;
            try
            {
                if (ownsTransaction) await transaction.DisposeAsync();
            }
            finally { expiry.Dispose(); }
        }
    }
}
