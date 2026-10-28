using Explore.Domain;
using Explore.Persistence.Configurations.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationAuthoritySourcePersistenceTests
{
    [Test]
    public async Task Another_binding_cannot_reparent_the_same_primary_store()
    {
        await using var database = await SourceDatabase.Create();
        database.Context.Add(ModerationHostBinding.Establish(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), database.Binding.MaintenanceServicePrincipalId));

        await Assert.That(() => database.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task Tenant_qualification_has_one_native_revision_owner()
    {
        await using var database = await SourceDatabase.Create();
        database.Context.Add(ModerationQualificationEpoch.Initialize(
            Guid.CreateVersion7(), database.Binding, database.Tenant));

        await Assert.That(() => database.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task Qualification_cannot_point_at_a_missing_tenant()
    {
        await using var database = await SourceDatabase.Create();
        var missing = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Missing jurisdiction",
            Slug = "missing-jurisdiction",
            TenantStatusId = 1,
            TenantStatus = new TenantStatus { Id = 1, MasterCode = "Active", FullName = "Active" }
        };
        database.Context.Add(ModerationQualificationEpoch.Initialize(
            Guid.CreateVersion7(), database.Binding, missing));

        await Assert.That(() => database.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task Stale_writer_cannot_overwrite_the_committed_qualification_generation()
    {
        await using var database = await SourceDatabase.Create();
        await using var staleContext = new SourceContext(database.Options);
        var stale = await staleContext.Set<ModerationQualificationEpoch>().SingleAsync();
        var current = await database.Context.Set<ModerationQualificationEpoch>().SingleAsync();
        current.Advance(1);
        await database.Context.SaveChangesAsync();
        stale.Advance(1);

        await Assert.That(() => staleContext.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        database.Context.ChangeTracker.Clear();
        await Assert.That((await database.Context.Set<ModerationQualificationEpoch>().SingleAsync()).Revision)
            .IsEqualTo(2L);
    }

    [Test]
    public async Task Direct_write_cannot_replace_a_positive_generation_with_zero()
    {
        await using var database = await SourceDatabase.Create();
        await Assert.That(() => database.Context.Database.ExecuteSqlRawAsync(
            "UPDATE moderation_qualification_epochs SET revision = 0 WHERE id = {0}", database.Epoch.Id))
            .Throws<SqliteException>();
        database.Context.ChangeTracker.Clear();
        await Assert.That((await database.Context.Set<ModerationQualificationEpoch>().SingleAsync()).Revision)
            .IsEqualTo(1L);
    }

    private sealed class SourceContext(DbContextOptions<SourceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Ignore<Actor>();
            builder.Ignore<TenantStatus>();
            builder.Ignore<TenantNavigationLink>();
            var sources = new ModerationAuthoritySourceConfiguration();
            builder.ApplyConfiguration<ModerationHostBinding>(sources);
            builder.ApplyConfiguration<ModerationQualificationEpoch>(sources);
            builder.Entity<Tenant>().ToTable("tenants");
            builder.Entity<Tenant>().Ignore(entity => entity.TenantStatus);
            builder.Entity<Tenant>().Ignore(entity => entity.NavigationLinks);
            builder.Entity<ServicePrincipal>().ToTable("service_principals");
            builder.Entity<ServicePrincipal>().Ignore(entity => entity.Actor);
        }
    }

    private sealed class SourceDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SourceDatabase(SqliteConnection connection)
        {
            _connection = connection;
            Options = new DbContextOptionsBuilder<SourceContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            Context = new SourceContext(Options);
            Tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Actual jurisdiction",
                Slug = "actual-jurisdiction",
                TenantStatusId = 1,
                TenantStatus = new TenantStatus { Id = 1, MasterCode = "Active", FullName = "Active" }
            };
            var service = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(),
                Code = "selected-moderation-maintenance",
                DisplayName = "Selected maintenance owner"
            };
            Binding = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), service.Id);
            Epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Binding, Tenant);
            Context.AddRange(service, Tenant, Binding, Epoch);
        }

        public DbContextOptions<SourceContext> Options { get; }
        public SourceContext Context { get; }
        public Tenant Tenant { get; }
        public ModerationHostBinding Binding { get; }
        public ModerationQualificationEpoch Epoch { get; }

        public static async Task<SourceDatabase> Create()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:"
            }.ToString());
            await connection.OpenAsync();
            var database = new SourceDatabase(connection);
            await database.Context.Database.EnsureCreatedAsync();
            await database.Context.SaveChangesAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
