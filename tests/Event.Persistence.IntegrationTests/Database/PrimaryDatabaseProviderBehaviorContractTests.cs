using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Projections;
using Explore.Persistence.Queries;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

[RequiresStructuredPrimaryDatabase]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class PrimaryDatabaseRuntimeSmokeTests
{
    [Test]
    public async Task StructuredRuntimeCredentialsReachBothMigratedSchemas()
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await using var applicationContext = fixture.CreateSystemContext();
        await using var dataProtectionContext = fixture.CreateDataProtectionContext();

        await Assert.That(await applicationContext.Database.CanConnectAsync()).IsTrue();
        await applicationContext.SystemSettings.AsNoTracking().Take(1).ToListAsync();
        await dataProtectionContext.DataProtectionKeys.AsNoTracking().Take(1).ToListAsync();
    }
}

[RequiresStructuredPrimaryDatabase]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class PrimaryDatabaseProviderBehaviorContractTests
{
    [Test]
    public Task MigratedProviderSupportsSharedPersistenceBehavior()
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        return AssertSharedPersistenceBehaviorAsync(fixture);
    }

    internal static async Task AssertSharedPersistenceBehaviorAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        var scope = await SeedTenantGraphAsync(fixture);

        await VerifyProjectionLockContentionAsync(fixture);
        await VerifyCrudPagingTenantIsolationAndSoftDeleteAsync(fixture, scope);
        await VerifyOptimisticConcurrencyAsync(fixture, scope);
        await VerifyTransactionsOutboxAndIdempotentReplayAsync(fixture);
    }

    [Test]
    public Task DataProtectionKeyRingSurvivesProviderRecreation()
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        return AssertDataProtectionKeyRingSurvivesProviderRecreationAsync(fixture);
    }

    internal static async Task AssertDataProtectionKeyRingSurvivesProviderRecreationAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        var applicationName = $"islamu-event-provider-contract-{Guid.CreateVersion7():N}";
        const string purpose = "provider-restart";
        const string payload = "authenticated-session-ticket";

        string protectedPayload;
        await using (var firstProvider = fixture.BuildDataProtectionProvider(applicationName))
        {
            protectedPayload = firstProvider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(purpose)
                .Protect(payload);

            await using var scope = firstProvider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>();
            await Assert.That(await context.DataProtectionKeys.CountAsync()).IsGreaterThanOrEqualTo(1);
        }

        await using (var restartedProvider = fixture.BuildDataProtectionProvider(applicationName))
        {
            var restored = restartedProvider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(purpose)
                .Unprotect(protectedPayload);

            await Assert.That(restored).IsEqualTo(payload);
        }
    }

    [Test]
    public Task MigratedProviderExecutesUnicodeAddressSuggestionContract()
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        return AssertUnicodeAddressSuggestionContractAsync(fixture);
    }

    internal static async Task AssertUnicodeAddressSuggestionContractAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid prefixId = Guid.CreateVersion7();
        Guid longerPrefixId = Guid.CreateVersion7();
        Guid bmpId = Guid.CreateVersion7();
        Guid tieFirstId = Guid.CreateVersion7();
        Guid tieSecondId = Guid.CreateVersion7();
        Guid boundaryCanaryId = Guid.CreateVersion7();
        Guid boundaryExactId = Guid.CreateVersion7();
        Guid legacyId = Guid.CreateVersion7();
        Guid maximumId = Guid.CreateVersion7();
        Guid expansionId = Guid.CreateVersion7();
        Guid suffixId = Guid.CreateVersion7();
        Guid arabicId = Guid.CreateVersion7();
        Guid greekId = Guid.CreateVersion7();
        Guid supplementaryId = Guid.CreateVersion7();
        Guid literalId = Guid.CreateVersion7();
        Guid wildcardCanaryId = Guid.CreateVersion7();
        Guid sharpSId = Guid.CreateVersion7();
        Guid dottedIId = Guid.CreateVersion7();
        Guid emojiId = Guid.CreateVersion7();
        Guid trailingSpaceId = Guid.CreateVersion7();

        var tenant = NewTenant($"unicode-{tenantId:N}");
        tenant.Id = tenantId;
        Location[] locations =
        [
            AddressLocation(prefixId, tenantId, actorId, "A", "Café 😀 %_\\ North"),
            AddressLocation(longerPrefixId, tenantId, actorId, "AA", "Cafe\u0301 😀 %_\\ North"),
            AddressLocation(bmpId, tenantId, actorId, "\uE000", "CAFÉ 😀 %_\\ NORTH"),
            AddressLocation(tieFirstId, tenantId, actorId, "Tie", "Café tie corpus"),
            AddressLocation(tieSecondId, tenantId, actorId, "tie", "CAFE\u0301 tie corpus"),
            AddressLocation(boundaryCanaryId, tenantId, actorId, "Boundary A", "AB"),
            AddressLocation(boundaryExactId, tenantId, actorId, "Boundary B", char.ConvertFromUtf32(0x100004) + " exact"),
            AddressLocation(legacyId, tenantId, actorId, "Legacy", "Legacy café address", tenantApproved: false),
            AddressLocation(maximumId, tenantId, actorId, new string('\uE000', 500), new string('\uE000', 500)),
            AddressLocation(expansionId, tenantId, actorId, "Expanded", new string('\u0344', 500)),
            AddressLocation(suffixId, tenantId, actorId, "Suffix", new string('a', 493) + " suffix"),
            AddressLocation(arabicId, tenantId, actorId, "Arabic", "مَسْجِد\u200C\u200D عناوين"),
            AddressLocation(greekId, tenantId, actorId, "Greek", "ςσ οδός"),
            AddressLocation(supplementaryId, tenantId, actorId, "😀", "\U00010428 road Café 😀 %_\\ North"),
            AddressLocation(literalId, tenantId, actorId, "Literals", "bracket [x] backslash \\ path"),
            AddressLocation(wildcardCanaryId, tenantId, actorId, "Canary", "bracket x backslash / path"),
            AddressLocation(sharpSId, tenantId, actorId, "German", "Straße"),
            AddressLocation(dottedIId, tenantId, actorId, "Turkish", "İstanbul"),
            AddressLocation(emojiId, tenantId, actorId, "Emoji", "👩🏽\u200D💻\uFE0F road"),
            AddressLocation(trailingSpaceId, tenantId, actorId, "Tie ", "Café tie corpus ")
        ];
        Location legacy = locations.Single(location => location.Id == legacyId);

        await using (var seed = fixture.CreateSystemContext())
        {
            seed.Add(tenant);
            seed.AddRange(locations);
            await seed.SaveChangesAsync();
        }
        Location maximum = locations.Single(location => location.Id == maximumId);
        await Assert.That(maximum.DisplaySortKey.Length).IsEqualTo(500);
        await Assert.That(maximum.Pii!.AddressSubstringKey.Length).IsEqualTo(500);

        var capture = new SelectCaptureInterceptor();
        await using var context = fixture.CreateTenantContext(tenantId, capture);
        var query = new LocalAddressSuggestionQuery(context);
        async Task<Guid[]> Search(string text, int limit = 20) => (await query.SearchAsync(
            new LocalAddressSuggestionCriteria(tenantId, actorId, userId, null, text, limit),
            CancellationToken.None)).Select(result => result.LocationId).ToArray();

        Guid[] composed = await Search("café 😀");
        await capture.RecordQueryPlanAsync(context, fixture.Provider, composed.Length);
        Guid[] expectedProviderOrder = fixture.Provider == PrimaryDatabaseProvider.SqlServer
            ? [prefixId, longerPrefixId, supplementaryId, bmpId]
            : [prefixId, longerPrefixId, bmpId, supplementaryId];
        await Assert.That(composed).IsEquivalentTo(expectedProviderOrder, CollectionOrdering.Matching);
        await Assert.That(await Search("café 😀", 2))
            .IsEquivalentTo(expectedProviderOrder.Take(2), CollectionOrdering.Matching);
        await Assert.That(await Search("CAFE\u0301 😀")).IsEquivalentTo(composed, CollectionOrdering.Matching);
        await Assert.That(await Search("%_")).IsEquivalentTo(composed, CollectionOrdering.Matching);
        await Assert.That(await Search("😀 %")).IsEquivalentTo(composed, CollectionOrdering.Matching);
        await Assert.That(await Search(char.ConvertFromUtf32(0x100004)))
            .IsEquivalentTo([boundaryExactId], CollectionOrdering.Matching);
        Guid[] ties = await Search("café tie");
        await Assert.That(ties).IsEquivalentTo([tieFirstId, tieSecondId, trailingSpaceId]);
        await Assert.That(await Search("café tie")).IsEquivalentTo(ties, CollectionOrdering.Matching);
        await Assert.That(await Search("café tie", 2)).IsEquivalentTo(ties.Take(2), CollectionOrdering.Matching);
        await Assert.That(await Search("suffix")).IsEquivalentTo([suffixId]);
        await Assert.That(await Search("\u0344\u0344")).IsEquivalentTo([expansionId]);
        await Assert.That(await Search("مَسْجِد\u200C\u200D")).IsEquivalentTo([arabicId]);
        await Assert.That(await Search("مسجد")).IsEmpty();
        await Assert.That(await Search("ΣΣ")).IsEquivalentTo([greekId]);
        await Assert.That(await Search("\U00010400")).IsEquivalentTo([supplementaryId]);
        await Assert.That(await Search("[x]")).IsEquivalentTo([literalId]);
        await Assert.That(await Search("\\ path")).IsEquivalentTo([literalId]);
        await Assert.That(await Search("straße")).IsEquivalentTo([sharpSId]);
        await Assert.That(await Search("STRASSE")).IsEmpty();
        await Assert.That(await Search("İSTANBUL")).IsEquivalentTo([dottedIId]);
        await Assert.That(await Search("ISTANBUL")).IsEmpty();
        await Assert.That(await Search("👩🏽\u200D💻\uFE0F")).IsEquivalentTo([emojiId]);
        await Assert.That(await Search(new string('\uE000', 2))).IsEquivalentTo([maximumId]);
        await Assert.That(await Search("CAFE ")).IsEmpty();

        foreach (string invalid in new[] { "a\0b", "a\uD800b", "a\uFDD0b", "a\U0010FFFFb" })
        {
            int commandsBefore = capture.Commands.Count;
            await Assert.That(() => Search(invalid)).Throws<ArgumentException>();
            await Assert.That(capture.Commands.Count).IsEqualTo(commandsBefore);
        }

        await SetRevisionChecksEnabledAsync(context, enabled: false);
        await context.Locations.Where(location => location.Id == legacyId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(location => location.DisplaySortKeyVersion, (short)999));
        await context.LocationPii.Where(pii => pii.LocationId == legacyId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(pii => pii.AddressSubstringKeyVersion, (short)999));
        await Assert.That(await Search("legacy café")).IsEmpty();

        legacy = await context.Locations.SingleAsync(location => location.Id == legacyId);
        legacy.PromoteAddressToTenantApproved(actorId, DateTime.UnixEpoch.AddDays(10));
        await context.SaveChangesAsync();
        await SetRevisionChecksEnabledAsync(context, enabled: true);
        await Assert.That(await Search("legacy café"))
            .IsEquivalentTo([legacyId], CollectionOrdering.Matching);

        string[] suggestionCommands = capture.Commands.Where(command =>
                command.Contains("address_substring_key", StringComparison.OrdinalIgnoreCase) &&
                command.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (string sql in suggestionCommands)
        {
            int from = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
            await Assert.That(from).IsGreaterThan(0);
            string projection = sql[..from].ToLowerInvariant();
            await Assert.That(projection).DoesNotContain("address_substring_key");
            await Assert.That(projection).DoesNotContain("display_sort_key");
        }
        await Assert.That(suggestionCommands).IsNotEmpty();
        await AssertErasedAddressCannotBeRecreatedAsync(fixture, tenantId, actorId);
        await Repositories.LocationUnicodeWriteAtomicityTests.AssertRejectedWritesAsync(fixture, tenantId, actorId);
        await Repositories.LocalAddressSuggestionQueryTests.AssertProviderAuthorityMatrixAsync(fixture);
    }

    private static async Task AssertErasedAddressCannotBeRecreatedAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture, Guid tenantId, Guid actorId)
    {
        Guid ownerId = Guid.CreateVersion7();
        Location home = AddressLocation(Guid.CreateVersion7(), tenantId, actorId,
            "Private café", "Erasure café sentinel", tenantApproved: false);
        home.ClassifyAsPrivateHome(ownerId);
        await using (ExploreDbContext seed = fixture.CreateSystemContext())
        {
            seed.Users.Add(new User { Id = ownerId, Pii = null!, CreatedAt = DateTime.UnixEpoch, ConcurrencyStamp = Guid.CreateVersion7() });
            seed.Locations.Add(home);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext staleContext = fixture.CreateTenantContext(tenantId);
        var staleRepository = new LocationRepository(staleContext);
        Location stale = (await staleRepository.GetById(home.Id, CancellationToken.None))!;
        DateTime erasedAt = new(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);
        await using (ExploreDbContext eraser = fixture.CreateTenantContext(tenantId))
        {
            Location current = await eraser.Locations.SingleAsync(location => location.Id == home.Id);
            current.EraseOwnedPii(erasedAt, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
            await eraser.SaveChangesAsync();
        }

        stale.SetManualAddress("Resurrection café sentinel", "2000");
        await Assert.That(() => staleRepository.Update(stale, CancellationToken.None))
            .Throws<Explore.Application.Exceptions.ConcurrencyConflictException>();

        await using ExploreDbContext verify = fixture.CreateTenantContext(tenantId);
        var repository = new LocationRepository(verify);
        Location erased = (await repository.GetById(home.Id, CancellationToken.None))!;
        erased.EraseOwnedPii(erasedAt, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        await Assert.That(() => erased.PromoteAddressToTenantApproved(actorId, erasedAt))
            .Throws<InvalidOperationException>();
        await Assert.That(() => erased.SetManualAddress("Erasure café sentinel", "1000"))
            .Throws<InvalidOperationException>();
        await Assert.That(erased.Pii).IsNull();
        await Assert.That(erased.DisplaySortKey).IsEqualTo("PRIVATE VENUE");
        await Assert.That(await new LocalAddressSuggestionQuery(verify).SearchAsync(
            new LocalAddressSuggestionCriteria(tenantId, actorId, ownerId, null, "sentinel", 20), CancellationToken.None))
            .IsEmpty();
    }

    private static async Task SetRevisionChecksEnabledAsync(ExploreDbContext context, bool enabled)
    {
        await context.Database.OpenConnectionAsync();
        if (context.Database.IsSqlite())
        {
            await context.Database.ExecuteSqlRawAsync(enabled
                ? "PRAGMA ignore_check_constraints = OFF"
                : "PRAGMA ignore_check_constraints = ON");
            return;
        }

        var model = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var sql = context.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>();
        foreach (Type entity in new[] { typeof(Location), typeof(LocationPii) })
        {
            var type = model.FindEntityType(entity)!;
            var check = type.GetCheckConstraints().Single(constraint =>
                constraint.Name is "ck_locations_display_sort_key_version" or "ck_location_pii_address_substring_key_version");
            string table = sql.DelimitIdentifier(type.GetTableName()!, type.GetSchema());
            string name = sql.DelimitIdentifier(check.Name);
            bool mariaDb = context.Database.IsMySql()
                && context.Database.GetDbConnection().ServerVersion.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
            string operation = enabled ? $"ADD CONSTRAINT {name} CHECK ({check.Sql})"
                : context.Database.IsMySql() && !mariaDb ? $"DROP CHECK {name}" : $"DROP CONSTRAINT {name}";
            await context.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} {operation}");
        }
    }

    private static async Task VerifyProjectionLockContentionAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await using var owner = fixture.CreateSystemContext();
        await using var contender = fixture.CreateSystemContext();
        await using var transaction = await owner.Database.BeginTransactionAsync();
        Guid tenantId = Guid.CreateVersion7();

        bool acquired = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
            owner,
            projectionLockKey: 82001,
            tenantId,
            exclusive: true,
            CancellationToken.None);
        bool blocked = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
            contender,
            projectionLockKey: 82001,
            tenantId,
            exclusive: false,
            CancellationToken.None);

        await Assert.That(acquired).IsTrue();
        await Assert.That(blocked).IsFalse();
        await transaction.RollbackAsync();

        bool acquiredAfterRelease = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
            contender,
            projectionLockKey: 82001,
            tenantId,
            exclusive: false,
            CancellationToken.None);
        await Assert.That(acquiredAfterRelease).IsTrue();
    }

    private static async Task<ProviderScope> SeedTenantGraphAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await using var context = fixture.CreateSystemContext();
        var suffix = Guid.CreateVersion7().ToString("N");
        var tenantA = NewTenant($"provider-a-{suffix}");
        var tenantB = NewTenant($"provider-b-{suffix}");
        var locationA = NewLocation(tenantA, "Brussels");
        var locationB = NewLocation(tenantB, "Antwerp");
        var roomA1 = NewRoom(tenantA, locationA, "Room 10", 10);
        var roomA2 = NewRoom(tenantA, locationA, "Room 20", 20);
        var roomA3 = NewRoom(tenantA, locationA, "Room 30", 30);
        var roomB = NewRoom(tenantB, locationB, "Other Tenant Room", 10);

        context.AddRange(tenantA, tenantB, locationA, locationB, roomA1, roomA2, roomA3, roomB);
        await context.SaveChangesAsync();

        return new ProviderScope(
            tenantA.Id,
            tenantB.Id,
            roomA1.Id,
            roomA2.Id,
            roomA3.Id,
            roomB.Id);
    }

    private static async Task VerifyCrudPagingTenantIsolationAndSoftDeleteAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture,
        ProviderScope scope)
    {
        await using (var tenantAContext = fixture.CreateTenantContext(scope.TenantAId))
        {
            var firstPage = await tenantAContext.LocationRooms
                .AsNoTracking()
                .OrderBy(room => room.SortOrder)
                .ThenBy(room => room.Id)
                .Take(2)
                .Select(room => room.Id)
                .ToListAsync();

            await Assert.That(firstPage).Count().IsEqualTo(2);
            await Assert.That(firstPage[0]).IsEqualTo(scope.RoomA1Id);
            await Assert.That(firstPage[1]).IsEqualTo(scope.RoomA2Id);

            var room = await tenantAContext.LocationRooms.SingleAsync(candidate => candidate.Id == scope.RoomA1Id);
            room.Name = "Updated Room";
            await tenantAContext.SaveChangesAsync();

            var deletedRoom = await tenantAContext.LocationRooms.SingleAsync(candidate => candidate.Id == scope.RoomA2Id);
            tenantAContext.Remove(deletedRoom);
            await tenantAContext.SaveChangesAsync();
        }

        await using (var tenantAContext = fixture.CreateTenantContext(scope.TenantAId))
        {
            var visible = await tenantAContext.LocationRooms
                .AsNoTracking()
                .OrderBy(room => room.SortOrder)
                .Select(room => new { room.Id, room.Name })
                .ToListAsync();
            var includingDeleted = await tenantAContext.LocationRooms
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .AsNoTracking()
                .Where(room => room.Id == scope.RoomA2Id)
                .SingleAsync();

            await Assert.That(visible).Count().IsEqualTo(2);
            await Assert.That(visible[0].Id).IsEqualTo(scope.RoomA1Id);
            await Assert.That(visible[1].Id).IsEqualTo(scope.RoomA3Id);
            await Assert.That(visible[0].Name).IsEqualTo("Updated Room");
            await Assert.That(includingDeleted.IsDeleted).IsTrue();
        }

        await using (var tenantBContext = fixture.CreateTenantContext(scope.TenantBId))
        {
            var visible = await tenantBContext.LocationRooms.AsNoTracking().Select(room => room.Id).ToListAsync();
            await Assert.That(visible).IsEquivalentTo([scope.RoomBId]);
        }

        await using (var missingTenantContext = fixture.CreateTenantContext(null))
        {
            Guid[] contractRoomIds = [scope.RoomA1Id, scope.RoomA2Id, scope.RoomA3Id, scope.RoomBId];
            await Assert.That(await missingTenantContext.LocationRooms
                .AsNoTracking()
                .AnyAsync(room => contractRoomIds.Contains(room.Id))).IsFalse();
        }
    }

    private static async Task VerifyOptimisticConcurrencyAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture,
        ProviderScope scope)
    {
        await using var staleContext = fixture.CreateTenantContext(scope.TenantAId);
        await using var winnerContext = fixture.CreateTenantContext(scope.TenantAId);
        var stale = await staleContext.LocationRooms.SingleAsync(room => room.Id == scope.RoomA3Id);
        var winner = await winnerContext.LocationRooms.SingleAsync(room => room.Id == scope.RoomA3Id);

        winner.Name = "Concurrent Winner";
        await winnerContext.SaveChangesAsync();
        stale.Name = "Stale Writer";

        DbUpdateConcurrencyException? conflict = null;
        try
        {
            await staleContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            conflict = exception;
        }

        await Assert.That(conflict).IsNotNull();
    }

    private static async Task VerifyTransactionsOutboxAndIdempotentReplayAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var committedSettingKey = $"provider-contract.commit.{suffix}";
        var rolledBackSettingKey = $"provider-contract.rollback.{suffix}";
        var committedOutbox = NewOutboxMessage(suffix, "committed");
        var rolledBackOutbox = NewOutboxMessage(suffix, "rolled-back");

        await using (var context = fixture.CreateSystemContext())
        {
            var unitOfWork = new EfCoreUnitOfWork(context);
            await unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
            {
                context.SystemSettings.Add(new SystemSetting { SettingKey = committedSettingKey, Value = "committed" });
                context.OutboxMessages.Add(committedOutbox);
                await context.SaveChangesAsync(cancellationToken);
            });
        }

        await using (var context = fixture.CreateSystemContext())
        {
            var unitOfWork = new EfCoreUnitOfWork(context);
            var rollback = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
                {
                    context.SystemSettings.Add(new SystemSetting { SettingKey = rolledBackSettingKey, Value = "rollback" });
                    context.OutboxMessages.Add(rolledBackOutbox);
                    await context.SaveChangesAsync(cancellationToken);
                    throw new InvalidOperationException("rollback contract");
                }));
            await Assert.That(rollback!.Message).IsEqualTo("rollback contract");
        }

        await using (var context = fixture.CreateSystemContext())
        {
            await Assert.That(await context.SystemSettings.AnyAsync(setting => setting.SettingKey == committedSettingKey)).IsTrue();
            await Assert.That(await context.OutboxMessages.AnyAsync(message => message.Id == committedOutbox.Id)).IsTrue();
            await Assert.That(await context.SystemSettings.AnyAsync(setting => setting.SettingKey == rolledBackSettingKey)).IsFalse();
            await Assert.That(await context.OutboxMessages.AnyAsync(message => message.Id == rolledBackOutbox.Id)).IsFalse();
        }

        await using (var firstContext = fixture.CreateSystemContext())
        await using (var secondContext = fixture.CreateSystemContext())
        {
            var firstRepository = new OutboxRepository(firstContext);
            var secondRepository = new OutboxRepository(secondContext);
            var claims = await Task.WhenAll(
                firstRepository.TryClaimForProcessing(
                    committedOutbox.Id,
                    DateTime.UtcNow),
                secondRepository.TryClaimForProcessing(
                    committedOutbox.Id,
                    DateTime.UtcNow));
            await Assert.That(claims.Count(claim => claim is not null))
                .IsEqualTo(1);
            var owner = claims[0] is not null
                ? firstRepository
                : secondRepository;
            var stale = claims[0] is not null
                ? secondRepository
                : firstRepository;
            DateTime claim = claims.Single(value => value is not null)!.Value;
            await Assert.That(
                    await owner.MarkAsCompleted(committedOutbox.Id, claim))
                .IsTrue();
            await Assert.That(
                    await stale.MarkAsCompleted(committedOutbox.Id, claim))
                .IsFalse();
        }

        var tenantId = Guid.CreateVersion7();
        var idempotencyKey = $"provider-contract-{suffix}";
        var now = DateTime.UtcNow;
        var ownerRecord = NewIdempotencyRecord(idempotencyKey, tenantId, now);
        await using (var context = fixture.CreateSystemContext())
        {
            var repository = new IdempotencyRepository(context);
            var claim = await repository.TryClaimAsync(ownerRecord);
            await Assert.That(claim.IsOwner).IsTrue();
            await Assert.That(await repository.CompleteAsync(ownerRecord.Id, 201, "{\"result\":\"created\"}", "application/json")).IsTrue();
        }

        await using (var context = fixture.CreateSystemContext())
        {
            var repository = new IdempotencyRepository(context);
            var replay = await repository.TryClaimAsync(NewIdempotencyRecord(idempotencyKey, tenantId, now.AddSeconds(1)));
            var persisted = await repository.FindAsync(idempotencyKey, tenantId);

            await Assert.That(replay.IsOwner).IsFalse();
            await Assert.That(replay.Record.Id).IsEqualTo(ownerRecord.Id);
            await Assert.That(persisted).IsNotNull();
            await Assert.That(persisted!.StatusCode).IsEqualTo(201);
            await Assert.That(persisted.ResponseBody).IsEqualTo("{\"result\":\"created\"}");
        }
    }

    private static Tenant NewTenant(string slug) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = slug,
        Slug = slug,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
    };

    private static Location NewLocation(Tenant tenant, string city) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = $"{city} Venue",
        Country = "BE",
        City = city,
        TenantId = tenant.Id,
        Tenant = tenant,
    };

    private static Location AddressLocation(
        Guid id,
        Guid tenantId,
        Guid actorId,
        string displayName,
        string address,
        bool tenantApproved = true)
    {
        var location = new Location
        {
            Id = id,
            TenantId = tenantId,
            FullName = displayName,
            Country = "BE",
            City = "Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        location.SetManualAddress(address, "1000");
        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.Manual,
            tenantApproved
                ? LocationAddressVisibilityEnum.TenantApproved
                : LocationAddressVisibilityEnum.CreatorPrivate,
            null);
        return location;
    }

    private static LocationRoom NewRoom(Tenant tenant, Location location, string name, int sortOrder) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant.Id,
        Tenant = tenant,
        LocationId = location.Id,
        Location = location,
        Name = name,
        SortOrder = sortOrder,
    };

    private static OutboxMessage NewOutboxMessage(string suffix, string state) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = "ProviderContract",
        AggregateId = Guid.CreateVersion7(),
        EventType = $"provider-contract-{state}-{suffix}",
        Status = OutboxMessageStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        MaxRetries = 3,
    };

    private static IdempotencyRecord NewIdempotencyRecord(string key, Guid tenantId, DateTime createdAt) => new()
    {
        Id = Guid.CreateVersion7(),
        Key = key,
        TenantId = tenantId,
        UserId = "provider-contract",
        RequestMethod = "POST",
        RequestTarget = "/provider-contract",
        RequestBodyHash = "provider-contract-body",
        PrincipalFingerprint = "provider-contract-principal",
        StatusCode = IdempotencyRecord.InProgressStatusCode,
        CreatedAt = createdAt,
        ExpiresAt = createdAt.AddHours(1),
    };

    private sealed record ProviderScope(
        Guid TenantAId,
        Guid TenantBId,
        Guid RoomA1Id,
        Guid RoomA2Id,
        Guid RoomA3Id,
        Guid RoomBId);

    private sealed class SelectCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        private string? _query;
        private (string Name, System.Data.DbType Type, int Size, object Value)[] _parameters = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            if (_query is null && command.CommandText.Contains("address_substring_key", StringComparison.Ordinal))
            {
                _query = command.CommandText;
                _parameters = command.Parameters.Cast<DbParameter>()
                    .Select(parameter => (parameter.ParameterName, parameter.DbType, parameter.Size, parameter.Value!)).ToArray();
            }
            return ValueTask.FromResult(result);
        }

        internal async Task RecordQueryPlanAsync(ExploreDbContext context, PrimaryDatabaseProvider provider, int matchingRows)
        {
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = _query ?? throw new InvalidOperationException("No actual suggestion query was captured.");
            foreach (var captured in _parameters)
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = captured.Name;
                parameter.DbType = captured.Type;
                parameter.Size = captured.Size;
                parameter.Value = captured.Value;
                command.Parameters.Add(parameter);
            }
            var timer = Stopwatch.StartNew();
            await using (DbDataReader measurement = await command.ExecuteReaderAsync())
            {
                while (await measurement.ReadAsync()) { }
            }
            timer.Stop();

            var operators = new List<string>();
            await using DbCommand mode = context.Database.GetDbConnection().CreateCommand();
            if (provider == PrimaryDatabaseProvider.SqlServer)
            {
                mode.CommandText = "SET STATISTICS XML ON";
                await mode.ExecuteNonQueryAsync();
            }
            try
            {
                command.CommandText = provider switch
                {
                    PrimaryDatabaseProvider.SqlServer => _query!,
                    PrimaryDatabaseProvider.Sqlite => "EXPLAIN QUERY PLAN " + _query,
                    PrimaryDatabaseProvider.PostgreSql => "EXPLAIN (FORMAT JSON) " + _query,
                    _ => "EXPLAIN FORMAT=JSON " + _query
                };
                await using DbDataReader plan = await command.ExecuteReaderAsync();
                do
                {
                    while (await plan.ReadAsync())
                    {
                        if (provider == PrimaryDatabaseProvider.Sqlite)
                        {
                            operators.Add(plan.GetString(3).Split(' ')[0]);
                        }
                        else if (provider == PrimaryDatabaseProvider.SqlServer)
                        {
                            if (plan.FieldCount == 1 && plan.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase))
                                operators.AddRange(XDocument.Parse(plan.GetString(0)).Descendants()
                                    .Attributes("PhysicalOp").Select(attribute => attribute.Value));
                        }
                        else
                        {
                            using JsonDocument json = JsonDocument.Parse(plan.GetString(0));
                            CollectOperators(json.RootElement, operators);
                        }
                    }
                }
                while (await plan.NextResultAsync());
            }
            finally
            {
                if (provider == PrimaryDatabaseProvider.SqlServer)
                {
                    mode.CommandText = "SET STATISTICS XML OFF";
                    await mode.ExecuteNonQueryAsync();
                }
            }
            await Assert.That(operators).IsNotEmpty();
            Console.WriteLine(FormattableString.Invariant(
                $"UnicodeQueryProbe provider={provider} authorized-matches={matchingRows} warm-query-ms={timer.Elapsed.TotalMilliseconds:F3} operators={string.Join(',', operators)}"));
        }

        private static void CollectOperators(JsonElement element, List<string> operators)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name is "Node Type" or "access_type")
                        operators.Add(property.Value.GetString()!);
                    else
                        CollectOperators(property.Value, operators);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray()) CollectOperators(item, operators);
            }
        }
    }
}

[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class ContainerizedPrimaryDatabaseProviderBehaviorContractTests(
    AdmissionAuthorityProviderFixture containerFixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task GeneratedInitialSupportsSharedRuntimeBehavior(
        PrimaryDatabaseProvider provider)
    {
        PrimaryDatabaseConnectionOptions migratorOptions =
            containerFixture.CreateOptions(provider, PrimaryDatabaseRole.Migrator);
        await MigrateAsync(migratorOptions);

        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create(
            containerFixture.CreateOptions(provider));
        await PrimaryDatabaseProviderBehaviorContractTests
            .AssertSharedPersistenceBehaviorAsync(fixture);
        await PrimaryDatabaseProviderBehaviorContractTests
            .AssertDataProtectionKeyRingSurvivesProviderRecreationAsync(fixture);
        await PrimaryDatabaseProviderBehaviorContractTests
            .AssertUnicodeAddressSuggestionContractAsync(fixture);
    }

    private static async Task MigrateAsync(
        PrimaryDatabaseConnectionOptions databaseOptions)
    {
        var applicationOptions =
            TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            applicationOptions,
            databaseOptions);
        await using (var context =
                     new ExploreDbContext(applicationOptions.Options))
        {
            await context.GetService<IMigrator>().MigrateAsync();
        }

        var dataProtectionOptions =
            TestDbContextOptions.Create<DataProtectionKeyContext>();
        PrimaryDatabaseProviderComposition.ConfigureDataProtection(
            dataProtectionOptions,
            databaseOptions);
        await using var dataProtectionContext =
            new DataProtectionKeyContext(dataProtectionOptions.Options);
        await dataProtectionContext.GetService<IMigrator>().MigrateAsync();
    }
}
