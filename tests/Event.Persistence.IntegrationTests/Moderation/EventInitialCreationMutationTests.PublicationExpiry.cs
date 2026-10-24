using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Explore.Application.Exceptions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventInitialCreationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Initial_publication_cannot_outlive_original_authority_during_terminal_snapshot_reads(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var setup = database.Context();
        var clock = new InitialPublicationClock();
        var creation = (await PrepareAsync(database, setup, "organization")) with
        {
            RequestedStatus = EventStatusEnum.Published,
            CreatedAtUtc = clock.Now.UtcDateTime
        };
        var publication = await AdoptInitialPublicationAsync(database);
        var originalUntil = clock.Now.AddMinutes(1);
        await StoreInitialPublicationRestrictionAsync(database, publication, originalUntil, protective: true);
        var crossing = new ExpireInitialPublicationDuringSnapshot(clock, originalUntil);
        await using var context = database.Context(crossing);
        await using var retained = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        await using var session = await Session(database, context, retained?.Authority(context, clock), clock);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await session.Boundary.ExecuteCreationAsync(
            creation, (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default));

        await Assert.That(crossing.CrossedOriginalBound).IsTrue();
        await AssertInitialPublicationStateAsync(database, creation, 2, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private sealed class ExpireInitialPublicationDuringSnapshot(InitialPublicationClock clock, DateTimeOffset bound)
        : DbCommandInterceptor
    {
        private int _reads;
        public bool CrossedOriginalBound { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not Explore.Persistence.ExploreDbContext context) return ValueTask.FromResult(result);
            var entity = context.Model.FindEntityType(typeof(ModerationProtectiveMeasure))!;
            var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"FROM {table}", StringComparison.OrdinalIgnoreCase)
                && ++_reads == 2)
            {
                clock.Now = bound;
                CrossedOriginalBound = true;
            }
            return ValueTask.FromResult(result);
        }
    }
}
