using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresCategoryMutationPermissionsTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CanonicalRuntimeMutatesOwnTenantCategoriesAndLinksWhileRlsAndImmutableKeyDenialsHold()
    {
        var (tenantA, tenantB, typeId, foreignCategoryId, foreignLinkId) = await SeedAsync();

        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenantA);
        await runtime.Database.OpenConnectionAsync();
        await PostgresCategoryMutationPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);

        var categoryRepo = new CategoryRepository(runtime);
        var linkRepo = new CategoryTypeCategoriesRepository(runtime);

        var createdCategory = await categoryRepo.Create(new Category
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            MasterCode = $"MUT_{Guid.CreateVersion7():N}",
            FullName = "Owned category",
            TenantId = tenantA,
            Tenant = null!
        });
        createdCategory.FullName = "Updated owned category";
        createdCategory.ConcurrencyStamp = Guid.CreateVersion7();
        await categoryRepo.Update(createdCategory);
        var trackedResult = await categoryRepo.GetById(createdCategory.Id);
        await Assert.That(trackedResult!.FullName).IsEqualTo("Updated owned category");
        await Assert.That(trackedResult.ConcurrencyStamp).IsEqualTo(createdCategory.ConcurrencyStamp);

        var createdLink = await linkRepo.Create(new CategoryTypeCategories
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantA,
            Tenant = null!,
            CategoryId = createdCategory.Id,
            Category = null!,
            CategoryTypeId = typeId,
            CategoryType = null!
        });
        var replacementCategory = await categoryRepo.Create(new Category
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            MasterCode = $"MUT_{Guid.CreateVersion7():N}",
            FullName = "Replacement category",
            TenantId = tenantA,
            Tenant = null!
        });
        var detachedUpdate = await categoryRepo.GetById(createdCategory.Id);
        detachedUpdate!.FullName = "Updated from detached category";
        detachedUpdate.MasterCode = $"DET_{Guid.CreateVersion7():N}";
        detachedUpdate.ParentId = replacementCategory.Id;
        await categoryRepo.Update(detachedUpdate);
        var detachedResult = await categoryRepo.GetById(createdCategory.Id);
        await Assert.That(detachedResult!.FullName).IsEqualTo(detachedUpdate.FullName);
        await Assert.That(detachedResult.MasterCode).IsEqualTo(detachedUpdate.MasterCode);
        await Assert.That(detachedResult.ParentId).IsEqualTo(replacementCategory.Id);
        await Assert.That(detachedResult.ConcurrencyStamp).IsEqualTo(createdCategory.ConcurrencyStamp);
        await Assert.That(detachedResult.ConcurrencyStamp).IsNotEqualTo(detachedUpdate.ConcurrencyStamp);
        createdLink.CategoryId = replacementCategory.Id;
        runtime.Entry(createdLink).Property(link => link.TenantId).IsModified = true;
        await linkRepo.Update(createdLink);
        var persistedLink = await runtime.CategoryTypeCategories.AsNoTracking()
            .SingleAsync(link => link.Id == createdLink.Id);
        await Assert.That(persistedLink.CategoryId).IsEqualTo(replacementCategory.Id);
        await Assert.That(persistedLink.TenantId).IsEqualTo(tenantA);

        // Cross-tenant reads remain isolated by PostgreSQL RLS.
        await Assert.That(await categoryRepo.GetById(foreignCategoryId)).IsNull();
        await Assert.That(await linkRepo.GetById(foreignLinkId)).IsNull();

        var foreignTrackedCategory = new Category
        {
            Id = foreignCategoryId,
            TenantId = tenantB,
            ConcurrencyStamp = Guid.CreateVersion7(),
            MasterCode = "FOREIGN_TRACKED",
            FullName = "Foreign tracked category",
            Tenant = null!
        };
        runtime.Attach(foreignTrackedCategory);
        var forgedCategory = new Category
        {
            Id = foreignCategoryId,
            TenantId = tenantA,
            ConcurrencyStamp = Guid.CreateVersion7(),
            MasterCode = "FORGED_CATEGORY",
            FullName = "Forged category",
            Tenant = null!
        };
        var categoryRefusal = await Assert.ThrowsAsync<InvalidOperationException>(() => categoryRepo.Update(forgedCategory));
        await Assert.That(categoryRefusal!.Message).IsEqualTo("tracked_tenant_authority_mismatch");
        runtime.Entry(foreignTrackedCategory).State = EntityState.Detached;

        var foreignTracked = new CategoryTypeCategories
        {
            Id = foreignLinkId,
            TenantId = tenantB,
            CategoryId = foreignCategoryId,
            CategoryTypeId = typeId,
            Tenant = null!,
            Category = null!,
            CategoryType = null!
        };
        runtime.Attach(foreignTracked);
        var forged = new CategoryTypeCategories
        {
            Id = foreignLinkId,
            TenantId = tenantA,
            CategoryId = replacementCategory.Id,
            CategoryTypeId = typeId,
            Tenant = null!,
            Category = null!,
            CategoryType = null!
        };
        var linkRefusal = await Assert.ThrowsAsync<InvalidOperationException>(() => linkRepo.Update(forged));
        await Assert.That(linkRefusal!.Message).IsEqualTo("tracked_tenant_authority_mismatch");
        runtime.Entry(foreignTracked).State = EntityState.Detached;
        await using var foreignRuntime = Open(PrimaryDatabaseRole.Runtime, tenantB);
        var foreignLink = await foreignRuntime.CategoryTypeCategories.AsNoTracking().SingleAsync(link => link.Id == foreignLinkId);
        await Assert.That(foreignLink.TenantId).IsEqualTo(tenantB);
        await Assert.That(foreignLink.CategoryId).IsEqualTo(foreignCategoryId);
        await Assert.That(foreignLink.CategoryTypeId).IsEqualTo(typeId);

        // Cross-tenant INSERT fails closed via RLS WITH CHECK (SQLSTATE 42501).
        var names = new HumanPrincipalSourceSql(runtime);
        string categoryTable = names.Table(typeof(Category));
        string linkTable = names.Table(typeof(CategoryTypeCategories));
        var crossTenantCategoryInsert = await Assert.ThrowsAsync<PostgresException>(() =>
            runtime.Database.ExecuteSqlRawAsync($"""
                INSERT INTO {categoryTable} ({names.Column(typeof(Category), nameof(Category.Id))},
                    {names.Column(typeof(Category), nameof(Category.ConcurrencyStamp))},
                    {names.Column(typeof(Category), nameof(Category.MasterCode))},
                    {names.Column(typeof(Category), nameof(Category.FullName))},
                    {names.Column(typeof(Category), nameof(Category.TenantId))})
                VALUES ('{Guid.CreateVersion7():D}', '{Guid.CreateVersion7():D}', 'FORGED_{Guid.CreateVersion7():N}', 'Forged', '{tenantB:D}')
                """));
        await Assert.That(crossTenantCategoryInsert!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

        var crossTenantLinkInsert = await Assert.ThrowsAsync<PostgresException>(() =>
            runtime.Database.ExecuteSqlRawAsync($"""
                INSERT INTO {linkTable} ({names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.Id))},
                    {names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.CategoryId))},
                    {names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.CategoryTypeId))},
                    {names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.TenantId))})
                VALUES ('{Guid.CreateVersion7():D}', '{foreignCategoryId:D}', {typeId}, '{tenantB:D}')
                """));
        await Assert.That(crossTenantLinkInsert!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

        // Immutable key columns (id, tenant_id) remain non-updatable under Runtime.
        foreach (string sql in new[]
        {
            $"UPDATE {categoryTable} SET {names.Column(typeof(Category), nameof(Category.Id))}={names.Column(typeof(Category), nameof(Category.Id))} WHERE false",
            $"UPDATE {categoryTable} SET {names.Column(typeof(Category), nameof(Category.TenantId))}={names.Column(typeof(Category), nameof(Category.TenantId))} WHERE false",
            $"UPDATE {linkTable} SET {names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.Id))}={names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.Id))} WHERE false",
            $"UPDATE {linkTable} SET {names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.TenantId))}={names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.TenantId))} WHERE false"
        })
        {
            var immutableDenied = await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.ExecuteSqlRawAsync(sql));
            await Assert.That(immutableDenied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }

        await linkRepo.Delete(createdLink);
        await categoryRepo.Delete(createdCategory);
        await categoryRepo.Delete(replacementCategory);

        // Global login remains completely denied on both tables.
        await using var global = Open(PrimaryDatabaseRole.Global, tenantA);
        await global.Database.OpenConnectionAsync();
        foreach (string table in new[] { categoryTable, linkTable })
        {
            var globalDenied = await Assert.ThrowsAsync<PostgresException>(() =>
                global.Database.ExecuteSqlRawAsync($"SELECT 1 FROM {table} LIMIT 1"));
            await Assert.That(globalDenied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    public async Task StaleDetachedCategoryCannotOverwriteNewerTrackedVersion()
    {
        var (tenantA, _, _, _, _) = await SeedAsync();
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenantA);
        await runtime.Database.OpenConnectionAsync();
        await PostgresCategoryMutationPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
        var repository = new CategoryRepository(runtime);
        var tracked = await repository.Create(new Category
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            MasterCode = $"STALE_{Guid.CreateVersion7():N}",
            FullName = "Original",
            TenantId = tenantA,
            Tenant = null!
        });
        var stale = await repository.GetById(tracked.Id);
        tracked.FullName = "Current";
        await repository.Update(tracked);
        stale!.FullName = "Stale overwrite";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.Update(stale));
        var persisted = await repository.GetById(tracked.Id);
        await Assert.That(persisted!.FullName).IsEqualTo("Current");
    }

    [Test]
    public async Task CanonicalRuntimeUpdatesDetachedCategoryReadModelWithoutWritingImmutableColumns()
    {
        var (tenantA, _, _, _, _) = await SeedAsync();

        await using var runtime = Open(PrimaryDatabaseRole.Runtime, tenantA);
        await runtime.Database.OpenConnectionAsync();
        await PostgresCategoryMutationPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);

        var categoryRepo = new CategoryRepository(runtime);
        var created = await categoryRepo.Create(new Category
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            MasterCode = $"DETACHED_{Guid.CreateVersion7():N}",
            FullName = "Detached category",
            TenantId = tenantA,
            Tenant = null!
        });

        // UpdateCategoryCommandHandler reads through the AsNoTracking repository projection and then
        // saves the mutated detached instance, so the immutable key columns must stay out of the UPDATE.
        runtime.ChangeTracker.Clear();
        var loaded = await categoryRepo.GetById(created.Id);
        await Assert.That(loaded).IsNotNull();
        loaded!.FullName = "Updated detached category";
        await categoryRepo.Update(loaded);

        runtime.ChangeTracker.Clear();
        var persisted = await categoryRepo.GetById(created.Id);
        await Assert.That(persisted!.FullName).IsEqualTo("Updated detached category");
        await Assert.That(persisted.TenantId).IsEqualTo(tenantA);
        // SaveChanges advances the optimistic stamp; the unchanged identity and tenant stay intact.
        await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(created.ConcurrencyStamp);
    }

    [Test]
    [Arguments("table-update")]
    [Arguments("immutable-column-update")]
    [Arguments("delete-grant-option")]
    [Arguments("public-delete")]
    [Arguments("partial-revoke")]
    [Arguments("rls-disabled")]
    [Arguments("global-select")]
    public async Task InstallerRejectsCategoryMutationDriftWithoutRepair(string drift)
    {
        var (tenantA, _, _, _, _) = await SeedAsync();
        await using var writer = Open(PrimaryDatabaseRole.Migrator, tenantA);
        await writer.Database.OpenConnectionAsync();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        string linkTable = names.Table(typeof(CategoryTypeCategories));
        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string tenantCol = names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.TenantId));
        string catIdCol = names.Column(typeof(CategoryTypeCategories), nameof(CategoryTypeCategories.CategoryId));

        await writer.Database.ExecuteSqlRawAsync(drift switch
        {
            "table-update" => $"GRANT UPDATE ON {linkTable} TO {runtime}",
            "immutable-column-update" => $"GRANT UPDATE ({tenantCol}) ON {linkTable} TO {runtime}",
            "delete-grant-option" => $"GRANT DELETE ON {linkTable} TO {runtime} WITH GRANT OPTION",
            "public-delete" => $"GRANT DELETE ON {linkTable} TO PUBLIC",
            "partial-revoke" => $"REVOKE UPDATE ({catIdCol}) ON {linkTable} FROM {runtime}",
            "rls-disabled" => $"ALTER TABLE {linkTable} DISABLE ROW LEVEL SECURITY",
            "global-select" => $"GRANT SELECT ({catIdCol}) ON {linkTable} TO {global}",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        });

        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresCategoryMutationPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.Message).IsEqualTo("category_mutation_permissions_drift");
        await transaction.RollbackAsync();
    }

    private async Task<(Guid TenantA, Guid TenantB, int TypeId, Guid ForeignCategoryId, Guid ForeignLinkId)> SeedAsync()
    {
        await using var db = fixture.CreateDbContext();
        db.BindTenantLease();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        int typeId = Random.Shared.Next(91000, 99000);
        var a = new Tenant { Id = tenantA, FullName = "CatMut A", Slug = $"catmut-a-{tenantA:N}", TenantStatusId = status.Id, TenantStatus = status };
        var b = new Tenant { Id = tenantB, FullName = "CatMut B", Slug = $"catmut-b-{tenantB:N}", TenantStatusId = status.Id, TenantStatus = status };
        var type = new CategoryType { Id = typeId, MasterCode = $"CATMUT_TYPE_{typeId}", FullName = $"CatMut Type {typeId}" };
        var foreignCategory = new Category
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            MasterCode = $"FOREIGN_{Guid.CreateVersion7():N}",
            FullName = "Foreign Category",
            TenantId = tenantB,
            Tenant = b
        };
        var foreignLink = new CategoryTypeCategories
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantB,
            Tenant = b,
            CategoryId = foreignCategory.Id,
            Category = foreignCategory,
            CategoryTypeId = typeId,
            CategoryType = type
        };
        db.Tenants.AddRange(a, b);
        db.CategoryTypes.Add(type);
        db.Categories.Add(foreignCategory);
        db.CategoryTypeCategories.Add(foreignLink);
        await db.SaveChangesAsync();
        return (tenantA, tenantB, typeId, foreignCategory.Id, foreignLink.Id);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid tenantId)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance
            : PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(builder.Options) { TenantContext = new SelectedTenant(tenantId) };
        context.BindTenantLease();
        return context;
    }

    private sealed class SelectedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public string TenantSlug => "selected";
        public bool IsResolved => true;
    }
}
