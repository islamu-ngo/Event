using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
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
        await using var human = new HumanSecuritySourceGate(migrator);
        await using var humanLease = await human.AcquireAsync(default);
        await using var enrollmentGate = new EventDiscoveryEnrollmentGate(migrator);
        await using var enrollmentLease = await enrollmentGate.AcquireAsync(default);
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
        // Fixed scanner storage is tenant-bound even without EF's query filter.
        await Assert.That(await request.AdmissionScannerCapabilities.IgnoreQueryFilters().AnyAsync()).IsFalse();
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new AdmissionScannerAuthenticationRepository(factory, fixture.NativeQueueEnrollment);
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
    [Arguments("runtime-select-grant-option")]
    [Arguments("runtime-executor")]
    [Arguments("runtime-via-global-login")]
    [Arguments("runtime-via-migrator-login")]
    [Arguments("runtime-update")]
    [Arguments("retired-owner")]
    [Arguments("retired-migrator")]
    [Arguments("profile-create-role")]
    [Arguments("global-via-migrator-login")]
    [Arguments("profile-bypass")]
    [Arguments("profile-inherit")]
    [Arguments("profile-login")]
    [Arguments("profile-create-db")]
    [Arguments("profile-replication")]
    [Arguments("profile-table-select")]
    [Arguments("global-table-select")]
    [Arguments("profile-grant-option")]
    [Arguments("unexpected-policy")]
    [Arguments("global-membership-inherit")]
    [Arguments("global-membership-set")]
    [Arguments("global-membership-admin")]
    [Arguments("missing-enrollment")]
    [Arguments("global-column")]
    [Arguments("profile-write")]
    public async Task AuthorityDriftRejectsEntryAndInstallerWithoutRepair(string drift)
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        var enrollment = fixture.NativeQueueEnrollment;
        string owner = PostgresAdmissionScannerAuthenticationContract.OwnerRole;
        string executor = PostgresAdmissionScannerAuthenticationContract.GlobalRole;
        string migration = PostgresAdmissionScannerAuthenticationContract.MigratorRole;
        var entity = migrator.Model.FindEntityType(typeof(AdmissionScannerCapability))!;
        string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
        string column = Quote(entity.FindProperty(nameof(AdmissionScannerCapability.LookupDigest))!.GetColumnName()!);
        string global = Quote(enrollment.GlobalLogin);
        string runtime = Quote(enrollment.RuntimeLogin);
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var selected = await factory.CreateDbContextAsync(default);
        await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(selected, enrollment);
        (string apply, string undo) = drift switch
        {
            "runtime-select-grant-option" => ($"GRANT SELECT ({column}) ON {table} TO {runtime} WITH GRANT OPTION", $"REVOKE GRANT OPTION FOR SELECT ({column}) ON {table} FROM {runtime}"),
            "runtime-update" => ($"GRANT UPDATE ({column}) ON {table} TO {runtime}", $"REVOKE UPDATE ({column}) ON {table} FROM {runtime}"),
            "runtime-executor" => ($"GRANT {executor} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE", $"REVOKE {executor} FROM {runtime}"),
            "runtime-via-global-login" => ($"GRANT {global} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE", $"REVOKE {global} FROM {runtime}"),
            "runtime-via-migrator-login" => ($"GRANT {Quote(enrollment.MigratorLogin)} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE", $"REVOKE {Quote(enrollment.MigratorLogin)} FROM {runtime}"),
            "global-via-migrator-login" => ($"GRANT {Quote(enrollment.MigratorLogin)} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE", $"REVOKE {Quote(enrollment.MigratorLogin)} FROM {global}"),
            "retired-owner" => ($"CREATE ROLE {owner} NOLOGIN", $"DROP ROLE {owner}"),
            "retired-migrator" => ($"CREATE ROLE {migration} NOLOGIN", $"DROP ROLE {migration}"),
            "profile-create-role" => ($"ALTER ROLE {executor} CREATEROLE", $"ALTER ROLE {executor} NOCREATEROLE"),
            "profile-bypass" => ($"ALTER ROLE {executor} BYPASSRLS", $"ALTER ROLE {executor} NOBYPASSRLS"),
            "profile-inherit" => ($"ALTER ROLE {executor} INHERIT", $"ALTER ROLE {executor} NOINHERIT"),
            "profile-login" => ($"ALTER ROLE {executor} LOGIN", $"ALTER ROLE {executor} NOLOGIN"),
            "profile-create-db" => ($"ALTER ROLE {executor} CREATEDB", $"ALTER ROLE {executor} NOCREATEDB"),
            "profile-replication" => ($"ALTER ROLE {executor} REPLICATION", $"ALTER ROLE {executor} NOREPLICATION"),
            "profile-table-select" => ($"GRANT SELECT ON {table} TO {executor}", $"REVOKE SELECT ON {table} FROM {executor}"),
            "global-table-select" => ($"GRANT SELECT ON {table} TO {global}", $"REVOKE SELECT ON {table} FROM {global}"),
            "profile-grant-option" => ($"GRANT SELECT ({column}) ON {table} TO {executor} WITH GRANT OPTION", $"REVOKE GRANT OPTION FOR SELECT ({column}) ON {table} FROM {executor}"),
            "unexpected-policy" => ($"CREATE POLICY auth_unexpected ON {table} FOR SELECT TO {global} USING(true)", $"DROP POLICY auth_unexpected ON {table}"),
            "global-membership-inherit" => ($"GRANT {executor} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN FALSE", $"GRANT {executor} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "global-membership-set" => ($"GRANT {executor} TO {global} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE", $"GRANT {executor} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "global-membership-admin" => ($"GRANT {executor} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN TRUE", $"GRANT {executor} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "missing-enrollment" => ($"REVOKE {executor} FROM {global}", $"GRANT {executor} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE"),
            "global-column" => ($"GRANT SELECT ({column}) ON {table} TO {global}", $"REVOKE SELECT ({column}) ON {table} FROM {global}"),
            "profile-write" => ($"GRANT UPDATE ({column}) ON {table} TO {executor}", $"REVOKE UPDATE ({column}) ON {table} FROM {executor}"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migrator.Database.ExecuteSqlRawAsync(apply);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(selected, enrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, enrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(selected, enrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, enrollment));
        }
        finally
        {
            if (drift == "profile-table-select")
            {
                string[] properties =
                [
                    nameof(AdmissionScannerCapability.Id), nameof(AdmissionScannerCapability.TenantId),
                    nameof(AdmissionScannerCapability.IssueRequestId), nameof(AdmissionScannerCapability.EventId),
                    nameof(AdmissionScannerCapability.AdmissionTargetId), nameof(AdmissionScannerCapability.LookupKeyVersion),
                    nameof(AdmissionScannerCapability.LookupDigest), nameof(AdmissionScannerCapability.DeviceLabel),
                    nameof(AdmissionScannerCapability.Actions), nameof(AdmissionScannerCapability.ExpiresAt),
                    nameof(AdmissionScannerCapability.IssuedByActorId), nameof(AdmissionScannerCapability.IssuedAt),
                    nameof(AdmissionScannerCapability.RevokedByActorId), nameof(AdmissionScannerCapability.RevokedAt),
                    nameof(AdmissionScannerCapability.RevocationReason), nameof(AdmissionScannerCapability.ConcurrencyStamp)
                ];
                string columns = string.Join(",", properties.Select(property =>
                    Quote(entity.FindProperty(property)!.GetColumnName()!)));
                await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await migrator.Database.BeginTransactionAsync();
                    await migrator.Database.ExecuteSqlRawAsync(undo);
                    await migrator.Database.ExecuteSqlRawAsync($"GRANT SELECT ({columns}) ON {table} TO {Quote(executor)}");
                    await transaction.CommitAsync();
                });
            }
            else
            {
                await migrator.Database.ExecuteSqlRawAsync(undo);
            }
        }
        await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(selected, enrollment);
    }

    [Test]
    public async Task ConfiguredGlobalRemainsValidAfterCleanReinstall()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new AdmissionScannerAuthenticationRepository(factory, fixture.NativeQueueEnrollment);
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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, changed));
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task UnenrolledSessionCannotExecuteWithCleanEnrollment(PrimaryDatabaseRole role)
    {
        await using var selected = Open(role);
        if (role != PrimaryDatabaseRole.Migrator) selected.BindTenantLease();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(selected, fixture.NativeQueueEnrollment));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime) options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options);
        if (role == PrimaryDatabaseRole.Migrator) context.BindTenantLease();
        return context;
    }
}
