using System.Data.Common;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    public async Task Grant_expiry_crossing_during_construction_returns_unavailable_without_a_persisted_prefix()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var clock = new AdvancingAdjudicatedIssueClock(fixture.Clock.GetUtcNow());
        var request = AdjudicatedGrantRequest(fixture, original) with { ValidUntil = clock.Start.AddMicroseconds(3) };
        var probe = new StartAdjudicatedIssueClock(clock);
        await using (var writer = fixture.Database.Context(probe))
        {
            var (identity, machine) = CorrectionIdentity(writer, fixture.Database.AdministratorId);
            var repository = new ModerationAdjudicatedCaseReadGrantRepository(writer,
                fixture.Privacy.Authority(writer, clock), identity, machine, clock);
            await Assert.That(await repository.GrantAsync(request)).IsNull();
            await Assert.That(clock.Started).IsTrue();
            await Assert.That(clock.GetUtcNow() >= request.ValidUntil).IsTrue();
            await Assert.That(writer.Database.CurrentTransaction).IsNull();
            await Assert.That(writer.ChangeTracker.HasChanges()).IsFalse();
        }
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationAdjudicatedCaseReadGrant>()
            .AnyAsync(row => row.Id == request.OperationId)).IsFalse();
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    private sealed class AdvancingAdjudicatedIssueClock(DateTimeOffset start) : TimeProvider
    {
        private int _reads;
        public DateTimeOffset Start { get; } = start;
        public bool Started { get; set; }
        public override DateTimeOffset GetUtcNow() => Started ? Start.AddMicroseconds(_reads++) : Start;
    }

    private sealed class StartAdjudicatedIssueClock(AdvancingAdjudicatedIssueClock clock) : DbCommandInterceptor
    {
        private bool _sourceRead;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!clock.Started && eventData.Context is ExploreDbContext context)
            {
                if (Reads<ModerationNoticeSource>(context, command)
                    && command.CommandText.Contains("snapshot_json", StringComparison.OrdinalIgnoreCase))
                    _sourceRead = true;
                else if (_sourceRead && Reads<ModerationCase>(context, command))
                    // Advance across the final custody checks, expiry check and construction instant.
                    clock.Started = true;
            }
            return ValueTask.FromResult(result);
        }

        private static bool Reads<T>(ExploreDbContext context, DbCommand command)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            return command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"FROM {table}", StringComparison.OrdinalIgnoreCase);
        }
    }
}
