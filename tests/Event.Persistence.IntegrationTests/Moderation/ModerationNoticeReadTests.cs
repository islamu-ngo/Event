using System.Collections.Immutable;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Notices;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Real native read owner and CQS terminal consumer; no substituted authorization repository.</summary>
public sealed partial class ModerationNoticeReadTests
{
    [Test]
    public async Task Correction_obligations_cannot_be_reconstructed_from_a_noncreating_notice()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var decision = await context.ModerationDecisions.SingleAsync();
        var sanction = await context.ModerationSanctions.SingleAsync();
        var original = await context.ModerationEffectIntents.SingleAsync();
        var now = Now.AddMinutes(5);
        fixture.Clock.UtcNow = now;
        sanction.Revoke(sanction.Revision, now);
        var revoke = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id,
            decision.Revision, sanction.Id, sanction.Revision, original.TrustDomainId,
            original.ScopeKind, original.ScopeId, original.SubjectKind, original.SubjectId, 7,
            original.PolicyRevision, original.QualificationRevision, now, null, null);

        await Assert.That(await ModerationNoticeCorrectionAudience.ReadAsync(context, revoke, decision, fixture.Clock, default)).IsNull();
        await Assert.That(await context.ModerationNoticeSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationNotices.AnyAsync(row => row.EffectId == revoke.Id)).IsFalse();
        await Assert.That(revoke.CompletedAt).IsNull();
    }

    [Test]
    public async Task OriginalContentRepresentativeCanAcknowledgeWithoutBecomingTheFindingSubject()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await Assert.That((await reader.Acknowledge(fixture.SubjectNoticeId,
            Guid.CreateVersion7(), fixture.SnapshotHash)).IsSuccess).IsTrue();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = await inspect.ModerationNoticeSources.SingleAsync();
        var notification = await inspect.Set<ModerationNoticeNotificationReceipt>().SingleAsync();
        await Assert.That(source.SubjectKind).IsEqualTo((int)ModerationSubjectKind.Content);
        await Assert.That(notification.SubjectId).IsEqualTo(source.SubjectId);
        await Assert.That(notification.SubjectId).IsNotEqualTo(fixture.Database.SubjectId);
        await Assert.That(notification.AcknowledgedByHumanId).IsEqualTo(fixture.Database.SubjectId);
        await Assert.That(notification.RetainUntil).IsEqualTo(source.RetainUntil);
        await Assert.That((await inspect.ModerationFindings.SingleAsync()).Subject.Kind)
            .IsEqualTo(ModerationSubjectKind.Content);
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("replacement")]
    [Arguments("version")]
    public async Task OriginalContentCustodyDoesNotTransferToChangedOrReplacementEventRoles(string change)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await fixture.MutateAsync(async context =>
        {
            var original = await context.EventRoleAssignments.SingleAsync();
            if (change == "version")
                original.UpdateValidityWindow(original.StartsAtUtc, Now.AddDays(2).UtcDateTime, Now.UtcDateTime);
            else
            {
                original.Revoke(fixture.Database.AdministratorId, Now.UtcDateTime);
                if (change == "replacement")
                    context.EventRoleAssignments.Add(EventRoleAssignment.Create(fixture.Database.TenantId,
                        original.EventId, original.UserId, (int)RoleEnum.EventManager,
                        EventRoleAssignmentStatus.Active, Now.UtcDateTime, Now.AddDays(1).UtcDateTime,
                        fixture.Database.AdministratorId));
            }
        });
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That((await reader.Acknowledge(fixture.SubjectNoticeId,
            Guid.CreateVersion7(), fixture.SnapshotHash)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task OriginalRepresentativeExpiryDuringPreparationSuppressesPreparedContentBytes()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync((page, _) =>
        {
            fixture.Clock.UtcNow = Now.AddDays(1);
            return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(page));
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task IndependentReviewerCannotAcknowledgeAnOriginalContentRepresentativeNotice()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var reader = fixture.Reader(fixture.Database.ReviewerId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That((await reader.Acknowledge(fixture.ReviewerNoticeId,
            Guid.CreateVersion7(), fixture.SnapshotHash)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task RetainedErasureBlocksOriginalContentCustodyBeforeItsPrimarySagaExists()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using (var primary = fixture.Database.Context())
        {
            await fixture.Privacy.Authority(primary, fixture.Clock).AppendAsync(new(
                Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, fixture.Database.SubjectId,
                PrivacyErasureReasonCode.AccountDeletion, 1));
            await Assert.That(await primary.PrivacyErasureSagas.AnyAsync()).IsFalse();
        }
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That((await reader.Acknowledge(fixture.SubjectNoticeId,
            Guid.CreateVersion7(), fixture.SnapshotHash)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task OriginalContentStatusReportsOnlyItsActualRepresentativeAcknowledgement()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        ModerationNoticeStatusDto? status = null;
        var pending = await reader.Status(fixture.DecisionId, fixture.EffectId);
        await Assert.That(await pending.Projection!.DiscloseAsync((dto, _) => Task.FromResult(dto),
            (dto, _) => { status = dto; return Task.CompletedTask; })).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await Assert.That(status!.NotificationProof).IsEqualTo(ModerationNoticeNotificationProof.Unproven.ToString());
        await Assert.That(status.NotifiedAt).IsNull();
        await Assert.That((await reader.Acknowledge(fixture.SubjectNoticeId,
            Guid.CreateVersion7(), fixture.SnapshotHash)).IsSuccess).IsTrue();
        pending = await reader.Status(fixture.DecisionId, fixture.EffectId);
        await Assert.That(await pending.Projection!.DiscloseAsync((dto, _) => Task.FromResult(dto),
            (dto, _) => { status = dto; return Task.CompletedTask; })).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await Assert.That(status!.NotificationProof).IsEqualTo(ModerationNoticeNotificationProof.Proven.ToString());
        await Assert.That(status.NotifiedAt).IsEqualTo(Now);
        await Assert.That(JsonSerializer.Serialize(status)).DoesNotContain(fixture.PrivateJustification);
        await Assert.That(JsonSerializer.Serialize(status)).DoesNotContain("AcknowledgedByHumanId");
    }

    [Test]
    public async Task UnselectedAdministratorCannotReadOriginalContentNotificationStatus()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var reader = fixture.Reader(fixture.Database.AdministratorId);
        var pending = await reader.Status(fixture.DecisionId, fixture.EffectId);
        var delivered = false;
        await Assert.That(await pending.Projection!.DiscloseAsync((dto, _) => Task.FromResult(dto),
            (_, _) => { delivered = true; return Task.CompletedTask; })).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task OriginalContentStatusCannotOutliveItsRepresentativeDuringOutput()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var pending = await reader.Status(fixture.DecisionId, fixture.EffectId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync((dto, _) =>
        {
            fixture.Clock.UtcNow = Now.AddDays(1);
            return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(dto));
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task SubjectReceivesOnlyOriginalApprovedTextWithoutRequiringOrdinaryMembership()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await fixture.MutateAsync(async context =>
        {
            var membership = await context.TenantUsers.SingleAsync(row => row.UserId == fixture.Database.SubjectId);
            membership.IsDeleted = true;
        });
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        await Assert.That(JsonSerializer.Serialize(pending)).DoesNotContain(fixture.PrivateJustification);
        string? bytes = null;
        var outcome = await pending.Projection!.DiscloseAsync(
            (page, _) => Task.FromResult(JsonSerializer.Serialize(page)),
            (value, _) => { bytes = value; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await Assert.That(bytes).Contains(fixture.Approval.RuleMeaning);
        await Assert.That(bytes).DoesNotContain(fixture.PrivateJustification);
        await Assert.That(bytes).DoesNotContain("SnapshotJson");
        await Assert.That(bytes).DoesNotContain("ReviewerHumanId");
        await Assert.That(bytes).DoesNotContain("QualifiedRecipientId");
        await Assert.That(reader.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(reader.Context.ModerationTrustDomainId).IsNull();
    }

    [Test]
    public async Task ForeignRecipientCannotOpenOrListTheNativePointer()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.AdministratorId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        var pending = await reader.List();
        var count = -1;
        var outcome = await pending.Projection!.DiscloseAsync(
            (page, _) => Task.FromResult(page.Items.Length),
            (length, _) => { count = length; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("origin")]
    public async Task MatchingLocalRecipientIdentifierCannotBorrowForeignSourceAncestry(string substitution)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(substitution);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    public async Task DeletedCanonicalAccountCannotUseItsRetainedNoticePointer()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await fixture.MutateAsync(async context =>
        {
            var user = await context.Users.SingleAsync(row => row.Id == fixture.Database.SubjectId);
            user.IsDeleted = true;
        });
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    public async Task TenantOrCanonicalCallerChangedAfterHandlerCannotDisclose()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        reader.Tenant.TenantId = Guid.CreateVersion7();
        var prepared = false;
        var outcome = await pending.Projection!.DiscloseAsync(
            (_, _) => { prepared = true; return Task.FromResult(0); }, (_, _) => Task.CompletedTask);
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.Unavailable);
        await Assert.That(prepared).IsFalse();

        reader.Tenant.TenantId = fixture.Database.TenantId;
        pending = await reader.Detail(fixture.SubjectNoticeId);
        reader.SetCaller(fixture.Database.AdministratorId);
        outcome = await pending.Projection!.DiscloseAsync(
            (_, _) => { prepared = true; return Task.FromResult(0); }, (_, _) => Task.CompletedTask);
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.Unauthenticated);
        await Assert.That(prepared).IsFalse();
    }

    [Test]
    public async Task PurposeBoundReceiptCannotBorrowAnOrdinaryRecipientIdentifier()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        reader.SetCaller(fixture.Database.SubjectId, ApiAuthenticationSchemeNames.PrivacyErasureReceipt);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        await Assert.That(pending.Outcome).IsEqualTo(ModerationNoticeReadOutcome.Unauthenticated);
        await Assert.That(pending.Projection).IsNull();
    }

    [Test]
    public async Task RetainedReviewerPointerRequiresCurrentIndependentQualification()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using (var reader = fixture.Reader(fixture.Database.ReviewerId))
            await Assert.That(await reader.DeliverDetail(fixture.ReviewerNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await fixture.MutateAsync(async context =>
        {
            var role = await context.PlatformUserRoles.SingleAsync(row => row.UserId == fixture.Database.ReviewerId);
            context.PlatformUserRoles.Remove(role);
        });
        await using var revoked = fixture.Reader(fixture.Database.ReviewerId);
        await Assert.That(await revoked.DeliverDetail(fixture.ReviewerNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    public async Task ReviewerQualificationExpiryDuringRepresentationPreventsTerminalDelivery()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.ReviewerId);
        var pending = await reader.Detail(fixture.ReviewerNoticeId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync(
            (page, _) =>
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(page);
                fixture.Clock.UtcNow = Now.AddMinutes(30);
                return Task.FromResult(bytes);
            },
            (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task ExpiredAcquisitionAndSanctionDoNotEraseTheRetainedSubjectExplanation()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddDays(2);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
    }

    [Test]
    public async Task ReversedFindingPreservesOriginalExplanationWithoutReinstatingItsContribution()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await fixture.MutateAsync(async context =>
        {
            var finding = await context.ModerationFindings.SingleAsync();
            finding.Reverse(Guid.CreateVersion7(),
                SubjectReference.Create(ModerationSubjectKind.Human, fixture.Database.UrgencyAssessorId, fixture.Database.Instance),
                "Independent correction of the original finding.", finding.Revision, Now);
        });
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await inspect.ModerationFindings.SingleAsync()).State).IsEqualTo(ModerationFindingState.Reversed);
        await Assert.That(await inspect.ModerationSanctions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task RetentionCeilingCrossedDuringRepresentationNeverWritesHistoricalBytes()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync(
            (page, _) =>
            {
                fixture.Clock.UtcNow = page.Items.Single().RetainUntil;
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(page));
            }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    public async Task ExplicitDisclosureRevocationClosesTextWithoutCompletingTheEffect()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await fixture.MutateAsync(async context =>
        {
            var approval = await context.ModerationNoticeDisclosureApprovals.SingleAsync();
            approval.Revoke(approval.Revision, fixture.Database.AdministratorId, Now);
        });
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    public async Task FinalCanonicalIdentityChangeAndProjectionReplayReleaseNoBytes()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        var delivered = 0;
        var first = await pending.Projection!.DiscloseAsync(
            (page, _) =>
            {
                reader.SetCaller(fixture.Database.AdministratorId);
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(page));
            }, (_, _) => { delivered++; return Task.CompletedTask; });
        reader.SetCaller(fixture.Database.SubjectId);
        var second = await pending.Projection.DiscloseAsync(
            (_, _) => Task.FromResult(0), (_, _) => { delivered++; return Task.CompletedTask; });
        await Assert.That(first).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(second).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsEqualTo(0);
    }

    [Test]
    public async Task ReadOnlyPortalAndReviewerExposureDoNotInventNotificationTime()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using (var subject = fixture.Reader(fixture.Database.SubjectId))
            await Assert.That(await subject.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await using (var reviewer = fixture.Reader(fixture.Database.ReviewerId))
            await Assert.That(await reviewer.DeliverDetail(fixture.ReviewerNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task FirstExplicitSubjectAcknowledgementStartsCalendarFloorAndExactReplayCannotRenewIt()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        fixture.Clock.UtcNow = Now.AddDays(2);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var operationId = Guid.CreateVersion7();
        var first = await reader.Acknowledge(fixture.SubjectNoticeId, operationId, fixture.SnapshotHash);
        fixture.Clock.UtcNow = Now.AddMonths(2);
        var replay = await reader.Acknowledge(fixture.SubjectNoticeId, operationId, fixture.SnapshotHash);
        var changedOperation = await reader.Acknowledge(fixture.SubjectNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash);
        var changedProof = await reader.Acknowledge(fixture.SubjectNoticeId, operationId, new string('b', 64));
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        await Assert.That(changedOperation.IsSuccess).IsFalse();
        await Assert.That(changedProof.IsSuccess).IsFalse();
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var receipt = await inspect.Set<ModerationNoticeNotificationReceipt>().SingleAsync();
        await Assert.That(receipt.NotifiedAt).IsEqualTo(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        await Assert.That(receipt.RemedyUntil).IsEqualTo(new DateTimeOffset(2027, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await Assert.That(receipt.RetainUntil).IsEqualTo(new DateTimeOffset(2027, 5, 8, 12, 0, 0, TimeSpan.Zero));
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    public async Task ReviewerCannotAcknowledgeOnBehalfOfTheSubject()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.ReviewerId);
        var result = await reader.Acknowledge(fixture.ReviewerNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task GenuineNotificationNeverExtendsTheOriginalHalfOpenPrivacyCeiling()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That((await reader.Acknowledge(fixture.SubjectNoticeId,
            Guid.CreateVersion7(), fixture.SnapshotHash)).IsSuccess).IsTrue();
        var ceiling = new DateTimeOffset(2027, 5, 8, 12, 0, 0, TimeSpan.Zero);
        fixture.Clock.UtcNow = ceiling.AddTicks(-1);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
        fixture.Clock.UtcNow = ceiling;
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    public async Task MissingNotificationCannotAcquireAShortenedCalendarFloorAtLateReadOrAcknowledgement()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(1);
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That((await reader.Acknowledge(fixture.SubjectNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash)).IsSuccess).IsFalse();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
        await Assert.That((await inspect.ModerationNoticeSources.SingleAsync()).RetainUntil)
            .IsEqualTo(new DateTimeOffset(2027, 5, 8, 12, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task FirstNotificationWindowCrossedDuringPreparationPreventsOutputBeforePrivacyCeiling()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync(
            (page, _) =>
            {
                fixture.Clock.UtcNow = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(1);
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(page));
            }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
    }

    [Test]
    [Arguments(false, "current")]
    [Arguments(false, "calendar-floor")]
    [Arguments(false, "recipient")]
    [Arguments(true, "current")]
    [Arguments(true, "calendar-floor")]
    [Arguments(true, "recipient")]
    public async Task AcknowledgementRequalifiesAfterPersistedDisclosureFinalization(bool content, string change)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(
            content: content, representativeUntil: Now.AddMonths(7));
        long? originalEpoch;
        long originalEffectRevision;
        await using (var before = fixture.Database.Context())
        {
            before.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            originalEpoch = await before.Set<EventDiscoveryRevision>().AsNoTracking()
                .Where(row => row.TenantId == fixture.Database.TenantId)
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
            originalEffectRevision = (await before.ModerationEffectIntents.AsNoTracking().SingleAsync()).Revision;
        }
        var advancement = new AcknowledgementDisclosureAdvancement(
            fixture.Database.TenantId, originalEpoch, async context =>
            {
                if (change == "calendar-floor")
                    fixture.Clock.UtcNow = Now.AddMonths(1).AddTicks(1);
                else if (change == "recipient")
                    await context.Users.Where(row => row.Id == fixture.Database.SubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            });
        var saved = new EnlistAcknowledgementDisclosure(fixture.Database.TenantId, advancement);
        await using var reader = fixture.Reader(fixture.Database.SubjectId, null, saved, advancement);
        var operationId = Guid.CreateVersion7();
        var result = await reader.Acknowledge(fixture.SubjectNoticeId, operationId, fixture.SnapshotHash);
        var current = change == "current";
        await Assert.That(saved.ObservedNotification).IsTrue();
        await Assert.That(advancement.ObservedAdvancement).IsTrue();
        await Assert.That(result.IsSuccess).IsEqualTo(current);
        await Assert.That(reader.Context.Database.CurrentTransaction).IsNull();

        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var receipt = await inspect.Set<ModerationNoticeNotificationReceipt>().AsNoTracking().SingleOrDefaultAsync();
        if (current)
        {
            await Assert.That(receipt!.Id).IsEqualTo(operationId);
            await Assert.That(receipt.NotifiedAt).IsEqualTo(Now);
            await Assert.That(receipt.RemedyUntil).IsEqualTo(Now.AddMonths(6));
            await Assert.That(receipt.AcknowledgedByHumanId).IsEqualTo(fixture.Database.SubjectId);
        }
        else
            await Assert.That(receipt).IsNull();
        var finalEpoch = await inspect.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == fixture.Database.TenantId)
            .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        await Assert.That(current ? finalEpoch > originalEpoch.GetValueOrDefault() : finalEpoch == originalEpoch).IsTrue();
        await Assert.That((await inspect.Users.IgnoreQueryFilters(["SoftDelete"]).AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Database.SubjectId)).IsDeleted).IsFalse();
        var effect = await inspect.ModerationEffectIntents.AsNoTracking().SingleAsync();
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.Revision).IsEqualTo(originalEffectRevision);
        var source = await inspect.ModerationNoticeSources.AsNoTracking().SingleAsync();
        await Assert.That(source.SnapshotHash).IsEqualTo(fixture.SnapshotHash);
        await Assert.That(source.RetainUntil).IsEqualTo(Now.AddMonths(7));
    }

    [Test]
    public async Task NotificationWindowCrossedAfterReceiptFlushRollsBackTheEntireAcknowledgement()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId,
            () => fixture.Clock.UtcNow = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero).AddTicks(1));
        var result = await reader.Acknowledge(fixture.SubjectNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(reader.Context.Database.CurrentTransaction).IsNull();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    public async Task CancellationOfOriginalQueryPreventsLaterPrivateProjectionUse()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Reader(fixture.Database.SubjectId);
        using var cancellation = new CancellationTokenSource();
        var pending = await reader.Detail(fixture.SubjectNoticeId, cancellation.Token);
        cancellation.Cancel();
        var delivered = false;
        await Assert.That(async () => await pending.Projection!.DiscloseAsync(
            (_, _) => Task.FromResult(0), (_, _) => { delivered = true; return Task.CompletedTask; }))
            .Throws<OperationCanceledException>();
        await Assert.That(delivered).IsFalse();
        await Assert.That(reader.Context.Database.CurrentTransaction).IsNull();
    }

    internal sealed partial class NoticeDatabase
    {
        internal ReaderSession Reader(Guid userId, Action? notificationSaved = null, params IInterceptor[] interceptors) =>
            new(this, userId, notificationSaved, interceptors);
    }

    internal sealed class ReaderSession : IAsyncDisposable
    {
        private readonly HttpContextAccessor _http = new() { HttpContext = new DefaultHttpContext() };
        private readonly GetModerationNoticeQueryHandler _detail;
        private readonly GetModerationNoticesQueryHandler _list;
        private readonly AcknowledgeModerationNoticeCommandHandler _acknowledge;
        private readonly GetModerationNoticeStatusQueryHandler _status;
        public ExploreDbContext Context { get; }
        public ReaderTenant Tenant { get; }

        public ReaderSession(NoticeDatabase fixture, Guid userId, Action? notificationSaved, IInterceptor[] interceptors,
            ModerationNoticePrivacyFixture? privacy = null)
        {
            Tenant = new(fixture.Database.TenantId);
            Context = fixture.Database.Context();
            if (notificationSaved is not null || interceptors.Length != 0)
            {
                var options = (DbContextOptions<ExploreDbContext>)Context.GetService<IDbContextOptions>();
                Context.Dispose();
                var builder = new DbContextOptionsBuilder<ExploreDbContext>(options).AddInterceptors(interceptors);
                if (notificationSaved is not null) builder.AddInterceptors(new NotificationSaved(notificationSaved));
                Context = new ExploreDbContext(builder.Options);
            }
            Context.TenantContext = Tenant;
            SetCaller(userId);
            var identity = new AdminContext(_http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(Context)),
                new OrganizationMemberRepository(Context),
                new GroupMemberRepository(Context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(Context), null), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(_http);
            var activation = new ModerationNativeActivationQualification(Context, fixture.Database.Inventory,
                null, new ModerationQualificationPolicySourceOwner(Context, fixture.Clock), fixture.Clock);
            var repository = new ModerationNoticeReadRepository(Context, identity, machine,
                new ModerationNoticeRecipientRepository(Context), activation, fixture.Clock,
                (privacy ?? fixture.Privacy).Authority(Context, fixture.Clock));
            _detail = new(repository, identity, machine, Tenant);
            _list = new(repository, identity, machine, Tenant);
            _acknowledge = new(repository, identity, machine, Tenant);
            _status = new(new ModerationNoticeStatusRepository(Context, identity, machine, fixture.Clock,
                (privacy ?? fixture.Privacy).Authority(Context, fixture.Clock)), identity, machine, Tenant);
        }

        public void SetCaller(Guid id, string scheme = "notice-native-witness") =>
            _http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())], scheme));
        public Task<ModerationNoticeReadResult> Detail(Guid id, CancellationToken token = default) => _detail.QueryAsync(new(id), token);
        public Task<ModerationNoticeReadResult> List() => _list.QueryAsync(new());
        public Task<Explore.Application.Responses.BaseCommandResponse<Guid>> Acknowledge(Guid id, Guid operationId, string hash) =>
            _acknowledge.ExecuteAsync(new(id, operationId, hash));
        public Task<ModerationNoticeStatusResult> Status(Guid decisionId, Guid effectId) =>
            _status.QueryAsync(new(decisionId, effectId));
        public async Task<ModerationNoticeReadOutcome> DeliverDetail(Guid id)
        {
            var pending = await Detail(id);
            return pending.Projection is null ? pending.Outcome
                : await pending.Projection.DiscloseAsync(
                    (page, _) => Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(page)), (_, _) => Task.CompletedTask);
        }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    internal sealed class ReaderTenant(Guid id) : ITenantContext
    {
        public Guid TenantId { get; set; } = id;
    }

    private sealed class EnlistAcknowledgementDisclosure(
        Guid tenantId, AcknowledgementDisclosureAdvancement advancement) : SaveChangesInterceptor
    {
        public bool ObservedNotification { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedNotification && eventData.Context is ExploreDbContext context
                && !context.ChangeTracker.HasChanges()
                && context.ChangeTracker.Entries<ModerationNoticeNotificationReceipt>().Any())
            {
                await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
                ObservedNotification = true;
                context.DisclosureMutations.Enlist([tenantId]);
                advancement.Armed = true;
            }
            return result;
        }
    }

    private sealed class AcknowledgementDisclosureAdvancement(
        Guid tenantId, long? originalEpoch, Func<ExploreDbContext, Task> afterAdvancement) : DbCommandInterceptor
    {
        private bool observing;
        public bool Armed { get; set; }
        public bool ObservedAdvancement { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || observing || ObservedAdvancement || eventData.Context is not ExploreDbContext context)
                return result;
            observing = true;
            try
            {
                var epoch = await context.Set<EventDiscoveryRevision>().AsNoTracking()
                    .Where(row => row.TenantId == tenantId).Select(row => (long?)row.DisclosureEpoch)
                    .SingleOrDefaultAsync(cancellationToken);
                if (epoch > originalEpoch.GetValueOrDefault())
                {
                    ObservedAdvancement = true;
                    await afterAdvancement(context);
                }
            }
            finally { observing = false; }
            return result;
        }
    }

    private sealed class NotificationSaved(Action saved) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ModerationNoticeNotificationReceipt>().Any())
                saved();
            return ValueTask.FromResult(result);
        }
    }
}
