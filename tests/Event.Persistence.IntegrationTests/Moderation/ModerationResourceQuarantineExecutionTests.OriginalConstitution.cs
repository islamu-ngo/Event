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
    public async Task Original_constitution_uses_real_independent_receipts_without_replacing_current_policy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalConstitutionRequestAsync(fixture);
        var replacement = await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        var database = fixture.Database;
        await Assert.That((await database.ExecuteAsync(database.AdministratorId,
            record with { OriginalEffect = null, OperationId = Guid.CreateVersion7() })).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, record)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, record)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId,
            record with { Terms = record.Terms with { ProtectedComplaintCalendarMonths = 7 } })).IsSuccess).IsFalse();
        var adopt = await OriginalConstitutionAdoptionAsync(fixture, record);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, adopt)).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, adopt)).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId,
            adopt with { OriginalEffect = null })).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId,
            adopt with { OriginalEffect = adopt.OriginalEffect! with { EffectId = Guid.CreateVersion7() } })).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adopt)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adopt)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId,
            adopt with { ValidUntil = adopt.ValidUntil.AddTicks(1) })).IsSuccess).IsFalse();
        var observed = await ObserveOriginalConstitutionAsync(fixture, record.ProfileId, original: true);
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.Adoption.Id).IsEqualTo(adopt.AdoptionId);
        await Assert.That(observed.ValidUntil).IsEqualTo(adopt.ValidUntil);
        await Assert.That(await ObserveOriginalConstitutionAsync(fixture, record.ProfileId, original: false)).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var source = await reader.Set<ModerationConstitutionSource>().SingleAsync(row => row.Id == record.SourceId);
        await Assert.That(source.OriginalEffectId).IsEqualTo(fixture.EffectId);
        await Assert.That(source.OriginalSnapshotHash).IsEqualTo(record.OriginalEffect!.SnapshotHash);
        await Assert.That(source.OriginalEffectBindingHash).IsEqualTo(record.OriginalEffect.EffectBindingHash);
        await Assert.That(source.OriginalCustodyUntil).IsEqualTo(fixture.PurposeDeadline);
        await Assert.That(source.PublicationId).IsEqualTo(record.PublicationId);
        await Assert.That(source.DefinitionId).IsEqualTo(record.PublicationId);
        await Assert.That(source.ProfileId).IsEqualTo(record.ProfileId);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync(row =>
            row.Id == record.OperationId || row.Id == adopt.OperationId)).IsEqualTo(2);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).LatestPublicationId).IsEqualTo(replacement);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Original_constitution_does_not_authorize_ordinary_selected_publication()
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalConstitutionRequestAsync(fixture);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsTrue();
        var adopt = await OriginalConstitutionAdoptionAsync(fixture, record);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, adopt)).IsSuccess).IsTrue();
        await Assert.That(await ObserveOriginalConstitutionAsync(fixture, record.ProfileId, original: true)).IsNotNull();
        // The original publication is still selected: purpose isolation, not replacement,
        // must keep this source out of ordinary activation and lifecycle qualification.
        await Assert.That(await ObserveOriginalConstitutionAsync(fixture, record.ProfileId, original: false)).IsNull();
    }

    [Test]
    public async Task Original_constitution_cannot_borrow_independently_adopted_replacement_governance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await OriginalConstitutionRequestAsync(fixture);
        var replacement = await ReplaceSelectedPublicationAsync(fixture);
        var record = original with
        {
            OriginalEffect = null,
            PublicationId = replacement,
            ExpectedPolicyRevision = original.ExpectedPolicyRevision + 1,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture)
        };
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsTrue();
        var adopt = await OriginalConstitutionAdoptionAsync(fixture, record);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, adopt)).IsSuccess).IsTrue();
        await Assert.That(await ObserveOriginalConstitutionAsync(fixture, original.ProfileId, original: true)).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await reader.Set<ModerationConstitutionAdoption>().SingleAsync()).SourceId).IsEqualTo(record.SourceId);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).LatestPublicationId).IsEqualTo(replacement);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    [Arguments("snapshot")]
    [Arguments("effect")]
    [Arguments("publication")]
    [Arguments("profile")]
    [Arguments("mode")]
    [Arguments("extended")]
    [Arguments("expired")]
    [Arguments("erasure")]
    [Arguments("availability")]
    [Arguments("recorder")]
    [Arguments("substance")]
    [Arguments("flush")]
    public async Task Original_constitution_writer_rejects_forged_stale_or_expired_authority(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalConstitutionRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        var epoch = await OriginalEpochAsync(fixture);
        record = record with { ExpectedQualificationRevision = epoch };
        if (change == "snapshot") record = record with
        {
            OriginalEffect = record.OriginalEffect! with { SnapshotHash = new string('e', 64) }
        };
        if (change == "effect") record = record with
        {
            OriginalEffect = record.OriginalEffect! with { EffectId = Guid.CreateVersion7() }
        };
        if (change == "publication") record = record with { PublicationId = Guid.CreateVersion7() };
        if (change == "profile") record = record with { ProfileId = Guid.CreateVersion7() };
        if (change == "mode") record = record with { Mode = ModerationNativeMode.RemoteStrict };
        if (change == "extended") record = record with { ValidUntil = fixture.PurposeDeadline.AddTicks(1) };
        if (change == "substance") record = record with
        {
            Terms = record.Terms with { Evidence = default }
        };
        if (change == "expired") fixture.Database.EvaluationTime = fixture.PurposeDeadline;
        if (change == "erasure") await EraseOriginalParticipantAsync(fixture, "representative");
        if (change == "availability")
            await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.ReviewerId,
                new WithdrawModerationConstitutionAvailability
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = fixture.Database.Scope,
                    ExpectedQualificationRevision = epoch,
                    AvailabilityId = record.Reviewers[0].AvailabilityId,
                    ExpectedAvailabilityRevision = 1
                })).IsSuccess).IsTrue();
        if (change == "availability") epoch = await OriginalEpochAsync(fixture);
        if (change == "recorder")
        {
            await using var writer = fixture.Context();
            await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Database.AdministratorId).ExecuteDeleteAsync();
        }
        if (change == "flush")
            fixture.Database.QualificationSaved = () => fixture.Database.EvaluationTime = fixture.PurposeDeadline;
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId,
            record with { ExpectedQualificationRevision = epoch })).IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationConstitutionSource>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(epoch);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    [Arguments("source-expiry")]
    [Arguments("adoption-expiry")]
    [Arguments("source-receipt")]
    [Arguments("adoption-receipt")]
    [Arguments("availability-receipt")]
    [Arguments("reviewer")]
    [Arguments("adopter")]
    [Arguments("withdrawn")]
    [Arguments("custody")]
    [Arguments("erasure")]
    [Arguments("new-source")]
    public async Task Original_constitution_observation_never_borrows_invalid_receipts_or_old_rosters(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalConstitutionRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsTrue();
        var adopt = await OriginalConstitutionAdoptionAsync(fixture, record);
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, adopt)).IsSuccess).IsTrue();
        if (change == "source-expiry") fixture.Clock.UtcNow = record.ValidUntil;
        if (change == "adoption-expiry") fixture.Clock.UtcNow = adopt.ValidUntil;
        if (change == "erasure") await EraseOriginalParticipantAsync(fixture, "representative");
        if (change == "withdrawn")
            await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId,
                new WithdrawModerationConstitutionAdoption
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = fixture.Database.Scope,
                    ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
                    AdoptionId = adopt.AdoptionId,
                    ExpectedAdoptionRevision = 1
                })).IsSuccess).IsTrue();
        if (change == "new-source")
            await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record with
            {
                OperationId = Guid.CreateVersion7(),
                SourceId = Guid.CreateVersion7(),
                ExpectedQualificationRevision = await OriginalEpochAsync(fixture)
            })).IsSuccess).IsTrue();
        if (change is "source-receipt" or "adoption-receipt" or "availability-receipt" or "reviewer" or "adopter" or "custody")
        {
            await using var writer = fixture.Context();
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            if (change == "source-receipt")
                await writer.ModerationQualificationMutationReceipts.Where(row => row.Id == record.OperationId).ExecuteDeleteAsync();
            if (change == "adoption-receipt")
                await writer.ModerationQualificationMutationReceipts.Where(row => row.Id == adopt.OperationId).ExecuteDeleteAsync();
            if (change == "availability-receipt")
                await writer.ModerationQualificationMutationReceipts.Where(row =>
                    row.SourceId == record.Reviewers[0].AvailabilityId).ExecuteDeleteAsync();
            if (change == "reviewer")
                await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Database.ReviewerId).ExecuteDeleteAsync();
            if (change == "adopter")
                await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Database.UrgencyAssessorId).ExecuteDeleteAsync();
            if (change == "custody")
                await writer.ModerationNoticeSources.Where(row => row.EffectId == fixture.EffectId).ExecuteDeleteAsync();
        }
        await Assert.That(await ObserveOriginalConstitutionAsync(fixture, record.ProfileId, original: true)).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Original_constitution_adoption_deadline_at_flush_rolls_back_native_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var record = await OriginalConstitutionRequestAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        record = record with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, record)).IsSuccess).IsTrue();
        var adopt = await OriginalConstitutionAdoptionAsync(fixture, record);
        fixture.Database.QualificationSaved = () => fixture.Database.EvaluationTime = adopt.ValidUntil;
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, adopt)).IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationConstitutionAdoption>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == adopt.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(adopt.ExpectedQualificationRevision);
    }

    private static async Task<RecordModerationConstitutionSource> OriginalConstitutionRequestAsync(Fixture fixture)
    {
        var database = fixture.Database;
        var secondReviewer = Guid.CreateVersion7();
        await using (var writer = fixture.Context())
        {
            var role = await writer.Roles.SingleAsync(row => row.RoleScopeId == (int)RoleScopeEnum.Platform
                && row.MasterCode == "platform.admin");
            var user = new User
            {
                Id = secondReviewer,
                Pii = new UserPii
                {
                    UserId = secondReviewer,
                    Email = $"{secondReviewer:N}@example.invalid",
                    FirstName = "Independent",
                    LastName = "Reviewer"
                }
            };
            writer.Users.Add(user);
            writer.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = secondReviewer,
                User = user,
                RoleId = role.Id,
                Role = role
            });
            await writer.SaveChangesAsync();
        }
        var reviewers = new List<ModerationConstitutionReviewerTerms>();
        foreach (var human in new[] { database.ReviewerId, secondReviewer })
        {
            var epoch = await OriginalEpochAsync(fixture);
            var availability = new RecordModerationConstitutionAvailability
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = epoch,
                AvailabilityId = Guid.CreateVersion7(),
                MaximumCaseload = 10,
                ValidUntil = Now.AddMinutes(2)
            };
            await Assert.That((await database.ExecuteAsync(human, availability)).IsSuccess).IsTrue();
            reviewers.Add(new(availability.AvailabilityId, 1, epoch + 1, human, human,
                "Native canonical controller independently reviewed.",
                "Qualified to assess findings, response and protected remedies.",
                "Distinct controller with case-specific implicated-party exclusion."));
        }
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var source = await reader.ModerationNoticeSources.SingleAsync(row => row.EffectId == fixture.EffectId);
        var notice = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!.Notice!;
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            SourceId = Guid.CreateVersion7(),
            PolicyId = notice.PolicyId,
            ExpectedPolicyRevision = notice.PolicyRevision,
            PublicationId = notice.PublicationId,
            ProfileId = notice.ProfileId,
            Mode = ModerationNativeMode.Embedded,
            OriginalEffect = new(fixture.EffectId, source.SnapshotHash, source.EffectBindingHash),
            ValidUntil = Now.AddSeconds(40),
            Reviewers = [.. reviewers],
            Terms = new(ModerationScopeKind.Tenant, ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
                ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
                ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
                ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
                ModerationConstitutionOverload.AcknowledgeAndEscalate, 2, notice.ContestationCalendarMonths,
                TimeSpan.FromDays(30), TimeSpan.FromHours(1), "https://example.invalid/support",
                "https://example.invalid/redress", database.UrgencyAssessorId, database.AdministratorId)
        };
    }

    private static async Task<AdoptModerationConstitutionSource> OriginalConstitutionAdoptionAsync(
        Fixture fixture, RecordModerationConstitutionSource record) => new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = record.Target,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = record.ExpectedQualificationRevision + 1,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddSeconds(30),
            OriginalEffect = record.OriginalEffect
        };

    private static async Task<ModerationConstitutionAdoptionObservation?> ObserveOriginalConstitutionAsync(
        Fixture fixture, Guid profileId, bool original)
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
            if (authority is null) return null;
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == fixture.EffectId, token);
            if (source is null) return null;
            var retained = await ModerationOriginalRecipeAuthority.AcquireAsync(context, authority, transaction.TransactionId,
                new(fixture.EffectId, source.SnapshotHash, source.EffectBindingHash), privacy, fixture.Clock, token);
            if (retained is null) return null;
            var publications = new ModerationPolicyPublicationRepository(context, admin, identity.Machine,
                fixture.Database.Inventory, null, fixture.Clock);
            using var session = new ModerationConsumerEvidenceSession(context, transaction, authority, retained.Policy,
                retained.CurrentAsync, publications, fixture.Database.Inventory, null, null, fixture.Clock, null,
                original ? retained : null);
            var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, identity.Machine,
                fixture.Database.Inventory, fixture.Clock);
            return await new ModerationConstitutionSourceOwner(context, definitions, fixture.Clock)
                .ReadAdoptionAsync(session, transaction.TransactionId, profileId, ModerationNativeMode.Embedded,
                    fixture.Clock.UtcNow, token);
        }, CancellationToken.None);
    }
}
