using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class PdsCompensationLineageAuthorityTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SupersededAt = CreatedAt.AddMinutes(1);

    [Test]
    public async Task SupersedePrior_ChangesOnlyExactSuccessorLineageAndPreservesCompensationEvidence()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        Lineage lineage = await SeedAsync(fixture);
        ExploreDbContext context = fixture.Context;
        Dictionary<Guid, Dictionary<string, object?>> expected = await OutboxSnapshotAsync(context);
        Dictionary<string, object?> recordBefore = ScalarSnapshot(
            context, await context.AtprotoRecords.AsNoTracking().SingleAsync());
        await using var transaction = await context.Database.BeginTransactionAsync();
        IPdsSyncOutboxRepository repository = fixture.Services.GetRequiredService<IPdsSyncOutboxRepository>();

        int count = await SupersedeAsync(repository, lineage.Successor);
        await repository.AddAsync(lineage.Successor);
        await transaction.CommitAsync();
        context.ChangeTracker.Clear();

        foreach (Guid predecessorId in lineage.PredecessorIds)
        {
            Dictionary<string, object?> predecessor = expected[predecessorId];
            predecessor[nameof(PdsSyncOutbox.Status)] = PdsSyncStatus.Superseded;
            predecessor[nameof(PdsSyncOutbox.SupersededById)] = lineage.Successor.Id;
            predecessor[nameof(PdsSyncOutbox.SupersededAt)] = SupersededAt;
            predecessor[nameof(PdsSyncOutbox.LeaseOwner)] = null;
            predecessor[nameof(PdsSyncOutbox.LeaseToken)] = null;
            predecessor[nameof(PdsSyncOutbox.LeaseExpiresAt)] = null;
        }

        expected.Add(lineage.Successor.Id, ScalarSnapshot(context, lineage.Successor));
        await AssertOutboxSnapshotAsync(context, expected);
        await AssertScalarsAsync(
            ScalarSnapshot(context, await context.AtprotoRecords.AsNoTracking().SingleAsync()),
            recordBefore);
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task SupersedePrior_RollbackRestoresPredecessorsLeasesEvidenceAndUnrelatedRows()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        Lineage lineage = await SeedAsync(fixture);
        ExploreDbContext context = fixture.Context;
        Dictionary<Guid, Dictionary<string, object?>> before = await OutboxSnapshotAsync(context);
        Dictionary<string, object?> recordBefore = ScalarSnapshot(
            context, await context.AtprotoRecords.AsNoTracking().SingleAsync());
        IPdsSyncOutboxRepository repository = fixture.Services.GetRequiredService<IPdsSyncOutboxRepository>();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await SupersedeAsync(repository, lineage.Successor);
            await repository.AddAsync(lineage.Successor);
            context.ChangeTracker.Clear();

            PdsSyncOutbox processing = await context.PdsSyncOutbox.AsNoTracking()
                .SingleAsync(row => row.Id == lineage.PredecessorIds[1]);
            await Assert.That(processing.Status).IsEqualTo(PdsSyncStatus.Superseded);
            await Assert.That(processing.SupersededById).IsEqualTo(lineage.Successor.Id);
            await Assert.That(processing.LeaseToken).IsNull();
            await Assert.That(await context.PdsSyncOutbox.AnyAsync(row => row.Id == lineage.Successor.Id)).IsTrue();
            await transaction.RollbackAsync();
        }

        context.ChangeTracker.Clear();
        await AssertOutboxSnapshotAsync(context, before);
        await AssertScalarsAsync(
            ScalarSnapshot(context, await context.AtprotoRecords.AsNoTracking().SingleAsync()),
            recordBefore);
    }

    private static Task<int> SupersedeAsync(IPdsSyncOutboxRepository repository, PdsSyncOutbox successor) =>
        repository.SupersedePriorAsync(
            successor,
            SupersededAt,
            CancellationToken.None);

    [Test]
    public async Task CompensationEvidence_RejectsOtherSourceDespiteMatchingRemoteLineageAndSuccessorPointer()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        Lineage lineage = await SeedAsync(fixture);
        ExploreDbContext context = fixture.Context;
        PdsSyncOutbox prior = await context.PdsSyncOutbox
            .SingleAsync(row => row.Id == lineage.PredecessorIds[0]);
        prior.Status = PdsSyncStatus.Superseded;
        prior.SupersededById = lineage.Successor.Id;
        prior.SupersededAt = SupersededAt;

        Guid otherId = Guid.CreateVersion7();
        const string otherPayload = "{\"name\":\"Other source evidence\"}";
        var otherSource = new PdsSyncOutbox
        {
            Id = otherId,
            TenantId = lineage.Successor.TenantId,
            UserId = lineage.Successor.UserId,
            Did = lineage.Successor.Did,
            Collection = lineage.Successor.Collection,
            RecordKey = lineage.Successor.RecordKey,
            PdsHost = lineage.Successor.PdsHost,
            SourceEntityType = lineage.Successor.SourceEntityType,
            SourceEntityId = Guid.CreateVersion7(),
            SourceVersion = lineage.Successor.SourceVersion,
            AtprotoRecordId = lineage.Successor.AtprotoRecordId,
            DependsOnAtprotoRecordId = lineage.Successor.DependsOnAtprotoRecordId,
            DependsOnCid = lineage.Successor.DependsOnCid,
            CreatedAt = SupersededAt,
            IdempotencyKey = otherId.ToString("N"),
            Operation = PdsSyncOperation.Update,
            Payload = otherPayload,
            PayloadHash = Hash(otherPayload),
            ExpectedCid = "bafy-other-source-base",
            MaxRetries = 10,
            Status = PdsSyncStatus.Superseded,
            SupersededById = lineage.Successor.Id,
            SupersededAt = SupersededAt
        };
        context.PdsSyncOutbox.AddRange(lineage.Successor, otherSource);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        IPdsSyncOutboxRepository repository = fixture.Services.GetRequiredService<IPdsSyncOutboxRepository>();
        PdsSyncCompensationEvidence evidence = await repository.GetCompensationEvidenceAsync(lineage.Successor);

        await Assert.That(evidence.IsComplete).IsTrue();
        await Assert.That(evidence.AllowedPayloads).IsEquivalentTo(new[] { prior.Payload! });
        await Assert.That(evidence.AllowedBaseCids)
            .IsEquivalentTo(new[] { prior.ExpectedCid!, lineage.Successor.ExpectedCid! });
        await Assert.That(context.ChangeTracker.Entries<PdsSyncOutbox>()).IsEmpty();
    }

    private static async Task<Lineage> SeedAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        ExploreDbContext context = fixture.Context;
        Guid otherUserId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = otherUserId,
            Pii = new UserPii { Email = string.Empty, FirstName = "Other", LastName = "Owner" },
            CreatedAt = CreatedAt
        });
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            UserId = otherUserId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = CreatedAt,
            CreatedAt = CreatedAt
        });
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = "did:plc:lineage-owner",
            Collection = "community.lexicon.calendar.event",
            RecordKey = "lineage-event",
            Cid = "bafy-lineage-record",
            Uri = "at://did:plc:lineage-owner/community.lexicon.calendar.event/lineage-event",
            Direction = AtprotoRecordDirection.Outbound,
            Provenance = AtprotoRecordProvenance.LocalLifecycle,
            SourceVersion = 7,
            RecordJson = "{\"name\":\"Persisted predecessor evidence\"}",
            RecordHash = Hash("{\"name\":\"Persisted predecessor evidence\"}"),
            UpdatedAt = CreatedAt
        };
        context.AtprotoRecords.Add(record);
        Guid sourceId = Guid.CreateVersion7();
        PdsSyncOutbox Row(
            string label,
            PdsSyncStatus status,
            Guid? userId = null,
            string? did = null,
            string? collection = null,
            string? recordKey = null,
            bool successor = false)
        {
            string payload = $"{{\"name\":\"{label} evidence\"}}";
            return new PdsSyncOutbox
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                UserId = userId ?? fixture.UserId,
                Did = did ?? record.Did,
                Collection = collection ?? record.Collection,
                RecordKey = recordKey ?? record.RecordKey,
                Operation = successor ? PdsSyncOperation.Delete : PdsSyncOperation.Update,
                Payload = successor ? null : payload,
                PayloadHash = Hash(successor ? string.Empty : payload),
                IdempotencyKey = Guid.CreateVersion7().ToString("N"),
                PdsHost = "https://pds.example.test/",
                SourceEntityType = "Event",
                SourceEntityId = sourceId,
                SourceVersion = Guid.CreateVersion7(),
                AtprotoRecordId = record.Id,
                DependsOnAtprotoRecordId = record.Id,
                DependsOnCid = record.Cid,
                ExpectedCid = $"bafy-{label}-base",
                Status = status,
                CreatedAt = successor ? SupersededAt : CreatedAt,
                RetryCount = successor ? 0 : 2,
                LastError = successor ? null : "remote_write_unconfirmed",
                NextRetryAt = CreatedAt.AddMinutes(5),
                MaxRetries = 10,
                LeaseOwner = status == PdsSyncStatus.Processing ? $"worker-{label}" : null,
                LeaseToken = status == PdsSyncStatus.Processing ? Guid.CreateVersion7() : null,
                LeaseExpiresAt = status == PdsSyncStatus.Processing ? CreatedAt.AddMinutes(10) : null,
                LeaseFence = status == PdsSyncStatus.Processing ? 4 : 0,
                ProcessedAt = status == PdsSyncStatus.Completed ? CreatedAt.AddSeconds(30) : null,
                SettledUri = status == PdsSyncStatus.Completed ? record.Uri : null,
                SettledCid = status == PdsSyncStatus.Completed ? record.Cid : null
            };
        }

        PdsSyncOutbox pending = Row("pending", PdsSyncStatus.Pending);
        PdsSyncOutbox processing = Row("processing", PdsSyncStatus.Processing);
        context.PdsSyncOutbox.AddRange(
            pending,
            processing,
            Row("other-user", PdsSyncStatus.Processing, userId: otherUserId),
            Row("other-did", PdsSyncStatus.Processing, did: "did:plc:other-owner"),
            Row("other-collection", PdsSyncStatus.Processing, collection: "app.bsky.feed.post"),
            Row("other-key", PdsSyncStatus.Processing, recordKey: "other-event"),
            Row("completed", PdsSyncStatus.Completed));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return new Lineage(Row("successor", PdsSyncStatus.Pending, successor: true), [pending.Id, processing.Id]);
    }

    private static string Hash(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static Dictionary<string, object?> ScalarSnapshot<TEntity>(ExploreDbContext context, TEntity entity)
        where TEntity : class =>
        context.Entry(entity).Properties.ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);

    private static async Task<Dictionary<Guid, Dictionary<string, object?>>> OutboxSnapshotAsync(ExploreDbContext context) =>
        (await context.PdsSyncOutbox.AsNoTracking().ToListAsync())
        .ToDictionary(row => row.Id, row => ScalarSnapshot(context, row));

    private static async Task AssertOutboxSnapshotAsync(
        ExploreDbContext context,
        Dictionary<Guid, Dictionary<string, object?>> expected)
    {
        Dictionary<Guid, Dictionary<string, object?>> actual = await OutboxSnapshotAsync(context);
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        foreach ((Guid id, Dictionary<string, object?> fields) in expected)
        {
            await Assert.That(actual.ContainsKey(id)).IsTrue();
            await AssertScalarsAsync(actual[id], fields);
        }
    }

    private static async Task AssertScalarsAsync(
        Dictionary<string, object?> actual,
        Dictionary<string, object?> expected)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        foreach ((string property, object? value) in expected)
        {
            await Assert.That(actual[property]).IsEqualTo(value);
        }
    }

    private sealed record Lineage(PdsSyncOutbox Successor, Guid[] PredecessorIds);
}
