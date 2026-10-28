using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Services.Privacy;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.None)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresSubjectPrivacyErasureAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private readonly string _authoritySchema = $"erasure_fact_{Guid.CreateVersion7():N}";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedSubjectSettlesBothTenantsWithoutPrivateTableAuthority(bool injectFailure)
    {
        GlobalLocationPrivacyErasureTests.ErasureGraph graph;
        StorageGraph storage;
        Guid tenantA;
        IdempotencyRecord[] receipts;
        await using (var seed = fixture.CreateDbContext())
        {
            await PostgresSubjectPrivacyErasureContract.ApplyAsync(seed);
            graph = await GlobalLocationPrivacyErasureTests.SeedErasureGraphAsync(seed);
            tenantA = await seed.Locations.IgnoreQueryFilters()
                .Where(row => row.Id == graph.LocationIds[0]).Select(row => row.TenantId).SingleAsync();
            receipts = new[] { graph.OwnerUserId.ToString("D"), graph.OwnerUserId.ToString("N"), graph.UnrelatedUserId.ToString("D") }
                .Select(user => new IdempotencyRecord
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantA,
                    Key = Guid.CreateVersion7().ToString("N"),
                    UserId = user,
                    RequestMethod = "POST",
                    RequestTarget = $"/api/users/{user}",
                    RequestContentType = "application/json",
                    RequestBodyHash = new string('a', 64),
                    PrincipalFingerprint = new string('b', 64),
                    StatusCode = 201,
                    ResponseBody = "private receipt",
                    ContentType = "text/plain",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                }).ToArray();
            seed.IdempotencyRecords.AddRange(receipts);
            var foreignHome = new Location
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantA,
                Tenant = null!,
                FullName = "Unrelated private home",
                Country = "BE",
                City = "Brussels",
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            foreignHome.ClassifyAsPrivateHome(graph.UnrelatedUserId);
            foreignHome.SetManualAddress("Unrelated private address", "1000");
            seed.Locations.Add(foreignHome);
            await seed.SaveChangesAsync();
            storage = await SeedStorageAsync(seed, graph, tenantA);
        }

        await using (var migrations = new CoLocatedPrivacyErasureAuthorityDbContext(
            AuthorityOptions(PrimaryDatabaseRole.Migrator)))
            await migrations.Database.MigrateAsync();
        await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(
            AuthorityOptions(PrimaryDatabaseRole.Runtime));
        var retained = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
            authority, TimeProvider.System, Options.Create(new PrivacyErasureOptions()));
        PrivacyErasureIntent fact = await retained.AppendAsync(PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, graph.OwnerUserId,
            PrivacyErasureReasonCode.AccountDeletion, new PrivacyErasureOptions().CurrentPolicyVersion));
        await using (var mirror = fixture.CreateDbContext())
        {
            mirror.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(
                fact, fact.AuthoritySequence, RandomNumberGenerator.GetBytes(32),
                fact.RecordedAtUtc.AddHours(1), fact.RecordedAtUtc));
            await mirror.SaveChangesAsync();
        }

        await using var logins = await OrdinaryLogins.CreateAsync(fixture);
        var runtimeFactory = new TenantExploreDbContextFactory(logins.RuntimeOptions);
        await using (var request = await runtimeFactory.CreateDbContextAsync(tenantA))
        {
            await AssertOrdinaryAsync(request, logins.RuntimeOptions.Username!);
            await Assert.That(await request.Locations.IgnoreQueryFilters()
                .CountAsync(row => graph.LocationIds.Contains(row.Id))).IsEqualTo(1);
            await Assert.That(request.TenantFilterTenantId).IsEqualTo(tenantA);
        }

        var globalFactory = new GlobalExploreDbContextFactory(logins.GlobalOptions);
        await using var application = await globalFactory.CreateDbContextAsync();
        await AssertOrdinaryAsync(application, logins.GlobalOptions.Username!);
        await Assert.That(application.TenantFilterTenantId).IsNull();
        await AssertPrivateReadDeniedAsync(application);
        if (injectFailure)
        {
            await using var administration = fixture.CreateDbContext();
            await GlobalLocationPrivacyErasureTests.InstallOutboxFailureTriggerAsync(administration);
        }
        bool settled = false;
        var locatorProtector = new PrivacyErasureProviderLocatorProtector(new EphemeralDataProtectionProvider());
        try
        {
            await using var workflow = CreateRuntime(application, retained, locatorProtector);
            if (injectFailure)
            {
                var failure = await Assert.ThrowsAsync<Exception>(() =>
                    workflow.Service.ReplayPendingAsync(CancellationToken.None));
                await Assert.That(failure).IsTypeOf<DbUpdateException>()
                    .Because(failure!.GetBaseException().Message);
                await Assert.That(((PostgresException)failure!.InnerException!).SqlState).IsEqualTo("P0001");
            }
            else
            {
                await workflow.Service.ReplayPendingAsync(CancellationToken.None);
                await workflow.Service.ReplayPendingAsync(CancellationToken.None);
                settled = true;
            }
        }
        finally
        {
            if (injectFailure)
            {
                await using var administration = fixture.CreateDbContext();
                await GlobalLocationPrivacyErasureTests.RemoveOutboxFailureTriggerAsync(administration);
            }
            await AssertRetainedAndApplicationAsync(retained, fact, graph, !settled, locatorProtector);
            await AssertStorageAsync(storage, graph, !settled);
        }
        await Assert.That(application.TenantFilterTenantId).IsNull();
        await AssertPrivateReadDeniedAsync(application);
        await using var receiptObserver = fixture.CreateDbContext();
        var retainedReceipts = await receiptObserver.IdempotencyRecords.AsNoTracking()
            .Where(row => receipts.Select(receipt => receipt.Id).Contains(row.Id)).ToArrayAsync();
        await Assert.That(retainedReceipts.Length).IsEqualTo(3);
        foreach (var original in receipts)
        {
            var row = retainedReceipts.Single(value => value.Id == original.Id);
            bool erased = settled && original.UserId != graph.UnrelatedUserId.ToString("D");
            await Assert.That(row.Key).IsEqualTo(original.Key);
            await Assert.That(row.StatusCode).IsEqualTo(erased ? -1 : 201);
            await Assert.That(row.UserId).IsEqualTo(erased ? null : original.UserId);
            await Assert.That(row.ResponseBody).IsEqualTo(erased ? null : "private receipt");
            await Assert.That(row.RequestTarget).IsEqualTo(erased ? "" : original.RequestTarget);
            await Assert.That(row.PrincipalFingerprint).IsEqualTo(erased ? "" : original.PrincipalFingerprint);
        }
        if (settled)
        {
            await using var request = await new TenantExploreDbContextFactory(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)).CreateDbContextAsync(tenantA);
            var retry = receipts[0];
            retry.Id = Guid.CreateVersion7();
            retry.UserId = graph.UnrelatedUserId.ToString("D");
            retry.StatusCode = 0;
            retry.ResponseBody = null;
            retry.ContentType = null;
            var result = await new IdempotencyRepository(request, enrollment: fixture.NativeQueueEnrollment).TryClaimAsync(retry);
            await Assert.That(result.IsOwner).IsFalse();
            await Assert.That(result.Lookup.Outcome).IsEqualTo(Explore.Application.Contracts.Persistence.IdempotencyLookupOutcome.RequestMismatch);
            await Assert.That(result.Record).IsNull();
        }
    }

    [Test]
    [Arguments("subject")]
    [Arguments("intent")]
    [Arguments("sequence")]
    [Arguments("policy")]
    public async Task ForgedPreparationAndMismatchedNativeTupleCannotErase(string changed)
    {
        GlobalLocationPrivacyErasureTests.ErasureGraph graph;
        await using (var seed = fixture.CreateDbContext())
        {
            await PostgresSubjectPrivacyErasureContract.ApplyAsync(seed);
            graph = await GlobalLocationPrivacyErasureTests.SeedErasureGraphAsync(seed);
        }
        await using (var migrations = new CoLocatedPrivacyErasureAuthorityDbContext(AuthorityOptions(PrimaryDatabaseRole.Migrator)))
            await migrations.Database.MigrateAsync();
        await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(AuthorityOptions(PrimaryDatabaseRole.Runtime));
        var retained = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
            authority, TimeProvider.System, Options.Create(new PrivacyErasureOptions()));
        var fact = await retained.AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, graph.OwnerUserId, PrivacyErasureReasonCode.AccountDeletion,
            new PrivacyErasureOptions().CurrentPolicyVersion));
        await using (var mirror = fixture.CreateDbContext())
        {
            mirror.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), fact.RecordedAtUtc.AddHours(1), fact.RecordedAtUtc));
            await mirror.SaveChangesAsync();
        }
        var forged = PrivacyErasureIntent.Record(
            changed == "intent" ? Guid.CreateVersion7() : fact.IntentId,
            changed == "sequence" ? fact.AuthoritySequence + 1 : fact.AuthoritySequence,
            fact.SubjectKind, changed == "subject" ? graph.UnrelatedUserId : fact.SubjectId,
            fact.ReasonCode, changed == "policy" ? fact.PolicyVersion + 1 : fact.PolicyVersion,
            fact.RequestedAtUtc, fact.RecordedAtUtc, fact.RetentionExpiresAtUtc);
        await using var logins = await OrdinaryLogins.CreateAsync(fixture);
        var factory = new GlobalExploreDbContextFactory(logins.GlobalOptions);
        await using var application = await factory.CreateDbContextAsync();
        var adapter = new PostgresSubjectPrivacyErasureSettlement(application);
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfCoreUnitOfWork(application).ExecuteSerializableAsync(
                token => adapter.PrepareAsync(forged, retained, token), CancellationToken.None));
        await Assert.That(rejected!.Message).IsEqualTo("subject_erasure_retained_authority_mismatch");
        var nativeRejected = await Assert.ThrowsAsync<PostgresException>(() =>
            new EfCoreUnitOfWork(application).ExecuteSerializableAsync(async token =>
            {
                await using var command = application.Database.GetDbConnection().CreateCommand();
                command.Transaction = application.Database.CurrentTransaction!.GetDbTransaction();
                string schema = application.GetService<ISqlGenerationHelper>().DelimitIdentifier(
                    application.Model.FindEntityType(typeof(Location))!.GetSchema()!);
                command.CommandText = $"SELECT {schema}.subject_erasure_prepare(@intent,@subject,@sequence,@policy,@reason,@recorded)";
                command.Parameters.Add(new NpgsqlParameter("intent", forged.IntentId));
                command.Parameters.Add(new NpgsqlParameter("subject", forged.SubjectId));
                command.Parameters.Add(new NpgsqlParameter("sequence", forged.AuthoritySequence));
                command.Parameters.Add(new NpgsqlParameter("policy", forged.PolicyVersion));
                command.Parameters.Add(new NpgsqlParameter("reason", (int)forged.ReasonCode));
                command.Parameters.Add(new NpgsqlParameter("recorded", forged.RecordedAtUtc));
                return await command.ExecuteScalarAsync(token);
            }, CancellationToken.None));
        await Assert.That(nativeRejected!.SqlState).IsEqualTo("42501");
        await using var persisted = fixture.CreateDbContext();
        await Assert.That(await persisted.UserPii.CountAsync(row => row.UserId == graph.OwnerUserId || row.UserId == graph.UnrelatedUserId)).IsEqualTo(2);
        await Assert.That(await persisted.Locations.IgnoreQueryFilters()
            .CountAsync(row => row.OwnerUserId == graph.OwnerUserId && row.PiiErasedAtUtc == null)).IsEqualTo(2);
        await Assert.That(await persisted.PrivacyErasureProviderWork.CountAsync()).IsEqualTo(0);
        await Assert.That(await persisted.PrivacyErasureReplayCheckpoints.CountAsync()).IsEqualTo(0);
        await AssertPrivateReadDeniedAsync(application);
    }

    [Test]
    public async Task DisposalCannotPrecedeProtectedProviderWork()
    {
        GlobalLocationPrivacyErasureTests.ErasureGraph graph;
        await using (var seed = fixture.CreateDbContext())
        {
            await PostgresSubjectPrivacyErasureContract.ApplyAsync(seed);
            graph = await GlobalLocationPrivacyErasureTests.SeedErasureGraphAsync(seed);
        }
        await using (var migrations = new CoLocatedPrivacyErasureAuthorityDbContext(AuthorityOptions(PrimaryDatabaseRole.Migrator)))
            await migrations.Database.MigrateAsync();
        await using var authority = new CoLocatedPrivacyErasureAuthorityDbContext(AuthorityOptions(PrimaryDatabaseRole.Runtime));
        var retained = new CoLocatedPostgresPrivacyErasureAuthorityRepository(
            authority, TimeProvider.System, Options.Create(new PrivacyErasureOptions()));
        var fact = await retained.AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, graph.OwnerUserId, PrivacyErasureReasonCode.AccountDeletion,
            new PrivacyErasureOptions().CurrentPolicyVersion));
        await using (var mirror = fixture.CreateDbContext())
        {
            mirror.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), fact.RecordedAtUtc.AddHours(1), fact.RecordedAtUtc));
            await mirror.SaveChangesAsync();
        }
        await using var logins = await OrdinaryLogins.CreateAsync(fixture);
        var factory = new GlobalExploreDbContextFactory(logins.GlobalOptions);
        await using var application = await factory.CreateDbContextAsync();
        var adapter = new PostgresSubjectPrivacyErasureSettlement(application);
        var failure = await Assert.ThrowsAsync<Exception>(() =>
            new EfCoreUnitOfWork(application).ExecuteSerializableAsync(async token =>
            {
                var candidates = await adapter.PrepareAsync(fact, retained, token);
                await Assert.That(candidates.Count).IsEqualTo(3);
                return await adapter.DisposeAsync(fact, DateTime.UtcNow, token);
            }, CancellationToken.None));
        // EF's non-retrying execution strategy wraps provider errors classified
        // as transient. The native authority rejection remains the root cause.
        var cause = failure!.GetBaseException();
        await Assert.That(cause).IsTypeOf<PostgresException>();
        var rejected = (PostgresException)cause;
        await Assert.That(rejected!.SqlState).IsEqualTo("55000");
        await Assert.That(rejected.MessageText).IsEqualTo("subject_erasure_protected_work_required");
        await using var persisted = fixture.CreateDbContext();
        await Assert.That(await persisted.UserExternalLogins.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(2);
        await Assert.That(await persisted.Locations.IgnoreQueryFilters()
            .CountAsync(row => row.OwnerUserId == graph.OwnerUserId && row.PiiErasedAtUtc == null)).IsEqualTo(2);
        await Assert.That(await persisted.PrivacyErasurePolicyCoverage.CountAsync()).IsEqualTo(0);
        await Assert.That(await persisted.OutboxMessages.CountAsync(row =>
            row.EventType == "LocationPiiErased" || row.EventType == "LocationPrivacyCorrectionRequested"
                || row.EventType == "PrivacyErasureCacheInvalidationRequested")).IsEqualTo(0);
    }

    private DbContextOptions<CoLocatedPrivacyErasureAuthorityDbContext> AuthorityOptions(PrimaryDatabaseRole role)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var builder = TestDbContextOptions.Create<CoLocatedPrivacyErasureAuthorityDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureCoLocatedPrivacyErasureAuthority(builder,
            new PrimaryDatabaseConnectionOptions
            {
                Role = role,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = connection.Username,
                Password = connection.Password,
                Schema = _authoritySchema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            });
        return builder.Options;
    }

    private async Task AssertRetainedAndApplicationAsync(
        CoLocatedPostgresPrivacyErasureAuthorityRepository retained, PrivacyErasureIntent fact,
        GlobalLocationPrivacyErasureTests.ErasureGraph graph, bool rolledBack,
        PrivacyErasureProviderLocatorProtector locatorProtector)
    {
        var facts = await retained.ReadAfterAsync(0, 10);
        await Assert.That(facts.Count).IsEqualTo(1);
        await Assert.That(facts[0].IntentId).IsEqualTo(fact.IntentId);
        await Assert.That(facts[0].SubjectId).IsEqualTo(graph.OwnerUserId);
        await Assert.That(await retained.ExecuteSerializedAsync(
            token => retained.IsSubjectFencedAsync(graph.OwnerUserId, token),
            CancellationToken.None)).IsTrue();
        await using var persisted = fixture.CreateDbContext();
        var homes = await persisted.Locations.IgnoreQueryFilters()
            .Where(row => graph.LocationIds.Contains(row.Id)).ToArrayAsync();
        await Assert.That(homes.Length).IsEqualTo(2);
        await Assert.That(homes.All(row => row.PiiErasedAtUtc.HasValue)).IsEqualTo(!rolledBack);
        await Assert.That(await persisted.Locations.IgnoreQueryFilters()
            .CountAsync(row => row.OwnerUserId == graph.UnrelatedUserId
                && row.PiiErasedAtUtc == null)).IsEqualTo(1);
        await Assert.That(await persisted.Users.IgnoreQueryFilters()
            .AnyAsync(row => row.Id == graph.UnrelatedUserId && !row.IsDeleted)).IsTrue();
        await Assert.That(await persisted.UserPreferences.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(rolledBack ? 2 : 0);
        await Assert.That(await persisted.UserPreferences.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
        await Assert.That(await persisted.UserExternalLogins.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(rolledBack ? 2 : 0);
        await Assert.That(await persisted.UserExternalLogins.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
        await Assert.That(await persisted.UserPii.IgnoreQueryFilters()
            .AnyAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(rolledBack);
        await Assert.That(await persisted.UserPii.IgnoreQueryFilters()
            .AnyAsync(row => row.UserId == graph.UnrelatedUserId)).IsTrue();
        await Assert.That(await persisted.PrivacyErasureReplayCheckpoints.CountAsync())
            .IsEqualTo(rolledBack ? 0 : 1);
        await Assert.That(await persisted.PrivacyErasurePolicyCoverage
            .CountAsync(row => row.IntentId == fact.IntentId)).IsEqualTo(rolledBack ? 0 : 1);
        var saga = await persisted.PrivacyErasureSagas.SingleAsync(row => row.IntentId == fact.IntentId);
        await Assert.That(saga.LocalSettledAtUtc.HasValue).IsEqualTo(!rolledBack);
        await Assert.That(await persisted.OutboxMessages.CountAsync(row =>
            row.EventType == "LocationPiiErased" || row.EventType == "LocationPrivacyCorrectionRequested"))
            .IsEqualTo(rolledBack ? 0 : 4);
        await Assert.That(await persisted.OutboxMessages.CountAsync(row =>
            row.EventType == "PrivacyErasureCacheInvalidationRequested")).IsEqualTo(rolledBack ? 0 : 1);
        await Assert.That(await persisted.TenantUsers.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(rolledBack ? 2 : 0);
        await Assert.That(await persisted.TenantUsers.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
        await Assert.That(await persisted.UserAuthenticationTokens.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(rolledBack ? 2 : 0);
        await Assert.That(await persisted.UserAuthenticationTokens.IgnoreQueryFilters()
            .CountAsync(row => row.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
        await Assert.That(await persisted.UserIdentityEmailEvidence
            .CountAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(rolledBack ? 2 : 0);
        await Assert.That(await persisted.UserIdentityEmailClaims
            .CountAsync(row => row.UserId == graph.OwnerUserId)).IsEqualTo(rolledBack ? 1 : 0);
        await Assert.That(await persisted.UserIdentityEmailEvidence
            .CountAsync(row => row.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
        await Assert.That(await persisted.Users.IgnoreQueryFilters()
            .AnyAsync(row => row.Id == graph.OwnerUserId && row.IsDeleted)).IsEqualTo(!rolledBack);
        Actor actor = await persisted.Actors.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.OwnerActorId);
        await Assert.That(actor.UserId).IsEqualTo(rolledBack ? graph.OwnerUserId : (Guid?)null);
        await Assert.That(actor.IsDeleted).IsEqualTo(!rolledBack);
        var actorPii = await persisted.ActorPii.IgnoreQueryFilters().SingleAsync(row => row.ActorId == graph.OwnerActorId);
        await Assert.That(actorPii.DisplayName).IsEqualTo(rolledBack ? "ACTOR-NAME-CANARY" : "Deleted user");
        var identity = await persisted.AtprotoIdentities.IgnoreQueryFilters()
            .SingleAsync(row => row.ActorId == graph.OwnerActorId);
        await Assert.That(identity.Did).IsEqualTo(rolledBack ? "did:plc:actor-canary" : $"did:deleted:{identity.Id:N}");
        await Assert.That(identity.IsDeleted).IsEqualTo(!rolledBack);
        await Assert.That(await persisted.AtprotoIdentities.IgnoreQueryFilters()
            .AnyAsync(row => row.ActorId == graph.UnrelatedActorId && row.Did == "did:plc:unrelated" && !row.IsDeleted)).IsTrue();
        var rooms = await persisted.LocationRooms.IgnoreQueryFilters()
            .Where(row => graph.RoomIds.Contains(row.Id)).ToArrayAsync();
        await Assert.That(rooms.Length).IsEqualTo(2);
        await Assert.That(rooms.All(row => row.Name == $"privacy-erased-{row.Id:N}" && row.Description == null && row.IsDeleted))
            .IsEqualTo(!rolledBack);
        var locations = await persisted.EventLocations.IgnoreQueryFilters()
            .Where(row => graph.EventLocationIds.Contains(row.Id)).ToArrayAsync();
        await Assert.That(locations.Length).IsEqualTo(2);
        await Assert.That(locations.All(row => row.PolicyVersion == (rolledBack ? 2 : 3))).IsTrue();
        if (!rolledBack)
        {
            await Assert.That(homes.All(row => row.Pii == null && row.OwnerUserId == null
                && row.FullName == "Private venue" && row.City == string.Empty)).IsTrue();
            await Assert.That(locations.All(row => row.NeedsPrivacyReview
                && row.FullDetailsAudienceId == (int)LocationDisclosureAudienceEnum.Never
                && !row.ShowVenueName && !row.ShowCity && !row.ShowCountry && !row.ShowRoomName
                && !row.ShowStreetAddress && !row.ShowPostcode && !row.ShowCoordinates)).IsTrue();
        }
        var audits = await persisted.EventLocationDisclosureAudits.IgnoreQueryFilters()
            .Where(row => row.Reason == EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation).ToArrayAsync();
        await Assert.That(audits.Length).IsEqualTo(rolledBack ? 0 : 2);
        await Assert.That(audits.All(row => row.PreviousPolicyVersion == 2 && row.NewPolicyVersion == 3
            && row.ActorUserId == graph.OwnerUserId && row.NewFields == EventLocationDisclosureFields.None)).IsTrue();
        await Assert.That((await persisted.EventLocationDisclosureAudits.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == graph.SubjectDisclosureAuditId)).ActorUserId).IsEqualTo(graph.OwnerUserId);
        await Assert.That((await persisted.EventLocationExactReadAudits.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == graph.SubjectExactReadAuditId)).RequesterUserId).IsEqualTo(graph.OwnerUserId);
        var work = await persisted.PrivacyErasureProviderWork.Where(row => row.IntentId == fact.IntentId).ToArrayAsync();
        await Assert.That(work.Length).IsEqualTo(rolledBack ? 0 : 3);
        if (!rolledBack)
        {
            await Assert.That(work.Count(row => row.ProviderKind == PrivacyErasureProviderKind.Keycloak)).IsEqualTo(2);
            await Assert.That(work.Count(row => row.ProviderKind == PrivacyErasureProviderKind.Atproto)).IsEqualTo(1);
            await Assert.That(work.All(row => row.SubjectId == graph.OwnerUserId && row.ProtectedLocator != null
                && row.LocatorProtectionVersion == 1 && row.Status == PrivacyErasureProviderWorkStatus.Pending
                && row.LocatorExpiresAtUtc - row.CreatedAtUtc == new PrivacyErasureOptions().ProviderLocatorLifetime)).IsTrue();
            await Assert.That(saga.ProviderWorkCount).IsEqualTo(3);
            await Assert.That(saga.Status).IsEqualTo(PrivacyErasureSagaStatus.ProviderPending);
            await Assert.That(work.Select(row => locatorProtector.Unprotect(row.ProtectedLocator!)))
                .IsEquivalentTo(["owner-a", "owner-b", "did:plc:actor-canary"]);
        }
        string payloads = string.Join('|', await persisted.OutboxMessages.Select(row => row.Payload).ToArrayAsync());
        foreach (string canary in graph.PiiCanaries.Concat(["owner-a", "owner-b", "did:plc:actor-canary"]))
        {
            await Assert.That(payloads).DoesNotContain(canary);
            foreach (var item in work)
                await Assert.That(item.ProtectedLocator!).IsNotEqualTo(canary);
        }
    }

    private static GlobalLocationPrivacyErasureTests.ErasureRuntime CreateRuntime(
        ExploreDbContext context, IPrivacyErasureAuthority authority, PrivacyErasureProviderLocatorProtector protector)
        => GlobalLocationPrivacyErasureTests.CreateRuntime(context, authority, protector: protector);

    private static async Task<StorageGraph> SeedStorageAsync(
        ExploreDbContext seed, GlobalLocationPrivacyErasureTests.ErasureGraph graph, Guid tenantA)
    {
        Guid tenantB = await seed.Locations.IgnoreQueryFilters()
            .Where(row => row.Id == graph.LocationIds[1]).Select(row => row.TenantId).SingleAsync();
        var binding = StorageProviderBinding.Local(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "agent-tmp", "erasure-fixture"));
        StorageObject first = Source(tenantA, 512, "owned-a");
        StorageObject second = Source(tenantB, 256, "owned-b");
        StorageObject shared = Source(tenantA, 128, "shared-foreign-picture");
        seed.AddRange(binding, first, second, shared);
        await seed.SaveChangesAsync();
        ActorPii ownerPicture = await seed.ActorPii.SingleAsync(row => row.ActorId == graph.OwnerActorId);
        ActorPii foreignPicture = await seed.ActorPii.SingleAsync(row => row.ActorId == graph.UnrelatedActorId);
        ownerPicture.SetProfilePicture(first.Id, null);
        foreignPicture.SetProfilePicture(shared.Id, null);
        var pending = new StorageUploadSession
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantB,
            UserId = graph.OwnerUserId,
            Provider = StorageProviders.Local,
            StorageProviderBindingId = binding.Id,
            ObjectKey = "erasure/pending-producer",
            ProviderVersionId = "version-pending",
            ContentType = "image/png",
            SafeDisplayName = "Pending private image",
            Purpose = StorageObjectPurposes.ProfileImage,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Status = StorageUploadSessionStates.Uploading,
            ExpectedSizeBytes = 64,
            ReservedBytes = 64,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UploadStartedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        seed.Add(pending);
        await seed.SaveChangesAsync();
        await new EfCoreUnitOfWork(seed).ExecuteSerializableAsync(async token =>
        {
            var counters = new StorageUsageCounterRepository(seed);
            await counters.RecalculateScopeAsync(tenantA, StorageProviders.Local, DateTime.UtcNow, token);
            await counters.RecalculateScopeAsync(tenantB, StorageProviders.Local, DateTime.UtcNow, token);
            await seed.SaveChangesAsync(token);
            return true;
        }, CancellationToken.None);
        return new(first.Id, second.Id, shared.Id, pending.Id, binding.Id, tenantA, tenantB);

        StorageObject Source(Guid tenantId, long size, string key) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            ActorId = graph.OwnerActorId,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Provider = StorageProviders.Local,
            StorageProviderBindingId = binding.Id,
            ObjectKey = "erasure/" + key,
            ProviderVersionId = "version-" + key,
            FullName = key,
            SafeDisplayName = key,
            Extension = ".png",
            ContentType = "image/png",
            Size = size,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.ProfileImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private async Task AssertStorageAsync(
        StorageGraph storage, GlobalLocationPrivacyErasureTests.ErasureGraph graph, bool rolledBack)
    {
        await using var persisted = fixture.CreateDbContext();
        await Assert.That(await persisted.StorageObjects.IgnoreQueryFilters().CountAsync(row =>
            row.Id == storage.First || row.Id == storage.Second)).IsEqualTo(rolledBack ? 2 : 0);
        var shared = await persisted.StorageObjects.IgnoreQueryFilters().SingleAsync(row => row.Id == storage.Shared);
        await Assert.That(shared.ActorId).IsEqualTo(rolledBack ? graph.OwnerActorId : (Guid?)null);
        await Assert.That(shared.ObjectKey).IsEqualTo("erasure/shared-foreign-picture");
        await Assert.That((await persisted.ActorPii.SingleAsync(row => row.ActorId == graph.UnrelatedActorId))
            .ProfilePictureStorageObjectId).IsEqualTo(storage.Shared);
        await Assert.That(await persisted.StorageUploadSessions.IgnoreQueryFilters()
            .CountAsync(row => row.Id == storage.Pending)).IsEqualTo(rolledBack ? 1 : 0);
        var targets = await persisted.StorageObjectDeletionTombstones.ToArrayAsync();
        await Assert.That(targets.Length).IsEqualTo(rolledBack ? 0 : 3);
        if (!rolledBack)
        {
            await Assert.That(targets.All(row => row.ProviderBindingId == storage.Binding && row.Provider == StorageProviders.Local)).IsTrue();
            await Assert.That(targets.Single(row => row.Id == storage.First).ObjectKey).IsEqualTo("erasure/owned-a");
            await Assert.That(targets.Single(row => row.Id == storage.Second).ProviderObjectVersion).IsEqualTo("version-owned-b");
            await Assert.That(targets.Count(row => row.State == StorageObjectDeletionState.Ready)).IsEqualTo(2);
            var pending = targets.Single(row => row.Id == storage.Pending);
            await Assert.That(pending.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
            await Assert.That(pending.ProviderObjectVersion).IsEqualTo("version-pending");
            await Assert.That(pending.NextAttemptAtUtc).IsNull();
            await Assert.That(targets.Any(row => row.Id == storage.Shared)).IsFalse();
        }
        var first = await persisted.StorageUsageCounters.IgnoreQueryFilters().SingleAsync(row => row.TenantId == storage.TenantA);
        var second = await persisted.StorageUsageCounters.IgnoreQueryFilters().SingleAsync(row => row.TenantId == storage.TenantB);
        await Assert.That(first.UsedBytes).IsEqualTo(rolledBack ? 640L : 128L);
        await Assert.That(first.ObjectCount).IsEqualTo(rolledBack ? 2L : 1L);
        await Assert.That(second.UsedBytes).IsEqualTo(rolledBack ? 256L : 0L);
        await Assert.That(second.ObjectCount).IsEqualTo(rolledBack ? 1L : 0L);
        await Assert.That(second.ReservedBytes).IsEqualTo(rolledBack ? 64L : 0L);
    }

    private sealed record StorageGraph(Guid First, Guid Second, Guid Shared, Guid Pending, Guid Binding, Guid TenantA, Guid TenantB);

    private static async Task AssertOrdinaryAsync(ExploreDbContext context, string expectedLogin)
    {
        await context.Database.OpenConnectionAsync();
        await using var probe = context.Database.GetDbConnection().CreateCommand();
        probe.CommandText = """
            SELECT current_user, rolsuper, rolbypassrls, rolcreaterole
            FROM pg_catalog.pg_roles WHERE rolname = current_user
            """;
        await using var reader = await probe.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(reader.GetString(0)).IsEqualTo(expectedLogin);
        await Assert.That(reader.GetBoolean(1)).IsFalse();
        await Assert.That(reader.GetBoolean(2)).IsFalse();
        await Assert.That(reader.GetBoolean(3)).IsFalse();
    }

    private static async Task AssertPrivateReadDeniedAsync(ExploreDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Locations.IgnoreQueryFilters().CountAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        foreach (Type type in new[] { typeof(LocationPii), typeof(UserPii), typeof(Actor),
            typeof(AtprotoIdentity), typeof(UserExternalLogin), typeof(UserAuthenticationToken),
            typeof(StorageObject), typeof(StorageUploadSession) })
        {
            var entity = context.Model.FindEntityType(type)!;
            string table = context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            await using var probe = context.Database.GetDbConnection().CreateCommand();
            probe.CommandText = "SELECT has_table_privilege(current_user, @table, 'SELECT,INSERT,UPDATE,DELETE')";
            probe.Parameters.Add(new NpgsqlParameter("table", table));
            await Assert.That((bool)(await probe.ExecuteScalarAsync())!).IsFalse();
        }
    }

    private sealed class OrdinaryLogins(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        private readonly string _global = $"erasure_global_{Guid.CreateVersion7():N}";
        private readonly string _runtime = $"erasure_runtime_{Guid.CreateVersion7():N}";
        private string[] _mirrorTables = [];
        private string[] _requestTables = [];
        private string _schema = null!;
        internal PrimaryDatabaseConnectionOptions GlobalOptions { get; private set; } = null!;
        internal PrimaryDatabaseConnectionOptions RuntimeOptions { get; private set; } = null!;

        internal static async Task<OrdinaryLogins> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var logins = new OrdinaryLogins(fixture);
            await using var metadata = fixture.CreateDbContext();
            var sql = metadata.GetService<ISqlGenerationHelper>();
            logins._schema = metadata.Model.FindEntityType(typeof(Location))!.GetSchema()!;
            logins._mirrorTables = new[] { typeof(PrivacyErasureSaga), typeof(PrivacyErasurePolicyCoverage),
                typeof(PrivacyErasureReplayCheckpoint), typeof(PrivacyErasureProviderWork), typeof(OutboxMessage) }
                .Select(Table).ToArray();
            logins._requestTables = new[] { typeof(Location), typeof(LocationPii) }.Select(Table).ToArray();
            string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grant = administration.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{logins._global}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalPassword}';
                CREATE ROLE "{logins._runtime}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{runtimePassword}';
                GRANT USAGE ON SCHEMA "{logins._schema}" TO "{logins._global}", "{logins._runtime}";
                GRANT {PostgresSubjectPrivacyErasureContract.GlobalRole} TO "{logins._global}";
                GRANT SELECT, INSERT, UPDATE ON {string.Join(", ", logins._mirrorTables)} TO "{logins._global}";
                GRANT SELECT ON {string.Join(", ", logins._requestTables)} TO "{logins._runtime}";
                """;
            await grant.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
            logins.GlobalOptions = Options(PrimaryDatabaseRole.Global, logins._global, globalPassword);
            logins.RuntimeOptions = Options(PrimaryDatabaseRole.Runtime, logins._runtime, runtimePassword);
            return logins;

            string Table(Type type)
            {
                var entity = metadata.Model.FindEntityType(type)!;
                return sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            }

            PrimaryDatabaseConnectionOptions Options(PrimaryDatabaseRole role, string login, string password) => new()
            {
                Role = role,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = login,
                Password = password,
                Schema = logins._schema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
        }

        public async ValueTask DisposeAsync()
        {
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", _mirrorTables)} FROM "{_global}";
                REVOKE {PostgresSubjectPrivacyErasureContract.GlobalRole} FROM "{_global}";
                REVOKE ALL ON {string.Join(", ", _requestTables)} FROM "{_runtime}";
                REVOKE USAGE ON SCHEMA "{_schema}" FROM "{_global}", "{_runtime}";
                DROP ROLE "{_global}", "{_runtime}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }
    }
}
