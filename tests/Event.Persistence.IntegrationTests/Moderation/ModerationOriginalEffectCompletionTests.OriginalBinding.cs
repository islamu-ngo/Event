using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Additional_or_changed_original_obligations_cannot_be_silently_dropped(bool additive)
    {
        await using var fixture = await Fixture.CreateAsync();
        await ReplaceCompletionOriginalAsync(fixture, capture =>
        {
            var notice = capture.Notice!;
            var original = notice.Consequences.Single();
            return capture with
            {
                Notice = notice with
                {
                    Consequences = additive
                        ? [original, original with { Strategy = ModerationAdditiveStrategyKind.ClassificationIntegrity }]
                        : [original with { Plan = original.Plan with { Capacity = ModerationCapacityMode.OwningParticipantTransitionExactlyOnce } }]
                }
            };
        });
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.OriginalRecipe, 0);
    }

    [Test]
    public async Task A_changed_original_publication_is_not_replaced_with_the_current_selection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await ReplaceCompletionOriginalAsync(fixture, capture => capture with
        {
            Notice = capture.Notice! with { PublicationId = Guid.CreateVersion7() }
        });
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.OriginalRecipe, 0);
    }

    [Test]
    public async Task Original_proof_expiry_during_native_accounting_cannot_continue_private_notice_reads()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cause = (await fixture.ExecuteAsync()).Receipt!;
        await SeedCompletedReviewAsync(fixture, cause, safe: false);
        await SeedOriginalNoticeFactsAsync(fixture, acknowledge: true);
        var expiry = new ExpireCompletionOnReview(fixture);
        var result = await CompleteOriginalAsync(fixture, interceptors: [expiry]);
        await Assert.That(expiry.Observed).IsTrue();
        await Assert.That(expiry.LaterNotificationRead).IsFalse();
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.OriginalCustody, 1);
    }

    private static async Task ReplaceCompletionOriginalAsync(Fixture fixture,
        Func<ModerationNoticeCaptureSnapshot, ModerationNoticeCaptureSnapshot> change)
    {
        await using var writer = fixture.Context();
        writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var effect = await writer.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == fixture.EffectId);
        var source = await writer.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == fixture.EffectId);
        var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        await writer.ModerationNoticeSources.Where(row => row.EffectId == fixture.EffectId).ExecuteDeleteAsync();
        writer.ModerationNoticeSources.Add(ModerationNoticeSource.Capture(effect, source.TenantId,
            JsonSerializer.Serialize(change(capture)), source.RetainUntil));
        await writer.SaveChangesAsync();
    }

    private sealed class ExpireCompletionOnReview(Fixture fixture) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }
        public bool LaterNotificationRead { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Observed && command.CommandText.Contains("moderation_notice_notification_receipts", StringComparison.Ordinal))
                LaterNotificationRead = true;
            if (!Observed && Enumerable.Range(0, result.FieldCount).Any(index => result.GetName(index) == "disposition")
                && command.CommandText.Contains("event_resource_moderation_review_completions", StringComparison.Ordinal))
            {
                Observed = true;
                fixture.Clock.UtcNow = fixture.PurposeDeadline;
            }
            return ValueTask.FromResult(result);
        }
    }
}
