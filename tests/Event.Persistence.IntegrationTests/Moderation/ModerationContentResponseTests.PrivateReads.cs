using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    [Test]
    public async Task Expired_original_response_qualifies_metadata_without_selecting_private_snapshot()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true,
            representativeUntil: OriginalAt.AddYears(1));
        fixture.Clock.UtcNow = fixture.Approval.RetainUntil;
        var witness = new RejectExpiredOriginalPrivateSelection(fixture);
        using var original = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)original.GetService<IDbContextOptions>();
        var context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(witness).Options) { TenantContext = fixture.Database };
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId, context);
        await Assert.That((await session.Handler.ExecuteAsync(Command(fixture))).IsSuccess).IsFalse();
        var released = false;
        var outcome = await session.Repository.DiscloseContentResponseAvailabilityAsync(
            fixture.Database.TenantId, fixture.SubjectNoticeId,
            (_, _) => throw new InvalidOperationException("Expired availability must never reach preparation."),
            (_, _) => { released = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.NotFound);
        await Assert.That(released).IsFalse();
        await Assert.That(witness.ExpiredMetadataReads).IsGreaterThan(0);
        await Assert.That(witness.ExpiredPrivateSelects).IsEqualTo(0);
    }

    private sealed class RejectExpiredOriginalPrivateSelection(NoticeDatabase fixture) : DbCommandInterceptor
    {
        public int ExpiredMetadataReads { get; private set; }
        public int ExpiredPrivateSelects { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (fixture.Clock.UtcNow < fixture.Approval.RetainUntil
                || eventData.Context is not ExploreDbContext context)
                return ValueTask.FromResult(result);
            var entity = context.Model.FindEntityType(typeof(ModerationNoticeSource))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = context.GetService<ISqlGenerationHelper>();
            var snapshot = sql.DelimitIdentifier(entity.FindProperty(nameof(ModerationNoticeSource.SnapshotJson))!
                .GetColumnName(table)!);
            var metadata = sql.DelimitIdentifier(entity.FindProperty(nameof(ModerationNoticeSource.StatusMetadataJson))!
                .GetColumnName(table)!);
            if (command.CommandText.Contains(snapshot, StringComparison.Ordinal))
            {
                ExpiredPrivateSelects++;
                throw new InvalidOperationException("An expired Content response attempted a private original snapshot SELECT.");
            }
            if (command.CommandText.Contains(metadata, StringComparison.Ordinal)) ExpiredMetadataReads++;
            return ValueTask.FromResult(result);
        }
    }
}
