using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ScannerAuthenticationReadPortTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task PairedLookupRejectsCrossedDigestsAndAmbiguousCredentials()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        Guid userId = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), UserId = userId,
            ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            Pii = new ActorPii { DisplayName = "Scanner owner" }
        };
        migrator.Users.Add(new User
        {
            Id = userId,
            Pii = new UserPii { Email = $"{userId:N}@example.test", FirstName = "Scanner", LastName = "Owner" }
        });
        migrator.Actors.Add(actor);
        await migrator.SaveChangesAsync();
        string digest = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var first = await SeedAsync(Guid.CreateVersion7());
        await using var request = Open(PrimaryDatabaseRole.Runtime);
        request.BindTenantLease();
        await request.Database.OpenConnectionAsync();
        var entity = request.Model.FindEntityType(typeof(AdmissionScannerCapability))!;
        await using (var direct = request.Database.GetDbConnection().CreateCommand())
        {
            direct.CommandText = $"SELECT * FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await direct.ExecuteScalarAsync(); });
            await Assert.That(denied.SqlState).IsEqualTo("42501");
        }
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new AdmissionScannerAuthenticationRepository(factory);
        string differentDigest = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await Assert.That(await repository.FindByDigestCandidatesAsync(
            [new(7, differentDigest), new(8, digest)], default)).IsNull();
        var found = await repository.FindByDigestCandidatesAsync([new(7, digest)], default);
        await Assert.That(found!.Id).IsEqualTo(first.Id);
        await Assert.That(found.TenantId).IsEqualTo(first.TenantId);
        await Assert.That(found.AdmissionTargetId).IsEqualTo(first.AdmissionTargetId);
        await Assert.That(request.TenantFilterTenantId).IsNull();

        await SeedAsync(Guid.CreateVersion7());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.FindByDigestCandidatesAsync([new(7, digest)], default));

        async Task<AdmissionScannerCapability> SeedAsync(Guid tenantId)
        {
            migrator.Tenants.Add(new Tenant
            {
                Id = tenantId, FullName = "Scanner tenant", Slug = $"scanner-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
            });
            var source = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, Title = "Scanner admission",
                EventStatus = null!, EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Digital, EventFormat = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            migrator.Events.Add(source);
            var target = AdmissionTarget.Create(Guid.CreateVersion7(), tenantId, source.Id,
                AdmissionTargetTypeEnum.Event, null, null);
            migrator.AdmissionTargets.Add(target);
            DateTime now = new(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc);
            var scanner = AdmissionScannerCapability.Issue(Guid.CreateVersion7(), tenantId,
                Guid.CreateVersion7(), source.Id, target.Id, 7, digest, "Scanner credential",
                AdmissionScannerCapabilityAction.CheckIn, now.AddHours(1), actor.Id, now);
            migrator.AdmissionScannerCapabilities.Add(scanner);
            await migrator.SaveChangesAsync();
            return scanner;
        }
    }

    [Test]
    public async Task UndeclaredExecutorRejectsEntryAndInstallerUntilRevoked()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        string signature = PostgresAdmissionScannerAuthenticationContract.FunctionSql(migrator) + "(integer[], text[])";
        string runtime = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Username.Replace("\"", "\"\"", StringComparison.Ordinal);
        await migrator.Database.ExecuteSqlRawAsync($"GRANT EXECUTE ON FUNCTION {signature} TO \"{runtime}\"");
        try
        {
            var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
            var repository = new AdmissionScannerAuthenticationRepository(factory);
            var rejected = await Assert.ThrowsAsync<RetryLimitExceededException>(async () =>
                await repository.FindByDigestCandidatesAsync(
                    [new(7, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))], default));
            await Assert.That(rejected.InnerException is PostgresException
                { SqlState: "55000", MessageText: "native_routine_acl_drift" }).IsTrue();
            var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator));
            await Assert.That(reinstall.SqlState).IsEqualTo("55000");
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{runtime}\"");
        }
        await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime) options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options);
    }
}
