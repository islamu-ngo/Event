// ABOUTME: Integration tests for EfCoreUnitOfWork transactional correctness against a real Postgres database.
// ABOUTME: Covers commit, rollback, nesting, generic returns, and translated optimistic-concurrency conflicts.

using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.UnitOfWork;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class EfCoreUnitOfWorkTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EfCoreUnitOfWorkTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenOperationSucceeds_CommitsAllWrites()
    {
        using var context = _fixture.CreateDbContext();
        var uow = new EfCoreUnitOfWork(context);
        var key1 = $"uow-commit-{Guid.NewGuid():N}";
        var key2 = $"uow-commit-{Guid.NewGuid():N}";

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            context.Set<SystemSetting>().Add(new SystemSetting { SettingKey = key1, Value = "v1" });
            await context.SaveChangesAsync(ct);
            context.Set<SystemSetting>().Add(new SystemSetting { SettingKey = key2, Value = "v2" });
            await context.SaveChangesAsync(ct);
        });

        // Verify both records persisted
        using var verifyContext = _fixture.CreateDbContext();
        var s1 = await verifyContext.Set<SystemSetting>().FirstOrDefaultAsync(s => s.SettingKey == key1);
        var s2 = await verifyContext.Set<SystemSetting>().FirstOrDefaultAsync(s => s.SettingKey == key2);

        await Assert.That(s1).IsNotNull();
        await Assert.That(s2).IsNotNull();
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenOperationThrows_RollsBackAllWrites()
    {
        using var context = _fixture.CreateDbContext();
        var uow = new EfCoreUnitOfWork(context);
        var key = $"uow-rollback-{Guid.NewGuid():N}";

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                context.Set<SystemSetting>().Add(new SystemSetting { SettingKey = key, Value = "should-not-persist" });
                await context.SaveChangesAsync(ct);

                // Simulate mid-workflow failure after first write
                throw new InvalidOperationException("Simulated workflow failure");
            });
        });

        // Verify the partial write was rolled back
        using var verifyContext = _fixture.CreateDbContext();
        var setting = await verifyContext.Set<SystemSetting>().FirstOrDefaultAsync(s => s.SettingKey == key);

        await Assert.That(setting).IsNull();
    }

    [Test]
    public async Task ExecuteInTransactionAsync_Generic_WhenOperationSucceeds_ReturnsValue()
    {
        using var context = _fixture.CreateDbContext();
        var uow = new EfCoreUnitOfWork(context);
        var key = $"uow-generic-{Guid.NewGuid():N}";

        var result = await uow.ExecuteInTransactionAsync(async ct =>
        {
            var setting = new SystemSetting { SettingKey = key, Value = "generic-return" };
            context.Set<SystemSetting>().Add(setting);
            await context.SaveChangesAsync(ct);
            return setting.SettingKey;
        });

        await Assert.That(result).IsEqualTo(key);

        // Verify record was persisted
        using var verifyContext = _fixture.CreateDbContext();
        var persisted = await verifyContext.Set<SystemSetting>().FirstOrDefaultAsync(s => s.SettingKey == key);
        await Assert.That(persisted).IsNotNull();
    }

    [Test]
    public async Task ExecuteSerializableAsync_UsesSerializableIsolation()
    {
        using var context = _fixture.CreateDbContext();
        var unitOfWork = new EfCoreUnitOfWork(context);

        var isolationLevel = await unitOfWork.ExecuteSerializableAsync(
            _ => Task.FromResult(
                context.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel));

        await Assert.That(isolationLevel).IsEqualTo(IsolationLevel.Serializable);
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenNestedTransaction_ThrowsInvalidOperationException()
    {
        using var context = _fixture.CreateDbContext();
        var uow = new EfCoreUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                // Attempt to nest — must throw immediately
                await uow.ExecuteInTransactionAsync(_ => Task.CompletedTask, ct);
            });
        });
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenNestedTransaction_ErrorMessageMentionsNestedTransactions()
    {
        using var context = _fixture.CreateDbContext();
        var uow = new EfCoreUnitOfWork(context);
        InvalidOperationException? caught = null;

        try
        {
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                await uow.ExecuteInTransactionAsync(_ => Task.CompletedTask, ct);
            });
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("Nested transactions are not supported");
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenConcurrencyStampStale_ThrowsConcurrencyConflictException()
    {
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();

        // Seed a real concurrency-aware aggregate so stale writes trigger EF concurrency translation.
        using (var seedContext = _fixture.CreateDbContext())
        {
            seedContext.Set<Tenant>().Add(new Tenant
            {
                Id = tenantId,
                FullName = $"Tenant {tenantId:N}",
                Slug = $"tenant-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });

            var location = new Location
            {
                Id = locationId,
                FullName = "Concurrency Test Location",
                Country = "BE",
                City = "Brussels",
                TenantId = tenantId,
                Tenant = null!
            };
            location.SetManualAddress("123 Test Street", "1000");
            seedContext.Set<Location>().Add(location);

            seedContext.Set<LocationRoom>().Add(new LocationRoom
            {
                Id = roomId,
                Name = "Room A",
                LocationId = locationId,
                Location = null!,
                TenantId = tenantId,
                Tenant = null!
            });

            await seedContext.SaveChangesAsync();
        }

        // Load the row in session A.
        using var contextA = _fixture.CreateDbContext();
        var entityA = await contextA.Set<LocationRoom>().FirstAsync(room => room.Id == roomId);

        // Mutate and commit the same row from session B so its stamp advances.
        using (var contextB = _fixture.CreateDbContext())
        {
            var entityB = await contextB.Set<LocationRoom>().FirstAsync(room => room.Id == roomId);
            entityB.Name = "Room B";
            await contextB.SaveChangesAsync();
        }

        // Session A still holds the old stamp; the save must be translated.
        var uowA = new EfCoreUnitOfWork(contextA);
        ConcurrencyConflictException? caught = null;
        try
        {
            await uowA.ExecuteInTransactionAsync(async ct =>
            {
                entityA.Name = "Room C";
                await contextA.SaveChangesAsync(ct);
            });
        }
        catch (ConcurrencyConflictException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(caught.EntityType).IsEqualTo(nameof(LocationRoom));
    }

    [Test]
    public async Task ExecuteInTransactionAsync_WhenTenantBrandingDocumentStampStale_TranslatesRepositoryConflict()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        const string originalPayload = "{\"displayName\":\"Original Brand\"}";
        const string concurrentPayload = "{\"displayName\":\"Concurrent Brand\"}";
        const string stalePayload = "{\"displayName\":\"Stale Brand\"}";

        using (var seedContext = _fixture.CreateDbContext())
        {
            seedContext.Set<Tenant>().Add(new Tenant
            {
                Id = tenantId,
                FullName = $"Tenant {tenantId:N}",
                Slug = $"tenant-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });

            var document = TenantSettingsDocument.Create(
                tenantId,
                SettingsDocumentKeys.Tenant.Branding,
                schemaVersion: 1,
                defaultsVersion: "2026-05-branding",
                payloadJson: originalPayload);
            document.Id = documentId;
            seedContext.Set<TenantSettingsDocument>().Add(document);
            await seedContext.SaveChangesAsync();
        }

        using var contextA = _fixture.CreateDbContext();
        var repositoryA = new TenantSettingsDocumentRepository(contextA);
        var documentA = await repositoryA.GetTrackedByTenantAndDocumentKey(
            tenantId,
            SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(documentA).IsNotNull();

        using (var contextB = _fixture.CreateDbContext())
        {
            var repositoryB = new TenantSettingsDocumentRepository(contextB);
            var documentB = await repositoryB.GetTrackedByTenantAndDocumentKey(
                tenantId,
                SettingsDocumentKeys.Tenant.Branding);
            documentB!.UpdatePayload(documentB.SchemaVersion, documentB.DefaultsVersion, concurrentPayload);
            await repositoryB.Update(documentB);
        }

        var unitOfWorkA = new EfCoreUnitOfWork(contextA);
        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            unitOfWorkA.ExecuteInTransactionAsync(async _ =>
            {
                documentA!.UpdatePayload(documentA.SchemaVersion, documentA.DefaultsVersion, stalePayload);
                await repositoryA.Update(documentA);
            }));

        await Assert.That(exception!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(exception.EntityType).IsEqualTo(nameof(TenantSettingsDocument));
        await Assert.That(exception.EntityId).IsEqualTo(documentId.ToString());

        using var verifyContext = _fixture.CreateDbContext();
        var persisted = await new TenantSettingsDocumentRepository(verifyContext)
            .GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(persisted).IsNotNull();
        using var persistedPayload = JsonDocument.Parse(persisted!.PayloadJson);
        await Assert.That(persistedPayload.RootElement.GetProperty("displayName").GetString()).IsEqualTo("Concurrent Brand");
    }
}
