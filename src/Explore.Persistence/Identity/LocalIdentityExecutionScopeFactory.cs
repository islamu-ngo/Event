using System.Runtime.ExceptionServices;
using System.Transactions;
using Explore.Application.Contracts.Identity;
using Explore.Application.Exceptions;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Identity;

/// <summary>Selects both original writer contexts before resolving the Local workflow graph.</summary>
internal sealed class LocalIdentityExecutionScopeFactory : ILocalIdentityExecutionScopeFactory
{
    private readonly IServiceProvider original;
    private readonly IServiceScopeFactory scopes;
    private readonly LocalIdentityWriterDbContextFactory contexts;

    public LocalIdentityExecutionScopeFactory(IServiceProvider original, IServiceScopeFactory scopes,
        LocalIdentityWriterDbContextFactory contexts)
    {
        this.original = original;
        this.scopes = scopes;
        this.contexts = contexts;
    }

    public async ValueTask<ILocalIdentityExecutionScope> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = original.GetRequiredService<LocalIdentityExecutionLease>();
        if (existing.Application is { } current)
        {
            RequireCurrent(original, current, existing.Identity
                ?? throw new UnauthorizedAccessException("The original selected identity is missing."));
            return new BorrowedScope(original);
        }
        if (existing.Identity is not null || Transaction.Current is not null)
            throw new InvalidOperationException("Local identity selection must precede the owning transaction.");
        var parent = original.GetRequiredService<ExploreDbContext>();
        parent.RequireBoundTenantLease();
        if (parent.IsTenantFilterBypassed || parent.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Local identity selection requires an idle original application owner.");
        if (original.GetRequiredService<ExploreDbContextExecutionLease>().Context is not null)
            original.GetRequiredService<ExploreDbContextExecutionLease>().RequireOriginalOwner(parent);
        if (HumanSecuritySourceGate.TryRequireCurrent(parent))
            throw new InvalidOperationException("Fresh Local selection cannot borrow another connector's retained Human custody.");
        if (EventDiscoveryEnrollmentGate.TryRequireCurrent(parent))
            throw new InvalidOperationException("Fresh Local selection cannot borrow another connector's retained Discovery custody.");

        var child = scopes.CreateAsyncScope();
        LocalIdentityWriterDbContextFactory.Contexts? pair = null;
        HumanSecuritySourceGate? human = null;
        EventDiscoveryEnrollmentGate? discovery = null;
        LocalIdentityLifecycleGateCustody? lifecycle = null;
        try
        {
            pair = await contexts.CreateAsync(parent.TenantFilterTenantId, cancellationToken).ConfigureAwait(false);
            pair.Application.CurrentUserService = parent.CurrentUserService;
            child.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>().Bind(pair.Application, pair.Identity);
            child.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>()
                .Bind(pair.Application, pair.Application.TenantFilterTenantId);
            human = new(pair.Application);
            _ = await human.AcquireAsync(cancellationToken).ConfigureAwait(false);
            discovery = new(pair.Application);
            _ = await discovery.AcquireAsync(cancellationToken).ConfigureAwait(false);
            lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(pair.Identity, cancellationToken).ConfigureAwait(false);
            RequireCurrent(child.ServiceProvider, pair.Application, pair.Identity);

            var application = child.ServiceProvider.GetRequiredService<ExploreDbContext>();
            if (!ReferenceEquals(application, pair.Application))
                throw new InvalidOperationException("The child replaced its selected Local application context.");
            pair.TransferApplicationOwnership(application);
            if (pair.Identity is ExternalIdentityDbContext external)
            {
                var captured = child.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>();
                if (!ReferenceEquals(captured, external))
                    throw new InvalidOperationException("The child replaced its selected Local identity context.");
                pair.TransferIdentityOwnership(captured);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new OwnedScope(child, pair, human, discovery, lifecycle);
        }
        catch (Exception failure)
        {
            await CleanupAsync(child, pair, human, discovery, lifecycle, failure).ConfigureAwait(false);
            throw;
        }
    }

    private static void RequireCurrent(IServiceProvider services, ExploreDbContext application, DbContext identity)
    {
        services.GetRequiredService<LocalIdentityExecutionLease>().RequireCurrent(application, identity);
        services.GetRequiredService<ExploreDbContextExecutionLease>().RequireOriginalOwner(application);
        HumanSecuritySourceGate.RequireCurrent(application);
        EventDiscoveryEnrollmentGate.RequireCurrent(application);
        LocalIdentityLifecycleGateCustody.RequireHeld(identity);
    }

    private static async Task CleanupAsync(AsyncServiceScope child, LocalIdentityWriterDbContextFactory.Contexts? pair,
        HumanSecuritySourceGate? human, EventDiscoveryEnrollmentGate? discovery,
        LocalIdentityLifecycleGateCustody? lifecycle, Exception? primary)
    {
        List<Exception> failures = [];
        if (primary is not null) failures.Add(primary);
        if (pair is not null)
        {
            try
            {
                child.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>()
                    .RequireCurrent(pair.Application, pair.Identity);
                if (pair.Identity.Database.CurrentTransaction is { } identityTransaction)
                    await identityTransaction.DisposeAsync().ConfigureAwait(false);
                if (!ReferenceEquals(pair.Application, pair.Identity)
                    && pair.Application.Database.CurrentTransaction is { } applicationTransaction)
                    await applicationTransaction.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        foreach (IAsyncDisposable? resource in new IAsyncDisposable?[] { lifecycle, discovery, human, child, pair })
        {
            if (resource is null) continue;
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
        {
            var aggregate = new AggregateException("Local owner acquisition and cleanup failures.", failures);
            if (primary is HumanMutationOutcomeUnknownException)
                throw new HumanMutationOutcomeUnknownException(aggregate);
            throw aggregate;
        }
    }

    private sealed class OwnedScope(AsyncServiceScope child, LocalIdentityWriterDbContextFactory.Contexts pair,
        HumanSecuritySourceGate human, EventDiscoveryEnrollmentGate discovery,
        LocalIdentityLifecycleGateCustody lifecycle) : ILocalIdentityExecutionScope
    {
        private readonly object lifetime = new();
        private Task? disposal;

        public IServiceProvider Services
        {
            get
            {
                lock (lifetime)
                {
                    ObjectDisposedException.ThrowIf(disposal is not null, this);
                    RequireCurrent(child.ServiceProvider, pair.Application, pair.Identity);
                    return child.ServiceProvider;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (lifetime) return new(disposal ??= CleanupAsync(child, pair, human, discovery, lifecycle, null));
        }
    }

    private sealed class BorrowedScope(IServiceProvider services) : ILocalIdentityExecutionScope
    {
        public IServiceProvider Services => services;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
