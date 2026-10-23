using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_measured_report_has_native_receipts_but_missing_operations_remain_unqualified(bool replace)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalMeasuredRequestAsync(fixture);
        Guid? replacement = replace ? await ReplaceSelectedPublicationAsync(fixture) : null;
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        var database = fixture.Database;
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, record)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, record)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId,
            record with { ValidUntil = record.ValidUntil.AddTicks(1) })).IsSuccess).IsFalse();
        var accept = await OriginalMeasuredAcceptanceAsync(fixture, record);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, accept)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, accept)).IsSuccess).IsTrue();
        var observed = await ObserveOriginalMeasuredAsync(fixture, record, original: true);
        await Assert.That(observed.Source).IsNotNull();
        await Assert.That(observed.Acceptance?.Acceptance.Id).IsEqualTo(accept.AcceptanceId);
        await Assert.That(observed.Acceptance?.Acceptance.SourceRevision).IsEqualTo(1L);
        await Assert.That(observed.Acceptance?.Acceptance.SourceWriterGeneration)
            .IsEqualTo(record.ExpectedQualificationRevision + 1);
        await Assert.That(observed.Source!.Source.Evidence.Staffing).IsNotNull();
        await Assert.That(observed.Source.Source.Evidence.Recovery).IsNull();
        await Assert.That(observed.Source.Source.Evidence.KeyConvergence).IsNull();
        await Assert.That(observed.Source.Source.Evidence.Transport).IsNull();
        await Assert.That(observed.Source.Source.Evidence.Consumers.All(row => row.Measurement is null)).IsTrue();
        await Assert.That(observed.Prerequisite).IsNull();
        var ordinary = await ObserveOriginalMeasuredAsync(fixture, record, original: false);
        await Assert.That(ordinary.Source).IsNull();
        await Assert.That(ordinary.Acceptance).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var source = await reader.Set<ModerationMeasuredOperationsSource>().SingleAsync(row => row.Id == record.SourceId);
        await Assert.That(source.OriginalEffectId).IsEqualTo(fixture.EffectId);
        await Assert.That(source.OriginalSnapshotHash).IsEqualTo(record.OriginalEffect!.SnapshotHash);
        await Assert.That(source.OriginalEffectBindingHash).IsEqualTo(record.OriginalEffect.EffectBindingHash);
        await Assert.That(source.OriginalCustodyUntil).IsEqualTo(fixture.PurposeDeadline);
        await Assert.That(source.DefinitionId).IsEqualTo(record.Binding.PublicationId);
        await Assert.That(source.PublicationId).IsEqualTo(record.Binding.PublicationId);
        await Assert.That(source.ProfileId).IsEqualTo(record.ProfileId);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync(row =>
            row.Id == record.OperationId || row.Id == accept.OperationId)).IsEqualTo(2);
        if (replacement is { } selected)
            await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).LatestPublicationId).IsEqualTo(selected);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("missing-anchor")]
    [Arguments("effect")]
    [Arguments("snapshot")]
    [Arguments("binding-hash")]
    [Arguments("publication")]
    [Arguments("revision")]
    [Arguments("profile")]
    [Arguments("mode")]
    [Arguments("capability")]
    [Arguments("extended")]
    [Arguments("expired")]
    [Arguments("erasure")]
    public async Task Original_measured_writer_rejects_other_purposes_without_source_or_receipt(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalMeasuredRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        record = change switch
        {
            "missing-anchor" => record with { OriginalEffect = null },
            "effect" => record with { OriginalEffect = record.OriginalEffect! with { EffectId = Guid.CreateVersion7() } },
            "snapshot" => record with { OriginalEffect = record.OriginalEffect! with { SnapshotHash = new string('a', 64) } },
            "binding-hash" => record with { OriginalEffect = record.OriginalEffect! with { EffectBindingHash = new string('b', 64) } },
            "publication" => record with { Binding = record.Binding with { PublicationId = Guid.CreateVersion7() } },
            "revision" => record with { Binding = record.Binding with { PolicyRevision = record.Binding.PolicyRevision + 1 } },
            "profile" => record with { ProfileId = Guid.CreateVersion7() },
            "mode" => record with { Binding = record.Binding with { Mode = ModerationNativeMode.RemoteStrict } },
            "capability" => record with { Binding = record.Binding with { Capability = ModerationCapability.Publish } },
            "extended" => record with { ValidUntil = fixture.PurposeDeadline.AddTicks(1) },
            _ => record
        };
        if (change == "expired") fixture.Database.EvaluationTime = fixture.PurposeDeadline;
        if (change == "erasure") await EraseOriginalParticipantAsync(fixture, "representative");
        var epoch = await OriginalEpochAsync(fixture);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationMeasuredOperationsSource>().AnyAsync(row => row.Id == record.SourceId)).IsFalse();
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(epoch);
    }

    [Test]
    [Arguments("missing-anchor")]
    [Arguments("effect")]
    [Arguments("snapshot")]
    [Arguments("source-revision")]
    [Arguments("source-generation")]
    [Arguments("recorder")]
    [Arguments("extended")]
    public async Task Original_measured_acceptance_requires_exact_source_purpose_and_independent_generation(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalMeasuredRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsTrue();
        var accept = await OriginalMeasuredAcceptanceAsync(fixture, record);
        accept = change switch
        {
            "missing-anchor" => accept with { OriginalEffect = null },
            "effect" => accept with { OriginalEffect = accept.OriginalEffect! with { EffectId = Guid.CreateVersion7() } },
            "snapshot" => accept with { OriginalEffect = accept.OriginalEffect! with { SnapshotHash = new string('a', 64) } },
            "source-revision" => accept with { ExpectedSourceRevision = 2 },
            "source-generation" => accept with { ExpectedSourceWriterGeneration = accept.ExpectedSourceWriterGeneration + 1 },
            "extended" => accept with { ValidUntil = record.ValidUntil.AddTicks(1) },
            _ => accept
        };
        var actor = change == "recorder" ? fixture.Database.AdministratorId : fixture.Database.UrgencyAssessorId;
        await Assert.That((await fixture.Database.ExecuteAsync(actor, accept)).IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationMeasuredOperationsAcceptance>().AnyAsync(row => row.Id == accept.AcceptanceId)).IsFalse();
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == accept.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision)
            .IsEqualTo(accept.ExpectedQualificationRevision);
    }

    [Test]
    [Arguments("source-receipt")]
    [Arguments("source-digest")]
    [Arguments("acceptance-receipt")]
    [Arguments("acceptance-digest")]
    [Arguments("custody")]
    [Arguments("snapshot")]
    [Arguments("staffing-withdrawn")]
    [Arguments("reviewer-departed")]
    [Arguments("source-expiry")]
    [Arguments("acceptance-expiry")]
    [Arguments("original-expiry")]
    public async Task Original_measured_observation_reconstructs_native_receipts_and_rechecks_live_producers(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalMeasuredRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsTrue();
        var accept = await OriginalMeasuredAcceptanceAsync(fixture, record);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, accept)).IsSuccess).IsTrue();
        await using (var writer = fixture.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var receiptId = change.StartsWith("acceptance", StringComparison.Ordinal) ? accept.OperationId : record.OperationId;
            if (change is "source-receipt" or "acceptance-receipt")
                await writer.ModerationQualificationMutationReceipts.Where(row => row.Id == receiptId).ExecuteDeleteAsync();
            if (change is "source-digest" or "acceptance-digest")
                await writer.ModerationQualificationMutationReceipts.Where(row => row.Id == receiptId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RequestDigest, new string('0', 64)));
            if (change == "custody")
                await writer.Set<ModerationMeasuredOperationsSource>().Where(row => row.Id == record.SourceId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OriginalCustodyUntil, fixture.PurposeDeadline.AddTicks(-1)));
            if (change == "snapshot")
                await writer.Set<ModerationMeasuredOperationsSource>().Where(row => row.Id == record.SourceId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OriginalSnapshotHash, new string('0', 64)));
            if (change == "reviewer-departed")
                await writer.PlatformUserRoles.Where(row => row.UserId == record.ReviewerHumanId).ExecuteDeleteAsync();
            if (change == "staffing-withdrawn")
            {
                var adoption = await writer.Set<ModerationConstitutionAdoption>().SingleAsync();
                adoption.Withdraw(1, fixture.Database.AdministratorId, Now);
                await writer.SaveChangesAsync();
            }
        }
        if (change == "source-expiry") fixture.Clock.UtcNow = record.ValidUntil;
        if (change == "acceptance-expiry") fixture.Clock.UtcNow = accept.ValidUntil;
        if (change == "original-expiry") fixture.Clock.UtcNow = fixture.PurposeDeadline;
        var observed = await ObserveOriginalMeasuredAsync(fixture, record, original: true);
        if (change is "acceptance-receipt" or "acceptance-digest" or "acceptance-expiry")
            await Assert.That(observed.Source).IsNotNull();
        else await Assert.That(observed.Source).IsNull();
        await Assert.That(observed.Acceptance).IsNull();
        await Assert.That(observed.Prerequisite).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_measured_deadline_during_flush_rolls_back_native_source_or_acceptance(bool accepting)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalMeasuredRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        if (accepting)
            await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsTrue();
        var accept = await OriginalMeasuredAcceptanceAsync(fixture, record);
        fixture.Database.QualificationSaved = () => fixture.Database.EvaluationTime = fixture.PurposeDeadline;
        var result = accepting
            ? await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, accept)
            : await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationMeasuredOperationsSource>().CountAsync()).IsEqualTo(accepting ? 1 : 0);
        await Assert.That(await reader.Set<ModerationMeasuredOperationsAcceptance>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row =>
            row.Id == (accepting ? accept.OperationId : record.OperationId))).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision)
            .IsEqualTo(accepting ? accept.ExpectedQualificationRevision : record.ExpectedQualificationRevision);
    }

    private static async Task<RecordModerationMeasuredOperationsSource> OriginalMeasuredRequestAsync(Fixture fixture)
    {
        var constitution = await OriginalConstitutionRequestAsync(fixture);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, constitution)).IsSuccess).IsTrue();
        var adoption = await OriginalConstitutionAdoptionAsync(fixture, constitution);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var noticeSource = await reader.ModerationNoticeSources.SingleAsync(row => row.EffectId == fixture.EffectId);
        var notice = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(noticeSource.SnapshotJson)!.Notice!;
        return new()
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Database.Scope,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture), SourceId = Guid.CreateVersion7(),
            ProfileId = notice.ProfileId, OriginalEffect = constitution.OriginalEffect, ValidUntil = Now.AddSeconds(25),
            ReviewerHumanId = fixture.Database.UrgencyAssessorId, ReviewerControllerHumanId = fixture.Database.UrgencyAssessorId,
            RecorderControllerHumanId = fixture.Database.AdministratorId,
            Binding = new(notice.PolicyId, notice.PolicyRevision, notice.PublicationId, fixture.Database.Scope,
                ModerationRuleReference.Create(notice.Rule.OwnerId, notice.Rule.Code, notice.Rule.Revision),
                ModerationCapability.ReadResource, ModerationNativeMode.Embedded)
        };
    }

    [Test]
    [Arguments("exact")]
    [Arguments("source-receipt")]
    [Arguments("acceptance-receipt")]
    public async Task Original_measured_terminal_withdrawal_preserves_original_receipts_after_custody_expiry(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalMeasuredRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        var database = fixture.Database;
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, record)).IsSuccess).IsTrue();
        var accept = await OriginalMeasuredAcceptanceAsync(fixture, record);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, accept)).IsSuccess).IsTrue();
        if (change != "exact")
        {
            await using var writer = fixture.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var id = change == "source-receipt" ? record.OperationId : accept.OperationId;
            await writer.ModerationQualificationMutationReceipts.Where(row => row.Id == id).ExecuteDeleteAsync();
        }
        database.EvaluationTime = fixture.PurposeDeadline.AddDays(1);
        var withdraw = new WithdrawModerationMeasuredOperationsAcceptance
        {
            OperationId = Guid.CreateVersion7(), Target = record.Target,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            AcceptanceId = accept.AcceptanceId, ExpectedAcceptanceRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, withdraw)).IsSuccess)
            .IsEqualTo(change == "exact");
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var retained = await reader.Set<ModerationMeasuredOperationsAcceptance>().SingleAsync(row => row.Id == accept.AcceptanceId);
        await Assert.That(retained.Revision).IsEqualTo(change == "exact" ? 2L : 1L);
        await Assert.That(retained.SourceRevision).IsEqualTo(1L);
        await Assert.That(retained.SourceWriterGeneration).IsEqualTo(record.ExpectedQualificationRevision + 1);
        await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.CountAsync()).IsEqualTo(0);
    }

    private static async Task<AcceptModerationMeasuredOperationsSource> OriginalMeasuredAcceptanceAsync(
        Fixture fixture, RecordModerationMeasuredOperationsSource record) => new()
    {
        OperationId = Guid.CreateVersion7(), Target = record.Target,
        ExpectedQualificationRevision = await OriginalEpochAsync(fixture), SourceId = record.SourceId,
        ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = record.ExpectedQualificationRevision + 1,
        AcceptanceId = Guid.CreateVersion7(), ValidUntil = Now.AddSeconds(20), OriginalEffect = record.OriginalEffect
    };

    private static async Task<(ModerationMeasuredOperationsSourceObservation? Source,
        ModerationMeasuredOperationsAcceptanceObservation? Acceptance, ModerationPrerequisiteObservation? Prerequisite)>
        ObserveOriginalMeasuredAsync(Fixture fixture, RecordModerationMeasuredOperationsSource request, bool original)
    {
        await using var context = fixture.Context();
        var identity = await fixture.AuthenticateAsync(context);
        var admin = new AdminContext(new HttpContextAccessor(), new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        var privacy = fixture.Database.Privacy.Authority(context, fixture.Clock);
        return await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var authority = await new ModerationMaintenanceAuthority(context, identity.Machine, identity.Bindings,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), fixture.Clock)
                .AcquireForEffectAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision, privacy, token);
            if (authority is null) return default;
            var retained = await ModerationOriginalRecipeAuthority.AcquireAsync(context, authority, transaction.TransactionId,
                request.OriginalEffect!, privacy, fixture.Clock, token);
            if (retained is null) return default;
            var publications = new ModerationPolicyPublicationRepository(context, admin, identity.Machine,
                fixture.Database.Inventory, null, fixture.Clock);
            using var session = new ModerationConsumerEvidenceSession(context, transaction, authority, retained.Policy,
                retained.CurrentAsync, publications, fixture.Database.Inventory, null, null, fixture.Clock, null,
                original ? retained : null);
            var owner = new ModerationMeasuredOperationsSourceOwner(context,
                new ModerationPolicyDefinitionSourceOwner(context, admin, identity.Machine, fixture.Database.Inventory, fixture.Clock),
                fixture.Database.Inventory, fixture.Clock);
            var source = await owner.AcquireSourceAsync(session, transaction.TransactionId, request.ProfileId,
                request.Binding, fixture.Clock.UtcNow, token);
            var acceptance = await owner.ReadAcceptanceAsync(session, transaction.TransactionId, request.ProfileId,
                request.Binding, fixture.Clock.UtcNow, token);
            var prerequisite = await owner.AcquirePrerequisiteAsync(session, transaction.TransactionId,
                request.Binding, fixture.Clock.UtcNow, token);
            return (source, acceptance, prerequisite);
        }, CancellationToken.None);
    }
}
