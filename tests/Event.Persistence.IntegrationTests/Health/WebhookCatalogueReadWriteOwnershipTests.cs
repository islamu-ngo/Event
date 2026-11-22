using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookCatalogueReadWriteOwnershipTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CatalogueCreatesReadsAndUpdatesGlobalMetadataWithoutPromotingRuntimeCaller()
    {
        Guid id = Guid.CreateVersion7();
        string name = $"catalogue.ownership{id:N}.created";
        var services = new ServiceCollection();
        services.AddScoped<ExploreDbContext>(_ =>
        {
            var context = fixture.CreateEnrolledTenantlessDbContext();
            context.BindTenantLease();
            return context;
        });
        services.AddSingleton<IGlobalExploreDbContextFactory>(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
        services.AddSingleton(fixture.NativeQueueEnrollment);
        services.AddScoped<IWebhookEventTypeRepository, WebhookEventTypeRepository>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IWebhookEventTypeRepository>();
        await runtime.Database.OpenConnectionAsync();

        try
        {
            await AssertRuntimeCannotAccessCatalogue(runtime, id);

            var created = await repository.CreateAsync(new WebhookEventType
            {
                Id = id,
                Name = name,
                GroupName = "catalogue",
                Description = "Original public catalogue metadata",
                SchemaJson = """{"type":"object"}""",
                SchemaVersion = 1,
                IsPublic = true,
                IsEnabled = true,
                PayloadRetentionDays = 14,
                CreatedAt = DateTime.UtcNow
            }, CancellationToken.None);

            await Assert.That(created.Id).IsEqualTo(id);
            var byNames = await repository.GetByNamesAsync([name], CancellationToken.None);
            await Assert.That(byNames.Count).IsEqualTo(1);
            await Assert.That(byNames[0].Id).IsEqualTo(id);
            await Assert.That(byNames[0].Description).IsEqualTo("Original public catalogue metadata");
            await Assert.That(byNames[0].IsPublic).IsTrue();
            var byIds = await repository.GetByIdsAsync([id], CancellationToken.None);
            await Assert.That(byIds.Count).IsEqualTo(1);
            await Assert.That(byIds[0].Name).IsEqualTo(name);

            var originalCreatedAt = byNames[0].CreatedAt;
            var originalCreatedBy = byNames[0].CreatedBy;
            byNames[0].Name = $"forbidden.rename{id:N}";
            byNames[0].CreatedAt = DateTime.UnixEpoch;
            byNames[0].CreatedBy = Guid.CreateVersion7();
            byNames[0].Description = "Updated public catalogue metadata";
            byNames[0].SchemaJson = """{"type":"object","required":["event"]}""";
            byNames[0].SchemaVersion = 2;
            byNames[0].PayloadRetentionDays = 7;
            await repository.UpdateAsync(byNames[0], CancellationToken.None);

            // A new DI scope cannot satisfy this observation from the writer's tracked entity.
            await using (var freshScope = provider.CreateAsyncScope())
            {
                var fresh = freshScope.ServiceProvider.GetRequiredService<IWebhookEventTypeRepository>();
                var stored = await fresh.GetByNamesAsync([name], CancellationToken.None);
                await Assert.That(stored.Count).IsEqualTo(1);
                await Assert.That(stored[0].Id).IsEqualTo(id);
                await Assert.That(stored[0].Name).IsEqualTo(name);
                await Assert.That(stored[0].CreatedAt).IsEqualTo(originalCreatedAt);
                await Assert.That(stored[0].CreatedBy).IsEqualTo(originalCreatedBy);
                await Assert.That(stored[0].GroupName).IsEqualTo("catalogue");
                await Assert.That(stored[0].Description).IsEqualTo("Updated public catalogue metadata");
                await Assert.That(stored[0].SchemaVersion).IsEqualTo(2);
                await Assert.That(stored[0].PayloadRetentionDays).IsEqualTo(7);
                await Assert.That(stored[0].IsPublic).IsTrue();
                await Assert.That(stored[0].IsEnabled).IsTrue();
                using var schema = System.Text.Json.JsonDocument.Parse(stored[0].SchemaJson);
                await Assert.That(schema.RootElement.GetProperty("required")[0].GetString()).IsEqualTo("event");
            }

            await AssertRuntimeCannotAccessCatalogue(runtime, id);
            await Assert.That(runtime.TenantFilterTenantId).IsNull();
            await Assert.That(runtime.IsTenantFilterBypassed).IsFalse();
            await Assert.That(runtime.CurrentUserService).IsNull();
            await Assert.That(runtime.Database.CurrentTransaction).IsNull();
            await Assert.That(runtime.ChangeTracker.Entries()).IsEmpty();

            await using var global = await scope.ServiceProvider.GetRequiredService<IGlobalExploreDbContextFactory>()
                .CreateDbContextAsync();
            await global.Database.OpenConnectionAsync();
            string table = new HumanPrincipalSourceSql(global).Table(typeof(WebhookEventType));
            var deleteDenial = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"DELETE FROM {table} WHERE id = {{0}}", id));
            await Assert.That(deleteDenial.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(
                options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
            options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
            await using var cleanup = new ExploreDbContext(options.Options);
            cleanup.BindTenantLease();
            await cleanup.WebhookEventTypes.Where(row => row.Id == id && row.Name == name)
                .ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task CatalogueRejectsMissingEnrollmentEvenForEmptySelection()
    {
        var repository = new WebhookEventTypeRepository(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
        var denial = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await repository.GetByIdsAsync([], CancellationToken.None));
        await Assert.That(denial.Message).IsEqualTo("webhook_catalogue_enrollment_required");
    }

    [Test]
    public async Task CatalogueRejectsDeleteGrantDriftUntilExactRevokeRestoresAdmission()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new WebhookEventTypeRepository(factory, fixture.NativeQueueEnrollment);
        string name = $"catalogue.absent{Guid.CreateVersion7():N}";
        await Assert.That(await repository.GetByNameAsync(name, CancellationToken.None)).IsNull();
        await using var selectedGlobal = await factory.CreateDbContextAsync();
        await selectedGlobal.Database.OpenConnectionAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        await using var migrator = new ExploreDbContext(options.Options);
        migrator.BindTenantLease();
        await migrator.Database.OpenConnectionAsync();
        string table = new HumanPrincipalSourceSql(migrator).Table(typeof(WebhookEventType));
        string global = migrator.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        try
        {
            await migrator.Database.ExecuteSqlRawAsync($"GRANT DELETE ON {table} TO {global}");
            // The repository re-admits the fixed leaf on every fresh observation.
            var denial = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await repository.GetByNameAsync(name, CancellationToken.None));
            await Assert.That(denial.Message).IsEqualTo("webhook_catalogue_permissions_not_ready");
            // An already selected physical owner must also re-admit the leaf manifest.
            var selectedDenial = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresWebhookCataloguePermissions.RequireReadyAsync(selectedGlobal, fixture.NativeQueueEnrollment));
            await Assert.That(selectedDenial.Message).IsEqualTo("webhook_catalogue_permissions_not_ready");
            var installerDenial = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await PostgresWebhookCataloguePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment));
            await Assert.That(installerDenial.Message).IsEqualTo("webhook_catalogue_permissions_drift");
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"REVOKE DELETE ON {table} FROM {global}");
        }
        await PostgresWebhookCataloguePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        await PostgresWebhookCataloguePermissions.RequireReadyAsync(selectedGlobal, fixture.NativeQueueEnrollment);
        await Assert.That(await repository.GetByNameAsync(name, CancellationToken.None)).IsNull();
    }

    private static async Task AssertRuntimeCannotAccessCatalogue(ExploreDbContext runtime, Guid id)
    {
        string table = new HumanPrincipalSourceSql(runtime).Table(typeof(WebhookEventType));
        var readDenial = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} WHERE id = {{0}}", id));
        await Assert.That(readDenial.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

        // Required catalogue fields prevent a row from being created even if INSERT is accidentally granted.
        // Such a constraint failure is still a failure: only the native privilege denial is accepted.
        var insertDenial = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Database.ExecuteSqlRawAsync($"INSERT INTO {table} DEFAULT VALUES"));
        await Assert.That(insertDenial.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

        var updateDenial = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.Database.ExecuteSqlRawAsync(
                $"UPDATE {table} SET description = description WHERE id = {{0}}", id));
        await Assert.That(updateDenial.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }
}
