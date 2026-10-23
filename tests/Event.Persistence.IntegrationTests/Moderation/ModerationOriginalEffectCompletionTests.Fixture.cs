using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    private static async Task<ModerationOriginalEffectCompletionResult> CompleteOriginalAsync(Fixture fixture,
        Guid? leaseId = null, Guid? tenantId = null, bool expireKeyAfterAuthentication = false,
        params IInterceptor[] interceptors)
    {
        await using var context = fixture.Context(interceptors);
        var identity = await fixture.AuthenticateAsync(context);
        if (expireKeyAfterAuthentication)
        {
            await using var writer = fixture.Context();
            await writer.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, fixture.Clock.UtcNow.UtcDateTime));
        }
        if (tenantId is { } foreign) context.TenantContext = new SelectedTenant(foreign);
        var result = await OriginalCompletionRepository(fixture, context, identity.Machine, identity.Bindings)
            .CompleteAsync(fixture.EffectId, leaseId ?? fixture.LeaseId, fixture.LeaseRevision);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        return result;
    }

    private static ModerationOriginalEffectCompletionRepository OriginalCompletionRepository(
        Fixture fixture, ExploreDbContext context, IMachinePrincipalAccessor machine,
        IModerationMachineSubjectBindingRepository bindings)
    {
        var admin = new AdminContext(new HttpContextAccessor(), new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        var inventory = fixture.Database.Inventory;
        var runtime = fixture.Database.ConsumerRuntimeSources;
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, inventory, fixture.Clock);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, inventory, null, fixture.Clock);
        var deployment = new ModerationDeploymentSourceOwner(context, definitions, inventory, runtime, fixture.Clock);
        var sources = new ModerationActivationSourceOwner(context,
            new(context, deployment, fixture.Clock),
            new(context, definitions, fixture.Clock), new(context, definitions, fixture.Clock),
            new(context, definitions, fixture.Clock), new(context, definitions, fixture.Clock),
            new(context, definitions, inventory, fixture.Clock), null, runtime, fixture.Clock);
        return new ModerationOriginalEffectCompletionRepository(context, machine, bindings,
            new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            fixture.Clock, fixture.Database.Privacy.Authority(context, fixture.Clock),
            inventory, publications, sources, runtime, new ModerationMeasurementReceiptVerifier());
    }

    private static async Task SeedOriginalNoticeFactsAsync(Fixture fixture, bool acknowledge)
    {
        await using var writer = fixture.Context();
        writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = await writer.ModerationNoticeSources.SingleAsync(row => row.EffectId == fixture.EffectId);
        var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        var occurrences = new List<ModerationNotice>
        {
            ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Subject,
                source.SubjectKind, source.SubjectId, null, ModerationNoticeDeliveryDisposition.NoHumanProjection)
        };
        foreach (var representative in capture.ContentRepresentatives)
            occurrences.Add(ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.ContentRepresentative,
                1, representative.HumanId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable));
        foreach (var reviewer in capture.Reviewers.DistinctBy(row => row.ReviewerHumanId))
            occurrences.Add(ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Reviewer,
                1, reviewer.ReviewerHumanId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable));
        writer.ModerationNotices.AddRange(occurrences);
        if (acknowledge)
        {
            var notice = occurrences.Single(row => row.Audience == ModerationNoticeAudience.ContentRepresentative);
            writer.ModerationNoticeNotificationReceipts.Add(ModerationNoticeNotificationReceipt.Acknowledge(
                Guid.CreateVersion7(), notice, source, fixture.Database.TenantId, notice.RecipientSubjectId,
                source.SnapshotHash, capture.Notice!.ContestationCalendarMonths, source.RetainUntil!.Value, Now));
        }
        await writer.SaveChangesAsync();
    }

    /// <summary>
    /// Supplies the independently committed review-owner boundary with real domain chain validation
    /// and persisted rows. It is not evidence of reviewer qualification or production conformance.
    /// The completion consumer must still join the exact cause, disposition and optional release.
    /// </summary>
    private static async Task<EventResourceModerationReviewCompletion> SeedCompletedReviewAsync(
        Fixture fixture, EventResourceModerationQuarantine cause, bool safe)
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7()).ToArray();
        var humans = new[] { fixture.Database.AdministratorId, fixture.Database.UrgencyAssessorId,
            fixture.Database.ReviewerId, fixture.Database.SubjectId };
        var steps = new[] { ResourceSafetyReviewStep.ProposePurpose, ResourceSafetyReviewStep.AdoptPurpose,
            ResourceSafetyReviewStep.AttestMaterialSafety, safe ? ResourceSafetyReviewStep.ApproveSafety
                : ResourceSafetyReviewStep.ApproveContinuedQuarantine };
        var chain = Enumerable.Range(0, 4).Select(index => new EventResourceModerationQuarantineReview
        {
            Id = ids[index], TenantId = cause.TenantId, HostBindingId = fixture.Database.HostId,
            TrustDomainId = fixture.Database.Scope.TrustDomainId, QuarantineId = cause.Id,
            EventId = cause.EventId, EventResourceId = cause.EventResourceId, MaterialVersionId = cause.MaterialVersionId,
            CaseId = Guid.CreateVersion7(), CaseRevision = 1, Step = steps[index], PurposeId = ids[0],
            PredecessorId = index == 0 ? null : ids[index - 1], HumanId = humans[index], ControllerHumanId = humans[index],
            ReviewerAssignmentId = Guid.CreateVersion7(), ReviewerRevision = 3, RequiredApprovals = 1,
            MaximumEvidenceLifetimeTicks = TimeSpan.FromMinutes(1).Ticks,
            Attestation = index == 2 ? safe ? ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience
                : ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience : null,
            ResourceStamp = Guid.NewGuid(), NativeDigest = new string('A', 64), RequestDigest = new string('B', 64),
            QualificationRevision = 1, RecordedAt = Now, ValidUntil = Now.AddMinutes(1)
        }).ToArray();
        var completion = EventResourceModerationReviewCompletion.Complete(cause, chain);
        await using var writer = fixture.Context();
        writer.Set<EventResourceModerationQuarantineReview>().AddRange(chain);
        writer.Set<EventResourceModerationReviewCompletion>().Add(completion);
        await writer.SaveChangesAsync();
        return completion;
    }
}
