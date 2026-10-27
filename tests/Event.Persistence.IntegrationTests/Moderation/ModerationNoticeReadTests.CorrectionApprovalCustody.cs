using System.Data.Common;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_approval_does_not_reread_original_private_text_after_custody_expires(bool atApprovalFence)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        var before = await CorrectionApprovalStateAsync(fixture, pending);
        var probe = new ExpireCorrectionOriginalCustody(fixture, pending, atApprovalFence);

        var result = await WriteCorrectionApprovalAsync(fixture, pending.Request, interceptor: probe);

        await Assert.That(probe.Expired).IsTrue();
        await Assert.That(probe.ExpiredPrivateQuery).IsFalse();
        await Assert.That(result.Outcome == Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known).IsFalse();
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(await CorrectionApprovalStateAsync(fixture, pending)).IsEqualTo(before);
    }

    private sealed class ExpireCorrectionOriginalCustody(NoticeDatabase fixture,
        CorrectionApprovalPending pending, bool atApprovalFence) : DbCommandInterceptor
    {
        public bool Expired { get; private set; }
        public bool ExpiredPrivateQuery { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!atApprovalFence && !Expired && eventData.Context is ExploreDbContext context
                && Reads<ModerationOperationReceipt>(context, command))
                Expire();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (atApprovalFence && !Expired && eventData.Context is ExploreDbContext context
                && command.CommandText.TrimStart().StartsWith(
                    $"UPDATE {Table<ModerationNoticeDisclosureApproval>(context)}", StringComparison.OrdinalIgnoreCase)
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id
                    && id == pending.Original.Capture.Notice!.ApprovalId))
                Expire();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Expired && eventData.Context is ExploreDbContext context
                && (ReadsBody<ModerationNoticeSource>(context, command, nameof(ModerationNoticeSource.SnapshotJson))
                    || ReadsBody<ModerationNoticeDisclosureApproval>(context, command, nameof(ModerationNoticeDisclosureApproval.FactsJson))))
            {
                ExpiredPrivateQuery = true;
                throw new InvalidOperationException("Expired original custody must not be reread for text approval.");
            }
            return ValueTask.FromResult(result);
        }

        private void Expire()
        {
            Expired = true;
            fixture.Clock.UtcNow = pending.Original.Source.RetainUntil!.Value;
        }

        private static bool Reads<T>(ExploreDbContext context, DbCommand command) =>
            command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains($"FROM {Table<T>(context)}", StringComparison.OrdinalIgnoreCase);

        private static bool ReadsBody<T>(ExploreDbContext context, DbCommand command, string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var column = entity.FindProperty(property)!.GetColumnName(table)!;
            return Reads<T>(context, command) && command.CommandText.Contains(
                context.GetService<ISqlGenerationHelper>().DelimitIdentifier(column), StringComparison.Ordinal);
        }

        private static string Table<T>(ExploreDbContext context)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        }
    }
}
