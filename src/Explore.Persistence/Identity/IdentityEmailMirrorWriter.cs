using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

/// <summary>
/// Retains an existing account and its email-only profile snapshot in the original
/// transaction. Private birth-date values are neither selected nor written.
/// </summary>
public sealed class IdentityEmailMirrorWriter(ExploreDbContext context, IUserRepository users)
    : IIdentityEmailMirrorWriter
{
    public async Task<IPreparedIdentityEmailMirror> PrepareAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("An existing account is required.", nameof(userId));
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = new Prepared(context, users);
        try
        {
            await prepared.InitializeAsync(userId, cancellationToken);
            return prepared;
        }
        catch
        {
            await prepared.DisposeAsync();
            throw;
        }
    }

    private sealed class Prepared : IPreparedIdentityEmailMirror
    {
        private readonly ExploreDbContext context;
        private readonly IUserRepository users;
        private readonly DbContextId contextId;
        private readonly IDbContextTransaction transaction;
        private readonly DbTransaction physicalTransaction;
        private readonly RelationalConnectionIdentity connection;
        private readonly ChangeTracker tracker;
        private readonly ITenantContext? tenant;
        private readonly Guid? tenantId;
        private EntityEntry<User>? account;
        private EntityEntry<UserPii>? profile;
        private bool attachedAccount;
        private UserPii? originalProfile;
        private bool originalProfileLoaded;
        private PropertyValues? accountSnapshot;
        private PropertyValues? profileSnapshot;
        private int expired;
        private int disposed;
        private int applyStarted;

        internal Prepared(ExploreDbContext context, IUserRepository users)
        {
            context.RequireBoundTenantLease();
            if (!context.Database.IsRelational() || context.IsTenantFilterBypassed
                || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("An email mirror requires its native tenant transaction.");
            this.context = context;
            this.users = users;
            contextId = context.ContextId;
            tenant = context.TenantContext;
            tenantId = context.TenantFilterTenantId;
            transaction = context.Database.CurrentTransaction
                ?? throw new InvalidOperationException("An email mirror requires its original account transaction.");
            physicalTransaction = transaction.GetDbTransaction();
            connection = RelationalConnectionIdentity.Capture(context);
            tracker = context.ChangeTracker;
            RequireCurrent();
            connection.StateChange += OnConnectionStateChange;
            tracker.StateChanged += OnEntityStateChanged;
        }

        internal async Task InitializeAsync(Guid userId, CancellationToken cancellationToken)
        {
            RequireCurrent();
            // Correlation entities are detached. Observe the database again before accepting
            // an already tracked account, without replacing any of its original values.
            User observed = await context.Users.IgnoreAutoIncludes().AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken)
                ?? throw new InvalidOperationException("The existing identity account is unavailable.");
            RequireCurrent();
            var observedProfile = await context.UserPii.AsNoTracking()
                .Where(pii => pii.UserId == userId)
                .Select(pii => new
                {
                    pii.UserId,
                    pii.Email,
                    pii.FirstName,
                    pii.LastName,
                    Revision = EF.Property<long>(pii, "_birthDateRevision")
                })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("The existing identity profile is unavailable.");
            cancellationToken.ThrowIfCancellationRequested();
            RequireCurrent();

            context.ChangeTracker.DetectChanges();
            if (context.ChangeTracker.Entries<UserPii>().Any(entry => entry.Entity.UserId == userId))
                throw new InvalidOperationException("An identity profile already belongs to another tracked owner.");

            EntityEntry<User>? existing = context.ChangeTracker.Entries<User>()
                .SingleOrDefault(entry => entry.Entity.Id == userId);
            PropertyValues observedValues = context.Entry(observed).CurrentValues;
            if (existing is not null)
            {
                if (existing.State != EntityState.Unchanged || existing.Entity.Pii is not null
                    || !MatchesSnapshot(existing, observedValues))
                    throw new InvalidOperationException("The tracked identity account differs from its current database owner.");
                account = existing;
                originalProfile = existing.Entity.Pii;
                originalProfileLoaded = existing.Reference(user => user.Pii).IsLoaded;
            }
            else
            {
                account = context.Entry(observed);
                attachedAccount = true;
                account.State = EntityState.Unchanged;
            }

            var pii = new UserPii
            {
                UserId = observedProfile.UserId,
                Email = observedProfile.Email,
                FirstName = observedProfile.FirstName,
                LastName = observedProfile.LastName
            };
            profile = context.Entry(pii);
            // A constructed projection is not tracked. Seed the mapped backing field
            // before taking its unchanged snapshot, then explicitly retain both values.
            var revision = profile.Property<long>("_birthDateRevision");
            revision.CurrentValue = observedProfile.Revision;
            profile.State = EntityState.Unchanged;
            revision.OriginalValue = observedProfile.Revision;
            account.Entity.Pii = pii;
            pii.User = account.Entity;
            context.ChangeTracker.DetectChanges();
            accountSnapshot = account.OriginalValues.Clone();
            profileSnapshot = profile.OriginalValues.Clone();
            RequireCurrent();
            RequireOriginalEntries();
        }

        public async Task ApplyAsync(
            string? replacementEmail, bool? emailVerified, CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref applyStarted, 1, 0) != 0)
                throw new InvalidOperationException("A prepared identity email mirror can be applied only once.");
            cancellationToken.ThrowIfCancellationRequested();
            RequireCurrent();
            context.ChangeTracker.DetectChanges();
            RequireOriginalEntries();

            User user = account!.Entity;
            if (replacementEmail is not null)
                user.Email = replacementEmail;
            if (emailVerified.HasValue)
                user.EmailVerified = emailVerified;

            await users.SaveIdentityProfileAsync(user, cancellationToken);
            RequireCurrent();
        }

        private void RequireCurrent()
        {
            if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref expired) != 0)
                throw new InvalidOperationException("The prepared identity email mirror has expired.");
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || context.IsTenantFilterBypassed
                || !ReferenceEquals(context.TenantContext, tenant) || context.TenantFilterTenantId != tenantId
                || System.Transactions.Transaction.Current is not null
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(transaction.GetDbTransaction(), physicalTransaction)
                || connection.State != ConnectionState.Open || !connection.Matches(context)
                || !connection.Owns(physicalTransaction))
                throw new InvalidOperationException("The prepared identity email mirror left its original persistence owner.");
        }

        private void RequireOriginalEntries()
        {
            if (account is null || profile is null || accountSnapshot is null || profileSnapshot is null
                || account.State != EntityState.Unchanged || profile.State != EntityState.Unchanged
                || account.Entity.IsDeleted || account.Entity.Id != profile.Entity.UserId
                || !ReferenceEquals(account.Entity.Pii, profile.Entity)
                || !ReferenceEquals(profile.Entity.User, account.Entity)
                || !MatchesSnapshot(account, accountSnapshot)
                || !MatchesSnapshot(profile, profileSnapshot))
                throw new InvalidOperationException("The prepared identity email mirror lost its original tracked snapshots.");
        }

        private static bool MatchesSnapshot(EntityEntry entry, PropertyValues snapshot) =>
            entry.Properties.All(property =>
                Equals(property.CurrentValue, snapshot[property.Metadata.Name])
                && Equals(property.OriginalValue, snapshot[property.Metadata.Name]));

        private void OnConnectionStateChange(object? sender, StateChangeEventArgs args)
        {
            if (args.CurrentState != ConnectionState.Open)
                Interlocked.Exchange(ref expired, 1);
        }

        private void OnEntityStateChanged(object? sender, EntityStateChangedEventArgs args)
        {
            if ((ReferenceEquals(args.Entry.Entity, account?.Entity)
                    || ReferenceEquals(args.Entry.Entity, profile?.Entity))
                && args.NewState is EntityState.Detached or EntityState.Added or EntityState.Deleted)
                Interlocked.Exchange(ref expired, 1);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return ValueTask.CompletedTask;

            Interlocked.Exchange(ref expired, 1);
            connection.StateChange -= OnConnectionStateChange;
            tracker.StateChanged -= OnEntityStateChanged;

            // Rollback or transaction disposal does not end ownership of these entries.
            // A pooled context's new lease does: never clean up through that lease.
            if (context.ContextId != contextId)
                return ValueTask.CompletedTask;

            if (profile is not null)
            {
                UserPii ownedProfile = profile.Entity;
                if (ownedProfile.User is not null
                    && !ReferenceEquals(ownedProfile.User, account?.Entity))
                    throw new InvalidOperationException("The prepared identity profile belongs to a different account.");

                bool hadOwnedReference = account is not null
                    && ReferenceEquals(account.Entity.Pii, ownedProfile);
                if (profile.State != EntityState.Detached)
                    profile.State = EntityState.Detached;

                if (account is not null)
                {
                    if (ReferenceEquals(account.Entity.Pii, ownedProfile))
                        account.Entity.Pii = originalProfile!;
                    if (ReferenceEquals(ownedProfile.User, account.Entity))
                        ownedProfile.User = null;
                    if (!attachedAccount && hadOwnedReference
                        && account.State != EntityState.Detached
                        && ReferenceEquals(account.Entity.Pii, originalProfile))
                        account.Reference(user => user.Pii).IsLoaded = originalProfileLoaded;
                }
            }

            if (attachedAccount && account is not null && account.State != EntityState.Detached)
                account.State = EntityState.Detached;

            return ValueTask.CompletedTask;
        }
    }
}
