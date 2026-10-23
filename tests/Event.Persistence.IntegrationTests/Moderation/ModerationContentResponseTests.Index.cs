using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
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
    public async Task Qualified_response_discovery_is_bounded_single_use_and_never_selects_ciphertext_or_allegation()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider());
        var first = Command(fixture);
        var second = first with { OperationId = Guid.CreateVersion7() };
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
        {
            await Assert.That((await submit.Handler.ExecuteAsync(first)).IsSuccess).IsTrue();
            await Assert.That((await submit.Handler.ExecuteAsync(second)).IsSuccess).IsTrue();
        }
        var ordered = new[] { first.OperationId, second.OperationId }.Order().ToArray();
        var witness = new RejectResponseIndexSecrets();
        using var template = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)template.GetService<IDbContextOptions>();
        var context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(witness).Options)
        { TenantContext = fixture.Database };
        await using var reviewer = new ResponseSession(fixture, fixture.Database.ReviewerId, context, protection: keys);
        var query = new GetModerationContentResponsesQueryHandler(reviewer.Repository, fixture.Database);
        var pending = await query.QueryAsync(new(fixture.Approval.CaseId, ordered[0], 1));
        ModerationContentResponseSummary? summary = null;
        var wrote = false;
        var outcome = await pending.DiscloseAsync((rows, _) =>
        {
            summary = rows.Single();
            return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 });
        }, async (_, _) =>
        {
            await Assert.That(reviewer.Context.Database.CurrentTransaction).IsNotNull();
            wrote = true;
        }, default);
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(wrote).IsTrue();
        await Assert.That(summary!.Id).IsEqualTo(ordered[1]);
        await Assert.That(summary.CaseId).IsEqualTo(fixture.Approval.CaseId);
        await Assert.That(summary.EffectId).IsEqualTo(fixture.EffectId);
        await Assert.That(summary.RetainUntil).IsEqualTo(fixture.Approval.RetainUntil);
        await Assert.That(witness.MetadataSelections).IsGreaterThan(0);
        await Assert.That(keys.Plaintext).IsNull();
        await Assert.That(await pending.DiscloseAsync((_, _) => throw new InvalidOperationException(),
            (_, _) => throw new InvalidOperationException(), default))
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
    }

    [Test]
    [Arguments("deadline")]
    [Arguments("withdrawn")]
    [Arguments("role")]
    public async Task Changed_reviewer_or_deadline_after_metadata_preparation_discards_the_whole_page(string change)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider());
        var command = Command(fixture);
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
            await Assert.That((await submit.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await using var reviewer = new ResponseSession(fixture, fixture.Database.ReviewerId, protection: keys);
        var wrote = false;
        var prepared = false;
        var outcome = await reviewer.Repository.DiscloseContentResponseIndexAsync(
            fixture.Database.TenantId, fixture.Approval.CaseId, null, 32, async (_, _) =>
            {
                prepared = true;
                if (change == "deadline") fixture.Clock.UtcNow = fixture.Approval.RetainUntil;
                else if (change == "role")
                    await reviewer.Context.TenantUserRoleGrants
                        .Where(row => row.TenantUser.UserId == fixture.Database.ReviewerId).ExecuteDeleteAsync();
                else
                {
                    var assignment = await reviewer.Context.ModerationReviewerAssignments
                        .SingleAsync(row => row.ReviewerHumanId == fixture.Database.ReviewerId);
                    assignment.Withdraw(assignment.Revision, fixture.Database.AdministratorId, fixture.Clock.UtcNow);
                    await reviewer.Context.SaveChangesAsync();
                }
                return new byte[] { 1 };
            }, (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(prepared).IsTrue();
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(wrote).IsFalse();
        await Assert.That(keys.Plaintext).IsNull();
        await Assert.That(reviewer.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retained_author_or_reviewer_erasure_closes_the_captured_metadata_query_before_preparation(bool eraseReviewer)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider());
        var command = Command(fixture);
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
            await Assert.That((await submit.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await using var reviewer = new ResponseSession(fixture, fixture.Database.ReviewerId, protection: keys);
        var pending = await new GetModerationContentResponsesQueryHandler(reviewer.Repository, fixture.Database)
            .QueryAsync(new(fixture.Approval.CaseId));
        await using (var primary = fixture.Database.Context())
            await fixture.Privacy.Authority(primary, fixture.Clock).AppendAsync(new(
                Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                eraseReviewer ? fixture.Database.ReviewerId : fixture.Database.SubjectId,
                PrivacyErasureReasonCode.AccountDeletion, 1));
        var result = await pending.DiscloseAsync((_, _) => throw new InvalidOperationException(),
            (_, _) => throw new InvalidOperationException(), default);
        await Assert.That(result).IsEqualTo(ModerationQualificationWriteOutcome.NotFound);
        await Assert.That(keys.Plaintext).IsNull();
    }

    [Test]
    public async Task Prepared_index_transport_failure_propagates_without_repeating_disclosure()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider());
        var command = Command(fixture);
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
            await Assert.That((await submit.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await using var reviewer = new ResponseSession(fixture, fixture.Database.ReviewerId, protection: keys);
        var writes = 0;
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await reviewer.Repository.DiscloseContentResponseIndexAsync(fixture.Database.TenantId,
                fixture.Approval.CaseId, null, 32,
                (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
                (_, _) => { writes++; throw new ArgumentException("Transport stopped."); }, default));
        await Assert.That(writes).IsEqualTo(1);
        await Assert.That(keys.Plaintext).IsNull();
        await Assert.That(reviewer.Context.Database.CurrentTransaction).IsNull();
    }

    private sealed class RejectResponseIndexSecrets : DbCommandInterceptor
    {
        public int MetadataSelections { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context) return ValueTask.FromResult(result);
            var sql = context.GetService<ISqlGenerationHelper>();
            var response = context.Model.FindEntityType(typeof(ModerationContentResponse))!;
            var responseTable = StoreObjectIdentifier.Table(response.GetTableName()!, response.GetSchema());
            var ciphertext = sql.DelimitIdentifier(response.FindProperty("protectedStatement")!.GetColumnName(responseTable)!);
            var source = context.Model.FindEntityType(typeof(ModerationNoticeSource))!;
            var sourceTable = StoreObjectIdentifier.Table(source.GetTableName()!, source.GetSchema());
            var snapshot = sql.DelimitIdentifier(source.FindProperty(nameof(ModerationNoticeSource.SnapshotJson))!
                .GetColumnName(sourceTable)!);
            if (command.CommandText.Contains(ciphertext, StringComparison.Ordinal)
                || command.CommandText.Contains(snapshot, StringComparison.Ordinal))
                throw new InvalidOperationException("Response discovery attempted private statement or original-body selection.");
            if (command.CommandText.Contains(sql.DelimitIdentifier(response.GetTableName()!), StringComparison.Ordinal))
                MetadataSelections++;
            return ValueTask.FromResult(result);
        }
    }
}
