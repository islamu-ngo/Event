using System.Data;
using System.Security.Claims;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    public async Task Adjudicated_case_grant_names_a_current_reader_without_substituting_original_notice_recipients()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
        {
            var repository = AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId);
            await Assert.That(await repository.GrantAsync(request with { ValidUntil = fixture.Clock.GetUtcNow() })).IsNull();
            await Assert.That(await repository.GrantAsync(request with
            { ValidUntil = original.Source.RetainUntil!.Value.AddMicroseconds(1) })).IsNull();
            await Assert.That(await repository.GrantAsync(request with { GranteeUserId = Guid.CreateVersion7() })).IsNull();
            await Assert.That(await repository.GrantAsync(request with
            { Original = request.Original with { EffectId = fixture.EffectId } })).IsNull();
            await Assert.That(await repository.GrantAsync(request with
            { Original = request.Original with { SnapshotHash = new string('0', 64) } })).IsNull();
            await Assert.That(await repository.GrantAsync(request with
            { Original = request.Original with { EffectBindingHash = new string('0', 64) } })).IsNull();
            var granted = await repository.GrantAsync(request);
            await Assert.That(granted).IsNotNull();
            await Assert.That(granted!.Id).IsEqualTo(request.OperationId);
            await Assert.That(granted.RecipientUserId).IsEqualTo(fixture.Database.UrgencyAssessorId);
            await Assert.That(granted.IssuerUserId).IsEqualTo(fixture.Database.AdministratorId);
            await Assert.That(granted.CaseId).IsEqualTo(request.CaseId);
            await Assert.That(granted.CaseRevision).IsEqualTo(request.ExpectedCaseRevision);
            await Assert.That(granted.OriginalEffectId).IsEqualTo(original.Effect.Id);
            await Assert.That(granted.OriginalSnapshotHash).IsEqualTo(original.Source.SnapshotHash);
            await Assert.That(granted.OriginalEffectBindingHash).IsEqualTo(original.Source.EffectBindingHash);
            await Assert.That(granted.OriginalRetainUntil).IsEqualTo(original.Source.RetainUntil!.Value);
            await Assert.That(granted.ExpiresAt).IsEqualTo(request.ValidUntil);
            await Assert.That(granted.Revision).IsEqualTo(1L);
            await Assert.That(granted.IssuerPlatformGrant).IsTrue();
            await Assert.That(await writer.PlatformUserRoles.AnyAsync(row => row.Id == granted.IssuerRoleGrantId
                && row.UserId == fixture.Database.AdministratorId)).IsTrue();
        }

        await using (var reader = fixture.Database.Context())
        {
            var repository = AdjudicatedGrants(reader, fixture, request.GranteeUserId);
            await Assert.That(await repository.ReadCurrentAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision)).IsNotNull();
            await Assert.That(await repository.ReadCurrentAsync(Guid.CreateVersion7(), request.CaseId,
                request.ExpectedCaseRevision)).IsNull();
            await Assert.That(await repository.ReadCurrentAsync(request.OperationId, Guid.CreateVersion7(),
                request.ExpectedCaseRevision)).IsNull();
            await Assert.That(await repository.ReadCurrentAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision + 1)).IsNull();
        }
        foreach (var caller in new[] { fixture.Database.SubjectId, fixture.Database.ReviewerId, fixture.Database.AdministratorId })
        {
            await using var reader = fixture.Database.Context();
            await Assert.That(await AdjudicatedGrants(reader, fixture, caller)
                .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNull();
        }
        await using (var noticeReader = fixture.Reader(request.GranteeUserId))
            foreach (var notice in original.Notices)
                await Assert.That(await noticeReader.DeliverDetail(notice.Id)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);

        await using (var allegationReader = fixture.Database.Context())
        {
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, request.GranteeUserId.ToString())], "adjudicated-native-witness"));
            var owner = new ModerationCaseTargetProofOwner(allegationReader, new CurrentUserService(http),
                new MachinePrincipalAccessor(http), fixture.Clock);
            await using var transaction = await allegationReader.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await owner.IssueReadAsync(request.OperationId, default)).IsNull();
            await Assert.That(await allegationReader.ModerationCaseReadGrants.AnyAsync()).IsFalse();
            await Assert.That(await allegationReader.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        }
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    [Test]
    public async Task Adjudicated_case_grant_replays_only_identical_inputs_and_never_resurrects_after_revocation()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        var otherRequest = request with { OperationId = Guid.CreateVersion7(), GranteeUserId = fixture.Database.SubjectId };
        Guid revokeId = Guid.CreateVersion7();
        await using (var writer = fixture.Database.Context())
        {
            var repository = AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId);
            await Assert.That(await repository.GrantAsync(request)).IsNotNull();
            await Assert.That(await repository.GrantAsync(otherRequest)).IsNotNull();
            var replay = await repository.GrantAsync(request);
            await Assert.That(replay).IsNotNull();
            await Assert.That(replay!.Id).IsEqualTo(request.OperationId);
            await Assert.That(replay.Revision).IsEqualTo(1L);
            await Assert.That(await repository.GrantAsync(request with { GranteeUserId = fixture.Database.SubjectId })).IsNull();
            await Assert.That(await repository.GrantAsync(request with { ValidUntil = request.ValidUntil.AddMinutes(1) })).IsNull();
            await Assert.That(await repository.RevokeAsync(request.OperationId, revokeId, 2)).IsNull();
            var revoked = await repository.RevokeAsync(request.OperationId, revokeId, 1);
            await Assert.That(revoked).IsNotNull();
            await Assert.That(revoked!.RevokedAt).IsNotNull();
            await Assert.That(revoked.Revision).IsEqualTo(2L);
            var revokedReplay = await repository.RevokeAsync(request.OperationId, revokeId, 1);
            await Assert.That(revokedReplay).IsNotNull();
            await Assert.That(revokedReplay!.RevokedAt).IsEqualTo(revoked.RevokedAt);
            await Assert.That(revokedReplay.Revision).IsEqualTo(2L);
            await Assert.That(await repository.RevokeAsync(request.OperationId, revokeId, 2)).IsNull();
            await Assert.That(await repository.RevokeAsync(request.OperationId, Guid.CreateVersion7(), 1)).IsNull();
            await Assert.That(await repository.RevokeAsync(otherRequest.OperationId, revokeId, 1)).IsNull();
            var replayAfterRevocation = await repository.GrantAsync(request);
            await Assert.That(replayAfterRevocation is null || replayAfterRevocation.RevokedAt is not null).IsTrue();
        }
        await using var reader = fixture.Database.Context();
        await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
            .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNull();
        await using var otherReader = fixture.Database.Context();
        await Assert.That(await AdjudicatedGrants(otherReader, fixture, otherRequest.GranteeUserId)
            .ReadCurrentAsync(otherRequest.OperationId, otherRequest.CaseId, otherRequest.ExpectedCaseRevision)).IsNotNull();
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    [Test]
    public async Task Adjudicated_case_read_stops_at_the_exact_original_custody_ceiling()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original) with { ValidUntil = original.Source.RetainUntil!.Value };
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        fixture.Clock.UtcNow = request.ValidUntil.AddMicroseconds(-1);
        await using (var reader = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
                .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNotNull();
        fixture.Clock.UtcNow = request.ValidUntil;
        await using (var reader = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
                .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNull();
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNull();
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    [Test]
    public async Task A_real_pending_correction_source_cannot_replace_the_original_creating_custody_anchor()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync(row => row.Id == fixture.DecisionId);
            var operation = ModerationOperation.Transition(ModerationOperationKind.Revoke, decision.Subject, decision.Scope,
                Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, original.Effect.SanctionRevision!.Value,
                original.Effect.PolicyRevision, original.Effect.SanctionId!.Value);
            writer.ChangeTracker.Clear();
            writer.ModerationTrustDomainId = null;
            await Assert.That((await CorrectionService(writer, fixture).ExecuteAsync(operation)).Outcome)
                .IsEqualTo(ModerationOperationOutcome.Known);
            await using var inspect = fixture.Database.Context();
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var correction = await inspect.ModerationNoticeSources.SingleAsync(row => row.EffectId == operation.EffectId);
            request = request with
            {
                Original = new ModerationOriginalEffectAnchor(correction.EffectId, correction.SnapshotHash,
                    correction.EffectBindingHash)
            };
            await Assert.That(await inspect.ModerationNotices.CountAsync(row => row.EffectId == correction.EffectId))
                .IsEqualTo(original.Notices.Length);
            await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == correction.EffectId
                && row.DeliveryDisposition != ModerationNoticeDeliveryDisposition.RecipientUnavailable)).IsFalse();
        }
        await using var issuer = fixture.Database.Context();
        await Assert.That(await AdjudicatedGrants(issuer, fixture, fixture.Database.AdministratorId)
            .GrantAsync(request)).IsNull();
        await Assert.That(await issuer.Set<ModerationAdjudicatedCaseReadGrant>().AnyAsync()).IsFalse();
        await Assert.That(await issuer.NotificationIntents.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task An_original_recipient_without_current_administration_cannot_issue_or_revoke_a_case_grant()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var unprivileged = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(unprivileged, fixture, fixture.Database.SubjectId)
                .GrantAsync(request)).IsNull();
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        await using (var unprivileged = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(unprivileged, fixture, fixture.Database.SubjectId)
                .RevokeAsync(request.OperationId, Guid.CreateVersion7(), 1)).IsNull();
        await using var reader = fixture.Database.Context();
        await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
            .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNotNull();
    }

    [Test]
    public async Task Replacing_the_issuing_native_admin_role_does_not_transfer_a_grants_authority_to_the_new_role()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        Guid originalRoleId;
        await using (var writer = fixture.Database.Context())
        {
            var granted = await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId).GrantAsync(request);
            await Assert.That(granted).IsNotNull();
            originalRoleId = granted!.IssuerRoleGrantId;
        }
        await fixture.MutateAsync(async context =>
        {
            var role = await context.PlatformUserRoles.AsNoTracking().SingleAsync(row => row.Id == originalRoleId);
            await context.PlatformUserRoles.Where(row => row.Id == originalRoleId).ExecuteDeleteAsync();
            context.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = role.UserId,
                RoleId = role.RoleId,
                User = null!,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
        });
        await using (var reader = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
                .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNull();
        await using var writerAfterReplacement = fixture.Database.Context();
        var replacementAdmin = AdjudicatedGrants(writerAfterReplacement, fixture, fixture.Database.AdministratorId);
        await Assert.That(await replacementAdmin.GrantAsync(request)).IsNull();
        await Assert.That(await replacementAdmin.RevokeAsync(request.OperationId, Guid.CreateVersion7(), 1)).IsNotNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Current_case_or_original_custody_drift_invalidates_an_existing_adjudicated_grant(bool caseRevision)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        await fixture.MutateAsync(async context =>
        {
            if (caseRevision)
                (await context.ModerationCases.SingleAsync(row => row.Id == request.CaseId))
                    .Close(request.ExpectedCaseRevision, fixture.Clock.GetUtcNow());
            else
                await context.ModerationNoticeSources.Where(row => row.EffectId == original.Effect.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SnapshotHash, new string('0', 64)));
        });
        await using var reader = fixture.Database.Context();
        var repository = AdjudicatedGrants(reader, fixture, request.GranteeUserId);
        await Assert.That(await repository.ReadCurrentAsync(request.OperationId, request.CaseId,
            request.ExpectedCaseRevision)).IsNull();
        if (caseRevision)
            await Assert.That(await repository.ReadCurrentAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision + 1)).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Adjudicated_grant_post_flush_authority_or_expiry_loss_rolls_back_without_notice_side_effects(
        bool colocatedPrivacy, bool expire)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using var primary = fixture.Database.Context();
        await using var colocated = colocatedPrivacy ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary) : null;
        var probe = new InvalidateAdjudicatedGrantAfterSave(request.OperationId, fixture.Clock, request.ValidUntil, expire);
        await using (var writer = fixture.Database.Context(probe))
        {
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId, colocated)
                .GrantAsync(request)).IsNull();
            await Assert.That(probe.Observed).IsTrue();
            await Assert.That(writer.Database.CurrentTransaction).IsNull();
            await Assert.That(writer.ChangeTracker.HasChanges()).IsFalse();
        }
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationAdjudicatedCaseReadGrant>()
            .AnyAsync(row => row.Id == request.OperationId)).IsFalse();
        await Assert.That(await inspect.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.Database.AdministratorId)).IsTrue();
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    [Test]
    public async Task Adjudicated_grant_rejects_dirty_caller_context_without_discarding_the_callers_edit()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using var context = fixture.Database.Context();
        var tenant = await context.Tenants.SingleAsync(row => row.Id == fixture.Database.TenantId);
        tenant.FullName = "Caller-owned unsaved tenant edit";
        var repository = AdjudicatedGrants(context, fixture, fixture.Database.AdministratorId);
        await Assert.That(async () => await repository.GrantAsync(request)).Throws<InvalidOperationException>();
        await Assert.That(async () => await repository.RevokeAsync(request.OperationId, Guid.CreateVersion7(), 1))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await repository.ReadCurrentAsync(request.OperationId, request.CaseId,
            request.ExpectedCaseRevision)).Throws<InvalidOperationException>();
        await Assert.That(context.Entry(tenant).State).IsEqualTo(EntityState.Modified);
        await Assert.That(tenant.FullName).IsEqualTo("Caller-owned unsaved tenant edit");
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.Tenants.SingleAsync(row => row.Id == fixture.Database.TenantId)).FullName)
            .IsNotEqualTo(tenant.FullName);
    }

    [Test]
    public async Task Retained_erasure_of_the_original_subject_blocks_current_grants_without_a_primary_erasure_saga()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        await using (var primary = fixture.Database.Context())
        {
            await fixture.Privacy.Authority(primary, fixture.Clock).AppendAsync(new(Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User, fixture.Database.SubjectId, PrivacyErasureReasonCode.AccountDeletion, 1));
            await Assert.That(await primary.PrivacyErasureSagas.AnyAsync()).IsFalse();
        }
        await using (var reader = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
                .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNull();
        await using var issuer = fixture.Database.Context();
        await Assert.That(await AdjudicatedGrants(issuer, fixture, fixture.Database.AdministratorId)
            .GrantAsync(request with { OperationId = Guid.CreateVersion7() })).IsNull();
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    private static async Task AssertOriginalAdjudicatedLedgerUnchangedAsync(
        NoticeDatabase fixture, CorrectionCreatingLedger original)
    {
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = await inspect.ModerationNoticeSources.SingleAsync(row => row.EffectId == original.Effect.Id);
        await Assert.That(source.SnapshotHash).IsEqualTo(original.Source.SnapshotHash);
        await Assert.That(source.EffectBindingHash).IsEqualTo(original.Source.EffectBindingHash);
        await Assert.That(source.RetainUntil).IsEqualTo(original.Source.RetainUntil);
        var notices = await inspect.ModerationNotices.Where(row => row.EffectId == original.Effect.Id).ToArrayAsync();
        await Assert.That(notices.Length).IsEqualTo(original.Notices.Length);
        foreach (var originalNotice in original.Notices)
        {
            var notice = notices.Single(row => row.Id == originalNotice.Id);
            await Assert.That(notice.Audience).IsEqualTo(originalNotice.Audience);
            await Assert.That(notice.RecipientSubjectKind).IsEqualTo(originalNotice.RecipientSubjectKind);
            await Assert.That(notice.RecipientSubjectId).IsEqualTo(originalNotice.RecipientSubjectId);
            await Assert.That(notice.DeliveryDisposition).IsEqualTo(originalNotice.DeliveryDisposition);
            await Assert.That(notice.NativeIntentId).IsNull();
        }
        await Assert.That(await inspect.NotificationIntents.AnyAsync()).IsFalse();
        await Assert.That(await inspect.Notifications.AnyAsync()).IsFalse();
        await Assert.That(await inspect.OutboxMessages.AnyAsync()).IsFalse();
    }

    private sealed class InvalidateAdjudicatedGrantAfterSave(Guid grantId, TestClock clock,
        DateTimeOffset expiresAt, bool expire) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationAdjudicatedCaseReadGrant>().Any(row => row.Entity.Id == grantId))
            {
                Observed = true;
                if (expire) clock.UtcNow = expiresAt;
                else
                {
                    var grant = context.ChangeTracker.Entries<ModerationAdjudicatedCaseReadGrant>()
                        .Single(row => row.Entity.Id == grantId).Entity;
                    await context.PlatformUserRoles.Where(row => row.Id == grant.IssuerRoleGrantId)
                        .ExecuteDeleteAsync(cancellationToken);
                }
            }
            return result;
        }
    }
}
