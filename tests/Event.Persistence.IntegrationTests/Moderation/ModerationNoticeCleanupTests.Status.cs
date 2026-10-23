using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Constants;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Moderation.Notices;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeCleanupTests
{
    [Test]
    public async Task Status_selects_only_original_minimal_metadata_not_private_capture_or_evidence()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(acknowledged: true);
        fixture.Clock.UtcNow = OriginalAt.AddMonths(2);
        await using (var inspect = fixture.Context())
        {
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var source = await inspect.ModerationNoticeSources.SingleAsync();
            var metadata = ModerationNoticeSource.StatusMetadata.Read(source.StatusMetadataJson, source.StatusMetadataHash);
            await Assert.That(metadata).IsNotNull();
            await Assert.That(metadata!.WriterTenantId).IsEqualTo(fixture.Database.TenantId);
            await Assert.That(metadata.HostBindingId).IsEqualTo(fixture.Database.HostId);
            await Assert.That(metadata.ContestationCalendarMonths).IsEqualTo(6);
            await Assert.That(metadata.SnapshotHash).IsEqualTo(source.SnapshotHash);
            await Assert.That(source.StatusMetadataJson).DoesNotContain("Facts");
            await Assert.That(source.StatusMetadataJson).DoesNotContain("Finding");
            await Assert.That(source.StatusMetadataJson).DoesNotContain("Reviewer");
            var membership = await inspect.TenantUsers.SingleAsync(row => row.UserId == fixture.Database.SubjectId);
            membership.IsDeleted = true;
            await inspect.SaveChangesAsync();
        }
        await using var reader = await StatusReader.CreateAsync(fixture, effectId, new RejectPrivateStatusReads());
        await Assert.That((await reader.ReadAsync()).NotificationProof).IsEqualTo("Proven");
    }

    [Test]
    [Arguments("metadata")]
    [Arguments("hash")]
    [Arguments("receipt")]
    public async Task Corrupt_minimal_metadata_cannot_publish_notification_facts(string defect)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(acknowledged: true);
        fixture.Clock.UtcNow = OriginalAt.AddMonths(2);
        await using (var mutation = fixture.Context())
        {
            mutation.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            if (defect == "metadata")
                await mutation.ModerationNoticeSources.Where(row => row.EffectId == effectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusMetadataJson, "{}"));
            else if (defect == "hash")
                await mutation.ModerationNoticeSources.Where(row => row.EffectId == effectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SnapshotHash, new string('e', 64)));
            else
                await mutation.Set<ModerationNoticeNotificationReceipt>().Where(row => row.EffectId == effectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ContestationCalendarMonths, 1)
                        .SetProperty(row => row.RemedyUntil, new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero)));
        }
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        var dto = await reader.ReadAsync();
        await Assert.That(dto.NotificationProof).IsEqualTo("Unavailable");
        await Assert.That(dto.NotifiedAt).IsNull();
        await Assert.That(dto.RemedyFloorUntil).IsNull();
        await Assert.That(dto.OriginalContestationCalendarMonths).IsNull();
        await Assert.That(dto.OriginalRetainUntil).IsEqualTo(defect == "receipt" ? Ceiling : (DateTimeOffset?)null);
    }

    [Test]
    public async Task Original_writer_tenant_cannot_be_replaced_by_current_tenant_or_subject_membership()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(defect: "wrong-writer");
        fixture.Clock.UtcNow = OriginalAt.AddMonths(2);
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        await Assert.That(await reader.DeliverAsync()).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Foreign_original_subject_ancestry_denies_even_purged_status(bool purged)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync();
        if (purged)
        {
            await using var writer = fixture.Context();
            await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        }
        await using (var mutation = fixture.Context())
        {
            mutation.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            await mutation.ModerationDecisions.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.SubjectOriginInstanceId, (Guid?)Guid.CreateVersion7()));
        }
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        await Assert.That(await reader.DeliverAsync()).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    public async Task Late_unnotified_subject_retains_status_without_a_fictional_notification_or_expiry()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync();
        fixture.Clock.UtcNow = OriginalAt.AddMonths(2);
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        var dto = await reader.ReadAsync();
        await Assert.That(dto.NotificationProof).IsEqualTo("Unproven");
        await Assert.That(dto.NotifiedAt).IsNull();
        await Assert.That(dto.RemedyFloorUntil).IsNull();
        await Assert.That(dto.OriginalContestationCalendarMonths).IsEqualTo(6);
        await Assert.That(dto.OriginalRetainUntil).IsEqualTo(Ceiling);
        var json = JsonSerializer.Serialize(dto);
        await Assert.That(json).DoesNotContain("SnapshotHash");
        await Assert.That(json).DoesNotContain("Justification");
        await Assert.That(json).DoesNotContain("QualifiedRecipientId");
    }

    [Test]
    public async Task Revoked_text_and_elapsed_floor_do_not_destroy_genuine_notification_facts()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(acknowledged: true);
        fixture.Clock.UtcNow = OriginalAt.AddMonths(6).AddDays(1);
        await using (var mutation = fixture.Context())
        {
            mutation.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var approval = await mutation.ModerationNoticeDisclosureApprovals.SingleAsync();
            approval.Revoke(approval.Revision, fixture.Database.AdministratorId, fixture.Clock.UtcNow);
            await mutation.SaveChangesAsync();
        }
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        var dto = await reader.ReadAsync();
        await Assert.That(dto.NotificationProof).IsEqualTo("Proven");
        await Assert.That(dto.NotifiedAt).IsEqualTo(OriginalAt);
        await Assert.That(dto.RemedyFloorUntil).IsEqualTo(new DateTimeOffset(2027, 4, 8, 12, 0, 0, TimeSpan.Zero));
        await Assert.That(dto.OriginalRetainUntil).IsEqualTo(Ceiling);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Purpose_cleanup_never_turns_receipt_absence_into_never_notified(bool acknowledged)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(acknowledged: acknowledged);
        await using (var writer = fixture.Context())
            await Assert.That((await fixture.Repository(writer, await fixture.AuthenticateAsync(writer))
                .PruneAsync(null, 10)).OriginalWritesProven).IsEqualTo(1);
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        var dto = await reader.ReadAsync();
        await Assert.That(dto.NotificationProof).IsEqualTo("PurposePurged");
        await Assert.That(dto.NotifiedAt).IsNull();
        await Assert.That(dto.RemedyFloorUntil).IsNull();
        await Assert.That(dto.OriginalContestationCalendarMonths).IsNull();
        await Assert.That(dto.OriginalRetainUntil).IsEqualTo(Ceiling);
    }

    [Test]
    [Arguments("missing-source")]
    [Arguments("wrong-hash")]
    public async Task Unproved_cleanup_returns_unavailable_proof_without_inventing_a_ceiling(string defect)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(defect: defect);
        await using (var writer = fixture.Context())
            await Assert.That((await fixture.Repository(writer, await fixture.AuthenticateAsync(writer))
                .PruneAsync(null, 10)).OriginalWritesProven).IsEqualTo(0);
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        var dto = await reader.ReadAsync();
        await Assert.That(dto.NotificationProof).IsEqualTo("Unavailable");
        await Assert.That(dto.OriginalRetainUntil).IsNull();
        await Assert.That(dto.OriginalContestationCalendarMonths).IsNull();
    }

    [Test]
    public async Task Ceiling_before_cleanup_withholds_retained_receipt_dates()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(acknowledged: true);
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        var dto = await reader.ReadAsync();
        await Assert.That(dto.NotificationProof).IsEqualTo("Unavailable");
        await Assert.That(dto.NotifiedAt).IsNull();
        await Assert.That(dto.RemedyFloorUntil).IsNull();
        await Assert.That(dto.OriginalContestationCalendarMonths).IsNull();
        await Assert.That(dto.OriginalRetainUntil).IsEqualTo(Ceiling);
    }

    [Test]
    public async Task Foreign_human_or_deleted_subject_cannot_borrow_stable_ledger_coordinates()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync();
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        reader.SetCaller(fixture.Database.AdministratorId);
        await Assert.That(await reader.DeliverAsync()).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        reader.SetCaller(fixture.Database.SubjectId);
        await using (var mutation = fixture.Context())
        {
            var subject = await mutation.Users.SingleAsync(row => row.Id == fixture.Database.SubjectId);
            subject.IsDeleted = true;
            await mutation.SaveChangesAsync();
        }
        await Assert.That(await reader.DeliverAsync()).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt)]
    [Arguments("ModerationRecognitionCorrectionReceipt")]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey)]
    public async Task Other_purpose_proofs_never_become_notice_status_authority(string scheme)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync();
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        reader.SetCaller(fixture.Database.SubjectId, scheme);
        await Assert.That((await reader.QueryAsync()).Outcome).IsEqualTo(ModerationNoticeReadOutcome.Unauthenticated);
    }

    [Test]
    [Arguments("identity")]
    [Arguments("ceiling")]
    [Arguments("tenant")]
    public async Task Change_during_private_serialization_prevents_output_and_projection_replay(string change)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(acknowledged: true);
        fixture.Clock.UtcNow = Ceiling.AddTicks(-1);
        await using var reader = await StatusReader.CreateAsync(fixture, effectId);
        var pending = await reader.QueryAsync();
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync(
            (dto, _) =>
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(dto);
                if (change == "identity") reader.SetCaller(fixture.Database.AdministratorId);
                if (change == "ceiling") fixture.Clock.UtcNow = Ceiling;
                if (change == "tenant") reader.Context.TenantContext = null;
                return Task.FromResult(bytes);
            }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await Assert.That(await pending.Projection.DiscloseAsync(
            (_, _) => Task.FromResult(0), (_, _) => Task.CompletedTask)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(reader.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(reader.Context.ModerationTrustDomainId).IsNull();
    }

    private sealed class StatusReader : IAsyncDisposable
    {
        private readonly HttpContextAccessor _http = new() { HttpContext = new DefaultHttpContext() };
        private readonly GetModerationNoticeStatusQueryHandler _handler;
        private readonly GetModerationNoticeStatusQuery _query;
        public ExploreDbContext Context { get; }

        private StatusReader(CleanupDatabase fixture, Guid effectId, Guid decisionId, IInterceptor[] interceptors)
        {
            Context = fixture.Context(interceptors);
            SetCaller(fixture.Database.SubjectId);
            var identity = new AdminContext(_http, new PlatformUserRoleRepository(Context),
                new TenantUserRoleGrantRepository(Context), new OrganizationMemberRepository(Context),
                new GroupMemberRepository(Context), new UserExternalLoginRepository(Context), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(_http);
            _handler = new(new ModerationNoticeStatusRepository(Context, identity, machine, fixture.Clock,
                fixture.Privacy.Authority(Context, fixture.Clock)),
                identity, machine, fixture.Database);
            _query = new(decisionId, effectId);
        }

        public static async Task<StatusReader> CreateAsync(
            CleanupDatabase fixture, Guid effectId, params IInterceptor[] interceptors)
        {
            await using var context = fixture.Context();
            context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var decisionId = await context.ModerationEffectIntents.Where(row => row.Id == effectId)
                .Select(row => row.DecisionId).SingleAsync();
            return new(fixture, effectId, decisionId, interceptors);
        }

        public void SetCaller(Guid id, string scheme = "status-native-witness") =>
            _http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(scheme == "ModerationRecognitionCorrectionReceipt"
                    ? "recognition_correction_intent_id" : ClaimTypes.NameIdentifier, id.ToString())], scheme));
        public Task<ModerationNoticeStatusResult> QueryAsync() => _handler.QueryAsync(_query);
        public async Task<ModerationNoticeReadOutcome> DeliverAsync()
        {
            var pending = await QueryAsync();
            return pending.Projection is null ? pending.Outcome
                : await pending.Projection.DiscloseAsync((dto, _) => Task.FromResult(dto), (_, _) => Task.CompletedTask);
        }
        public async Task<ModerationNoticeStatusDto> ReadAsync()
        {
            var pending = await QueryAsync();
            ModerationNoticeStatusDto? result = null;
            var outcome = await pending.Projection!.DiscloseAsync(
                (dto, _) => Task.FromResult(dto), (dto, _) => { result = dto; return Task.CompletedTask; });
            await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.Allowed);
            return result!;
        }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class RejectPrivateStatusReads : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var forbidden in new[] { "snapshot_json", "moderation_findings", "moderation_case_evidence",
                "moderation_notice_disclosure_approvals" })
                if (command.CommandText.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Minimal status attempted to read private original payload.");
            return ValueTask.FromResult(result);
        }
    }
}
