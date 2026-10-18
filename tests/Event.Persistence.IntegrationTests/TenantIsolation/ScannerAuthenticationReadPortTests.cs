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
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
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
                Id = tenantId,
                FullName = "Scanner tenant",
                Slug = $"scanner-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            var source = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                ActorId = actor.Id,
                Actor = actor,
                Title = "Scanner admission",
                EventStatus = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Digital,
                EventFormat = null!,
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
    [Arguments("runtime-execute")]
    [Arguments("runtime-executor")]
    [Arguments("runtime-via-global-login")]
    [Arguments("runtime-via-migrator-login")]
    [Arguments("runtime-owner")]
    [Arguments("runtime-migrator")]
    [Arguments("global-owner")]
    [Arguments("global-migrator")]
    [Arguments("global-via-migrator-login")]
    [Arguments("owner-flags")]
    [Arguments("migrator-flags")]
    [Arguments("executor-flags")]
    [Arguments("owner-membership-inherit")]
    [Arguments("owner-membership-set")]
    [Arguments("owner-membership-admin")]
    [Arguments("migrator-membership-inherit")]
    [Arguments("migrator-membership-set")]
    [Arguments("migrator-membership-admin")]
    [Arguments("global-membership-inherit")]
    [Arguments("global-membership-set")]
    [Arguments("global-membership-admin")]
    [Arguments("missing-enrollment")]
    [Arguments("global-column")]
    [Arguments("owner-write")]
    public async Task AuthorityDriftRejectsEntryAndInstallerWithoutRepair(string drift)
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await migrator.Database.BeginTransactionAsync();
        var enrollment = fixture.NativeQueueEnrollment;
        string owner = PostgresAdmissionScannerAuthenticationContract.OwnerRole;
        string executor = PostgresAdmissionScannerAuthenticationContract.GlobalRole;
        string migration = PostgresAdmissionScannerAuthenticationContract.MigratorRole;
        string signature = PostgresAdmissionScannerAuthenticationContract.FunctionSql(migrator) + "(integer[], text[])";
        var entity = migrator.Model.FindEntityType(typeof(AdmissionScannerCapability))!;
        string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
        string column = Quote(entity.FindProperty(nameof(AdmissionScannerCapability.LookupDigest))!.GetColumnName()!);
        string grant = drift switch
        {
            "runtime-execute" => $"GRANT EXECUTE ON FUNCTION {signature} TO {Quote(enrollment.RuntimeLogin)}",
            "runtime-executor" => $"GRANT {executor} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "runtime-via-global-login" => $"GRANT {Quote(enrollment.GlobalLogin)} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "runtime-via-migrator-login" => $"GRANT {Quote(enrollment.MigratorLogin)} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE",
            "runtime-owner" => $"GRANT {owner} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "runtime-migrator" => $"GRANT {migration} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "global-owner" => $"GRANT {owner} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "global-migrator" => $"GRANT {migration} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "global-via-migrator-login" => $"GRANT {Quote(enrollment.MigratorLogin)} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE",
            "owner-flags" => $"ALTER ROLE {owner} BYPASSRLS",
            "migrator-flags" => $"ALTER ROLE {migration} INHERIT",
            "executor-flags" => $"ALTER ROLE {executor} LOGIN",
            "owner-membership-inherit" => $"GRANT {owner} TO {migration} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "owner-membership-set" => $"GRANT {owner} TO {migration} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE",
            "owner-membership-admin" => $"GRANT {owner} TO {migration} WITH INHERIT FALSE, SET TRUE, ADMIN TRUE",
            "migrator-membership-inherit" => $"GRANT {migration} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "migrator-membership-set" => $"GRANT {migration} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE",
            "migrator-membership-admin" => $"GRANT {migration} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN TRUE",
            "global-membership-inherit" => $"GRANT {executor} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE",
            "global-membership-set" => $"GRANT {executor} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "global-membership-admin" => $"GRANT {executor} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN TRUE",
            "missing-enrollment" => $"REVOKE {migration} FROM {Quote(enrollment.MigratorLogin)}",
            "global-column" => $"GRANT SELECT ({column}) ON {table} TO {Quote(enrollment.GlobalLogin)}",
            "owner-write" => $"GRANT UPDATE ({column}) ON {table} TO {owner}",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migrator.Database.ExecuteSqlRawAsync(grant);
        await transaction.CreateSavepointAsync("before_entry");
        // Without inherited EXECUTE, use the fixture's superuser to reach the native entry guard.
        if (drift != "global-membership-inherit")
            await migrator.Database.ExecuteSqlRawAsync(
                $"SET LOCAL SESSION AUTHORIZATION {Quote(enrollment.GlobalLogin)}");
        await using (var command = migrator.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT * FROM {PostgresAdmissionScannerAuthenticationContract.FunctionSql(migrator)}(@versions, @digests)";
            command.Parameters.Add(new NpgsqlParameter("versions", new[] { 7 }));
            command.Parameters.Add(new NpgsqlParameter("digests", new[] { Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
            await Assert.That(denied.SqlState).IsEqualTo("55000");
        }
        await transaction.RollbackToSavepointAsync("before_entry");
        await transaction.CreateSavepointAsync("before_install");
        var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, enrollment));
        await Assert.That(reinstall.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_install");
        var stillDrifted = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, enrollment));
        await Assert.That(stillDrifted.SqlState).IsEqualTo("55000");
    }

    [Test]
    public async Task ConfiguredGlobalRemainsValidAfterCleanReinstall()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new AdmissionScannerAuthenticationRepository(factory);
        await Assert.That(await repository.FindByDigestCandidatesAsync(
            [new(7, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))], default)).IsNull();
    }

    [Test]
    public async Task InstallerRejectsDifferentConfiguredGlobalLogin()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        var enrollment = fixture.NativeQueueEnrollment;
        var changed = enrollment with
        {
            GlobalLogin = enrollment.EmailInstanceMutationLogin,
            EmailInstanceMutationLogin = enrollment.GlobalLogin
        };
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, changed));
        await Assert.That(denied.SqlState).IsEqualTo("55000");
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task UnenrolledSessionCannotExecuteWithCleanEnrollment(PrimaryDatabaseRole role)
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await migrator.Database.BeginTransactionAsync();
        await migrator.Database.ExecuteSqlRawAsync(
            $"SET LOCAL SESSION AUTHORIZATION {Quote(fixture.GetNativeQueueOptions(role).Username!)}");
        await using var command = migrator.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = $"SELECT * FROM {PostgresAdmissionScannerAuthenticationContract.FunctionSql(migrator)}(@versions, @digests)";
        command.Parameters.Add(new NpgsqlParameter("versions", new[] { 7 }));
        command.Parameters.Add(new NpgsqlParameter("digests", new[] { Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime) options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options);
    }
}
