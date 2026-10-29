using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Explore.Persistence.Identity;

/// <summary>Owns configured writer pools, never request, Runtime or Migrator connections.</summary>
internal sealed class LocalIdentityWriterDbContextFactory
{
    private readonly PooledDbContextFactory<ExploreDbContext> applicationPool;
    private readonly PooledDbContextFactory<ExternalIdentityDbContext>? identityPool;

    public LocalIdentityWriterDbContextFactory(PrimaryDatabaseConnectionOptions applicationWriter,
        ExternalIdentityDatabaseDescriptor? identityWriter = null, int poolSize = 128,
        PostgresLocalIdentityWriterEnrollment? applicationEnrollment = null,
        PostgresLocalIdentityWriterEnrollment? identityEnrollment = null,
        IInterceptor[]? interceptors = null)
    {
        ArgumentNullException.ThrowIfNull(applicationWriter);
        if (applicationWriter.Role != PrimaryDatabaseRole.LocalIdentityWriter)
            throw new ArgumentException("Local identity contexts require explicit LocalIdentityWriter database options.", nameof(applicationWriter));
        Topology = identityWriter is null ? IdentityDatabaseTopology.Colocated : IdentityDatabaseTopology.External;
        var application = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(application, applicationWriter);
        if (interceptors is not null) application.AddInterceptors(interceptors);
        if (applicationWriter.Provider == PrimaryDatabaseProvider.PostgreSql)
        {
            RequireEnrollment(applicationEnrollment, applicationWriter.Username);
            application.AddInterceptors(PostgresTenantSessionInterceptor.ForLocalIdentityWriter(
                applicationEnrollment!, Topology, LocalIdentityWriterInventory.Store.Application));
        }
        applicationPool = new(application.Options, poolSize);

        if (identityWriter is not null)
        {
            var identity = new DbContextOptionsBuilder<ExternalIdentityDbContext>();
            IdentityDatabaseProviderComposition.Configure(identity, identityWriter);
            if (interceptors is not null) identity.AddInterceptors(interceptors);
            switch (identityWriter.Provider)
            {
                case PrimaryDatabaseProvider.PostgreSql:
                    RequireEnrollment(identityEnrollment, new NpgsqlConnectionStringBuilder(identityWriter.ConnectionString).Username);
                    identity.AddInterceptors(PostgresTenantSessionInterceptor.ForLocalIdentityWriter(
                        identityEnrollment!, Topology, LocalIdentityWriterInventory.Store.Identity));
                    break;
                case PrimaryDatabaseProvider.Sqlite:
                    identity.AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance);
                    break;
                case PrimaryDatabaseProvider.MySql:
                case PrimaryDatabaseProvider.MariaDb:
                    identity.AddInterceptors(MySqlNamedLockTransactionInterceptor.Instance);
                    break;
            }
            identityPool = new(identity.Options, poolSize);
        }
    }

    internal IdentityDatabaseTopology Topology { get; }

    internal async Task<ExternalIdentityDbContext> CreateExternalIdentityAsync(CancellationToken cancellationToken)
    {
        if (identityPool is null)
            throw new InvalidOperationException("The configured identity topology has no external writer.");
        var identity = await identityPool.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The purpose interceptor attests the complete declared profile on each physical opening.
            if (identity.Database.IsNpgsql())
                await identity.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return identity;
        }
        catch
        {
            await identity.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<Contexts> CreateAsync(Guid? selectedTenant, CancellationToken cancellationToken)
    {
        if (selectedTenant == Guid.Empty)
            throw new ArgumentException("A selected Local identity tenant cannot be empty.", nameof(selectedTenant));
        var application = await applicationPool.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ExternalIdentityDbContext? identity = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            application.ClearTenantFilterBypass();
            application.TenantContext = selectedTenant is { } tenant ? new SelectedTenant(tenant) : null;
            application.CurrentUserService = null;
            application.BindTenantLease();
            if (application.Database.IsNpgsql())
                await application.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (identityPool is not null)
                identity = await CreateExternalIdentityAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(application, identity ?? (DbContext)application);
        }
        catch
        {
            try { if (identity is not null) await identity.DisposeAsync().ConfigureAwait(false); }
            finally { await application.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private static void RequireEnrollment(PostgresLocalIdentityWriterEnrollment? enrollment, string? login)
    {
        if (enrollment is null || string.IsNullOrWhiteSpace(login) || enrollment.WriterLogin != login)
            throw new ArgumentException("PostgreSQL Local identity writers require their exact declared enrollment.", nameof(enrollment));
        enrollment.Validate();
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    internal sealed class Contexts(ExploreDbContext application, DbContext identity) : IAsyncDisposable
    {
        private readonly object lifecycle = new();
        private Task? disposal;
        private bool applicationTransferred;
        private bool identityTransferred;
        internal ExploreDbContext Application { get; } = application;
        internal DbContext Identity { get; } = identity;

        internal void TransferApplicationOwnership(ExploreDbContext captured)
        {
            lock (lifecycle)
            {
                if (disposal is not null || applicationTransferred || !ReferenceEquals(captured, Application))
                    throw new InvalidOperationException("Only successful original application capture transfers its lifetime.");
                applicationTransferred = true;
            }
        }

        internal void TransferIdentityOwnership(ExternalIdentityDbContext captured)
        {
            lock (lifecycle)
            {
                if (disposal is not null || identityTransferred || !ReferenceEquals(captured, Identity)
                    || ReferenceEquals(Identity, Application))
                    throw new InvalidOperationException("Only successful original external identity capture transfers its lifetime.");
                identityTransferred = true;
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (lifecycle) return new(disposal ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            try
            {
                if (!identityTransferred && !ReferenceEquals(Identity, Application))
                    await Identity.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (!applicationTransferred) await Application.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
