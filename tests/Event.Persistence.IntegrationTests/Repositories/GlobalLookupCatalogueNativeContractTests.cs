using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Caching;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class GlobalLookupCatalogueNativeContractTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Type[] LookupTypes =
    [
        typeof(EventType), typeof(EventFormat), typeof(EventStatus), typeof(AudienceAge),
        typeof(AudienceGender), typeof(Madhab), typeof(Language), typeof(VisibilityType),
        typeof(ApprovalStatus), typeof(RegistrationMode), typeof(Role), typeof(OrganizationPosition),
        typeof(ActorType), typeof(DidCustodyType), typeof(FileType), typeof(TagType), typeof(OwnerType)
    ];

    [Test]
    public async Task ActualRuntimeCacheLoadsAllSeventeenRealLookupsAndRefreshesWithoutTenantOrHuman()
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        writer.ClearTenantFilterBypass();
        var names = new HumanPrincipalSourceSql(writer);
        int id = 1_000_000;
        foreach (Type type in LookupTypes)
        {
            int maximum = await writer.Database.SqlQueryRaw<int>(
                $"SELECT COALESCE(MAX({names.Column(type, "Id")}),0) AS \"Value\" FROM {names.Table(type)}").SingleAsync();
            id = Math.Max(id, checked(maximum + 1));
        }
        var inserted = LookupTypes.Select(type =>
        {
            object row = Activator.CreateInstance(type)!;
            type.GetProperty("Id")!.SetValue(row, id);
            type.GetProperty("MasterCode")!.SetValue(row, "CATALOGUE_" + id);
            type.GetProperty("FullName")!.SetValue(row, "Actual database reference");
            type.GetProperty("Description")!.SetValue(row, "Persisted reference metadata");
            if (row is AudienceAge age) { age.MinAge = 7; age.MaxAge = 13; }
            if (row is Role role) { role.RoleScopeId = (int)RoleScopeEnum.Platform; role.IsSystem = false; }
            return row;
        }).ToArray();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(), FullName = "Catalogue isolation",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        var privateType = new EventType
        {
            Id = checked(id + 1), MasterCode = "PRIVATE_" + id, FullName = "Tenant-only reference",
            TenantId = tenant.Id, Tenant = tenant
        };
        writer.AddRange(inserted);
        writer.AddRange(tenant, privateType);
        await writer.SaveChangesAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(fixture.NativeQueueEnrollment);
            services.AddScoped(_ => Open(PrimaryDatabaseRole.Runtime));
            await using var provider = services.BuildServiceProvider();
            var cache = new LookupDataCache(provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<LookupDataCache>.Instance);
            await cache.InitializeAsync();
            await CompareAsync<EventType>();
            await CompareAsync<EventFormat>();
            await CompareAsync<EventStatus>();
            await CompareAsync<AudienceAge>();
            await CompareAsync<AudienceGender>();
            await CompareAsync<Madhab>();
            await CompareAsync<Language>();
            await CompareAsync<VisibilityType>();
            await CompareAsync<ApprovalStatus>();
            await CompareAsync<RegistrationMode>();
            await CompareAsync<Role>();
            await CompareAsync<OrganizationPosition>();
            await CompareAsync<ActorType>();
            await CompareAsync<DidCustodyType>();
            await CompareAsync<FileType>();
            await CompareAsync<TagType>();
            await CompareAsync<OwnerType>();
            await Assert.That(cache.GetById<EventType>(privateType.Id)).IsNull();
            await Assert.That(cache.GetById<AudienceAge>(id)!.MinAge).IsEqualTo(7);
            await Assert.That(cache.GetById<AudienceAge>(id)!.MaxAge).IsEqualTo(13);
            await Assert.That(cache.GetById<Role>(id)!.Scope).IsEqualTo(RoleScopeEnum.Platform);
            await Assert.That(cache.GetById<Role>(id)!.IsSystem).IsFalse();
            var owner = inserted.OfType<OwnerType>().Single();
            owner.FullName = "Changed persisted reference";
            owner.Description = null;
            await writer.SaveChangesAsync();
            await cache.RefreshAsync();
            await Assert.That(cache.GetById<OwnerType>(id)!.FullName).IsEqualTo(owner.FullName);
            await Assert.That(cache.GetById<OwnerType>(id)!.Description).IsNull();

            async Task CompareAsync<T>() where T : class
            {
                var expected = await writer.Set<T>().AsNoTracking().ToListAsync();
                var actual = cache.Get<T>();
                await Assert.That(actual.Count).IsEqualTo(expected.Count);
                await Assert.That(cache.GetById<T>(id)).IsNotNull();
                foreach (var row in expected)
                {
                    int key = (int)typeof(T).GetProperty("Id")!.GetValue(row)!;
                    foreach (var property in writer.Model.FindEntityType(typeof(T))!.GetProperties())
                        await Assert.That(property.PropertyInfo!.GetValue(actual[key]))
                            .IsEqualTo(property.PropertyInfo.GetValue(row));
                }
            }
        }
        finally
        {
            writer.RemoveRange(inserted);
            writer.Remove(privateType);
            await writer.SaveChangesAsync();
        }
    }

    [Test]
    public async Task RuntimeHasNoSourceOrOwnerPromotionAuthority()
    {
        await using var reader = Open(PrimaryDatabaseRole.Runtime);
        await Assert.That((await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
            reader, fixture.NativeQueueEnrollment, default)).Count).IsGreaterThan(0);
        var names = new HumanPrincipalSourceSql(reader);
        foreach (Type type in LookupTypes.Concat([typeof(RolePermission), typeof(UserPii)]))
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                reader.Database.ExecuteSqlRawAsync($"SELECT * FROM {names.Table(type)} LIMIT 1"));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
        foreach (string role in new[]
        {
            PostgresGlobalLookupCatalogueContract.OwnerRole,
            PostgresGlobalLookupCatalogueContract.MigratorRole,
            PostgresGlobalLookupCatalogueContract.RuntimeRole
        })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                reader.Database.ExecuteSqlRawAsync($"SET ROLE {role}"));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    public async Task UnknownAndNonglobalSelectorsCannotReachAnySource()
    {
        await using var reader = Open(PrimaryDatabaseRole.Runtime);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GlobalLookupCatalogueProviderOperations.ReadAsync<RolePermission>(
                reader, fixture.NativeQueueEnrollment, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GlobalLookupCatalogueProviderOperations.ReadAsync<Tenant>(
                reader, fixture.NativeQueueEnrollment, default));
        await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            reader.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresGlobalLookupCatalogueContract.FunctionSql(reader)}(2147483647)"));
        await Assert.That(denied!.SqlState).IsEqualTo("22023");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task OtherPhysicalLoginsCannotBorrowTheCatalogue(PrimaryDatabaseRole role)
    {
        await using var reader = Open(role);
        await Assert.ThrowsAsync<PostgresException>(() =>
            GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(reader, fixture.NativeQueueEnrollment, default));
    }

    [Test]
    public async Task SerializableOwnerIsRetainedAndNonserializableOwnerIsRejected()
    {
        await using var reader = Open(PrimaryDatabaseRole.Runtime);
        await using (var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var connection = reader.Database.GetDbConnection();
            await Assert.That((await GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(
                reader, fixture.NativeQueueEnrollment, default)).Count).IsGreaterThan(0);
            await Assert.That(reader.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(reader.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(reader.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await transaction.RollbackAsync();
        }
        await using var weaker = await reader.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            GlobalLookupCatalogueProviderOperations.ReadAsync<Role>(reader, fixture.NativeQueueEnrollment, default));
        await weaker.RollbackAsync();
    }

    [Test]
    public async Task ForgedTenantSessionCannotTurnGlobalCatalogueIntoTenantLookup()
    {
        await using var reader = Open(PrimaryDatabaseRole.Runtime);
        await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await reader.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id',{0},true)", Guid.CreateVersion7().ToString());
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            GlobalLookupCatalogueProviderOperations.ReadAsync<EventType>(reader, fixture.NativeQueueEnrollment, default));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options);
    }
}
