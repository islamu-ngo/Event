using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task NativeAttributionSeparatesContentAndSubmissionWithoutChangingRootOrReporterAcceptance()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var contentRequest = review.Request(ModerationResponsibilityBasis.EventContent,
            ModerationSubjectKind.Content, database.EventId);
        var humanRequest = review.Request(ModerationResponsibilityBasis.EventSubmission,
            ModerationSubjectKind.Human, database.CallerId);
        await using var context = database.Context();
        var writer = AttributionWriter(context, database, review.Caller);
        var content = await writer.AttributeAsync(contentRequest, default);
        var human = await writer.AttributeAsync(humanRequest, default);
        await Assert.That(content).IsNotNull();
        await Assert.That(human).IsNotNull();
        var replay = await writer.AttributeAsync(contentRequest, default);
        await Assert.That(replay!.Id).IsEqualTo(content!.Id);
        await Assert.That(replay.CreatedAt).IsEqualTo(content.CreatedAt);
        await Assert.That(await writer.AttributeAsync(contentRequest with
        {
            Basis = ModerationResponsibilityBasis.EventSubmission,
            SubjectKind = ModerationSubjectKind.Human, SubjectId = database.CallerId
        }, default)).IsNull();
        await Assert.That(await writer.AttributeAsync(contentRequest with { OperationId = Guid.CreateVersion7() }, default)).IsNull();

        await using var verify = database.Context();
        var root = await verify.ModerationCases.Include(row => row.Evidence).SingleAsync(row => row.Id == review.RootId);
        await Assert.That(root.Subject).IsNull();
        await Assert.That(root.Revision).IsEqualTo(1L);
        await Assert.That(root.Evidence.Count).IsEqualTo(1);
        await Assert.That(human!.OccurrenceId).IsEqualTo(root.OccurrenceId);
        await Assert.That(content.OccurrenceId).IsEqualTo(root.OccurrenceId);
        await Assert.That(human.Subject!.Id).IsEqualTo(database.CallerId);
        await Assert.That(content.Subject!.Id).IsEqualTo(database.EventId);
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(2);
        await Assert.That(await verify.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationSanctions.AnyAsync()).IsFalse();

        // Later reporter intake still resolves only the confidential target root.
        var later = await database.IssueAsync(material: "Separately reported additional material.");
        await using var intakeContext = database.Context();
        var accepted = await Committer(intakeContext, database).SubmitAsync(
            new(ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, Guid.CreateVersion7(), 0,
                later.Receipt.Id, later.Token) { Narrative = database.Request(material: "Separately reported additional material.").Material },
            default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(accepted.Acknowledgement!.CaseId).IsEqualTo(root.Id);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task NativeActorAndRosterDoNotQualifyHumanSubmissionResponsibility(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        var review = await PrepareAttributionReviewAsync(database);
        await using var context = database.Context();
        var result = await AttributionWriter(context, database, review.Caller).AttributeAsync(
            review.Request(ModerationResponsibilityBasis.EventSubmission, ModerationSubjectKind.Human, database.CallerId), default);
        await Assert.That(result).IsNull();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseAttributionSources.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ControlledOrganizationRosterCannotSubstituteForTheActualEventSubmitter()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var organizationId = Guid.CreateVersion7();
        var participationId = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            var organization = new Organization
            {
                Id = organizationId,
                Pii = new OrganizationPii { OrganizationId = organizationId, FullName = "Controlled roster organization" }
            };
            var manager = new User
            {
                Id = managerId,
                Pii = new UserPii
                {
                    UserId = managerId, Email = $"{managerId:N}@example.invalid",
                    FirstName = "Roster", LastName = "Manager"
                }
            };
            var participation = new OrganizationTenant
            {
                Id = participationId, TenantId = database.TenantId, Tenant = null!,
                OrganizationId = organizationId, Organization = organization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!,
                IsOrganizerEligible = true, IsVisible = true
            };
            setup.AddRange(manager, organization, participation);
            foreach (var userId in new[] { database.CallerId, managerId })
                setup.OrganizationMembers.Add(new OrganizationMember
                {
                    Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
                    OrganizationTenantId = participationId, OrganizationTenant = participation,
                    UserId = userId, User = null!, RoleId = (int)RoleEnum.OrgAdmin, Role = null!
                });
            var actor = await setup.Actors.SingleAsync(row => row.Id == database.ActorId);
            actor.ActorTypeId = (int)ActorTypeEnum.Organization;
            actor.ServicePrincipalId = null;
            actor.OrganizationId = organizationId;
            await setup.SaveChangesAsync();
            await Assert.That(await setup.OrganizationMembers.CountAsync(row =>
                row.OrganizationTenantId == participationId && row.RoleId == (int)RoleEnum.OrgAdmin)).IsEqualTo(2);
        }
        var review = await PrepareAttributionReviewAsync(database);
        await using var context = database.Context();
        var writer = AttributionWriter(context, database, review.Caller);
        await Assert.That(await writer.AttributeAsync(review.Request(
            ModerationResponsibilityBasis.EventSubmission, ModerationSubjectKind.Human, managerId), default)).IsNull();
        await Assert.That(await writer.AttributeAsync(review.Request(
            ModerationResponsibilityBasis.OrganizationalActor, ModerationSubjectKind.Organization, organizationId),
            default)).IsNull();
        var accepted = await writer.AttributeAsync(review.Request(
            ModerationResponsibilityBasis.EventSubmission, ModerationSubjectKind.Human, database.CallerId), default);
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.Subject!.Id).IsEqualTo(database.CallerId);

        await using var verify = database.Context();
        await Assert.That((await verify.ModerationCases.SingleAsync(row => row.Id == review.RootId)).Subject).IsNull();
        await Assert.That(await verify.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(2);
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationSanctions.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task NativeServiceAttributionUsesServiceIdentityNotOperatorOrIssuer()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Service, standalone: true);
        var review = await PrepareAttributionReviewAsync(database, standalone: true);
        await using var context = database.Context();
        var writer = AttributionWriter(context, database, review.Caller);
        await Assert.That(await writer.AttributeAsync(review.Request(ModerationResponsibilityBasis.ServiceIdentity,
            ModerationSubjectKind.Service, database.CallerId), default)).IsNull();
        var accepted = await writer.AttributeAsync(review.Request(ModerationResponsibilityBasis.ServiceIdentity,
            ModerationSubjectKind.Service, database.ServiceId), default);
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.Subject!.Id).IsEqualTo(database.ServiceId);
        await Assert.That(accepted.Subject.Kind).IsEqualTo(ModerationSubjectKind.Service);
        await Assert.That(accepted.Subject.Origin.Kind).IsEqualTo(ModerationScopeKind.Instance);
    }

    [Test]
    public async Task OrganizationalAttributionRequiresActualOrganizationActorNotEmployee()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Actor, standalone: true);
        var organizationId = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            setup.Organizations.Add(new Organization
            {
                Id = organizationId, Pii = new OrganizationPii { OrganizationId = organizationId, FullName = "Native organization" }
            });
            var actor = await setup.Actors.SingleAsync(row => row.Id == database.ActorId);
            actor.ActorTypeId = (int)ActorTypeEnum.Organization;
            actor.ServicePrincipalId = null;
            actor.OrganizationId = organizationId;
            await setup.SaveChangesAsync();
        }
        var review = await PrepareAttributionReviewAsync(database, standalone: true);
        await using var context = database.Context();
        var writer = AttributionWriter(context, database, review.Caller);
        await Assert.That(await writer.AttributeAsync(review.Request(ModerationResponsibilityBasis.OrganizationalActor,
            ModerationSubjectKind.Organization, database.CallerId), default)).IsNull();
        var accepted = await writer.AttributeAsync(review.Request(ModerationResponsibilityBasis.OrganizationalActor,
            ModerationSubjectKind.Organization, organizationId), default);
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.Subject!.Id).IsEqualTo(organizationId);
    }

    [Test]
    public async Task CurrentAttributionRequalifiesNativeFactsAfterQualificationWriterProgress()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        Guid caseId;
        await using (var context = database.Context())
        {
            var accepted = await AttributionWriter(context, database, review.Caller).AttributeAsync(request, default);
            await Assert.That(accepted).IsNotNull();
            caseId = accepted!.Id;
        }
        await using var current = database.Context();
        current.CurrentUserService = review.Caller;
        await using var transaction = await current.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = new ModerationCaseTargetProofOwner(current, review.Caller, database.Machine, database.Clock);
        var source = await owner.AcquireCurrentAttributionAsync(caseId, default);
        await Assert.That(source).IsNotNull();
        var epoch = await current.ModerationQualificationEpochs.SingleAsync();
        var qualifiedRevision = epoch.Revision;
        epoch.Advance(qualifiedRevision);
        await current.SaveChangesAsync();
        await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        await Assert.That(await owner.AcquireCurrentAttributionAsync(caseId, default)).IsNotNull();
        await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        var retained = await current.ModerationCaseAttributionSources.SingleAsync();
        await Assert.That(retained.QualificationRevision).IsEqualTo(qualifiedRevision);
        await Assert.That(retained.ValidUntil).IsEqualTo(request.ValidUntil);
        await Assert.That(retained.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task RetainedAttributionQualifiesFactsForAnotherCallerWithoutGrantingDisclosure()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        Guid caseId;
        await using (var qualified = database.Context())
        {
            var adjudication = await AttributionWriter(qualified, database, review.Caller).AttributeAsync(request, default);
            await Assert.That(adjudication).IsNotNull();
            caseId = adjudication!.Id;
        }
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(context);
        var source = await owner.AcquireCurrentAttributionAsync(caseId, default);
        await Assert.That(source).IsNotNull();
        await Assert.That(source!.QualifiedByHumanId).IsEqualTo(review.Caller.UserId!.Value);
        await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        await Assert.That(await context.ModerationCaseReadGrants.AnyAsync(
            row => row.CallerUserId == database.CallerId)).IsFalse();
        await Assert.That(await context.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("source")]
    [Arguments("read-grant")]
    [Arguments("qualifier")]
    [Arguments("regressed-epoch")]
    [Arguments("expiry")]
    public async Task CurrentAttributionRejectsWithdrawnNativeQualificationWithoutRewritingHistory(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        if (defect == "regressed-epoch")
        {
            await using var progress = database.Context();
            var epoch = await progress.ModerationQualificationEpochs.SingleAsync();
            epoch.Advance(epoch.Revision);
            await progress.SaveChangesAsync();
        }
        Guid caseId;
        await using (var context = database.Context())
        {
            var accepted = await AttributionWriter(context, database, review.Caller).AttributeAsync(request, default);
            await Assert.That(accepted).IsNotNull();
            caseId = accepted!.Id;
        }
        await using (var positive = database.Context())
        {
            positive.CurrentUserService = review.Caller;
            await using var transaction = await positive.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = new ModerationCaseTargetProofOwner(positive, review.Caller, database.Machine, database.Clock);
            await Assert.That(await owner.AcquireCurrentAttributionAsync(caseId, default)).IsNotNull();
            await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        }
        await using (var mutation = database.Context())
        {
            if (defect == "source")
                await mutation.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
            else if (defect == "read-grant")
                await mutation.ModerationCaseReadGrants.Where(row => row.Id == review.GrantId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.RevokedAt, database.Clock.Now).SetProperty(row => row.Revision, 2));
            else if (defect == "qualifier")
                await mutation.PlatformUserRoles.Where(row => row.Id == review.AdminGrantId).ExecuteDeleteAsync();
            else if (defect == "regressed-epoch")
                await mutation.ModerationQualificationEpochs.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.Revision, row => row.Revision - 1));
            else database.Clock.Now = request.ValidUntil;
        }
        await using (var denied = database.Context())
        {
            denied.CurrentUserService = review.Caller;
            await using var transaction = await denied.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = new ModerationCaseTargetProofOwner(denied, review.Caller, database.Machine, database.Clock);
            await Assert.That(await owner.AcquireCurrentAttributionAsync(caseId, default)).IsNull();
        }
        await using var retryContext = database.Context();
        await Assert.That(await AttributionWriter(retryContext, database, review.Caller).AttributeAsync(request, default)).IsNull();
        await using var verify = database.Context();
        var retained = await verify.ModerationCaseAttributionSources.SingleAsync();
        await Assert.That(retained.Id).IsEqualTo(request.OperationId);
        await Assert.That(retained.CaseId).IsEqualTo(caseId);
        await Assert.That(retained.ValidUntil).IsEqualTo(request.ValidUntil);
        await Assert.That(retained.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("target")]
    [Arguments("grant")]
    [Arguments("clock")]
    [Arguments("caller")]
    public async Task FinalAttributionCheckpointRollsBackBothSourceAndCase(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        var checkpoint = new AttributionSavedCheckpoint(async context =>
        {
            if (defect == "target")
                await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
            else if (defect == "grant")
                await context.ModerationCaseReadGrants.Where(row => row.Id == review.GrantId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.RevokedAt, database.Clock.Now).SetProperty(row => row.Revision, 2));
            else if (defect == "clock") database.Clock.Now = request.ValidUntil;
            else review.Caller.IsAuthenticated = false;
        });
        await using (var context = database.Context(checkpoint))
            await Assert.That(await AttributionWriter(context, database, review.Caller).AttributeAsync(request, default)).IsNull();
        await Assert.That(checkpoint.Observed).IsTrue();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseAttributionSources.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task WithdrawalInvalidatesOnlyItsSubjectAndExactRetryDoesNotRenewIt()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var contentRequest = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        var humanRequest = review.Request(ModerationResponsibilityBasis.EventSubmission, ModerationSubjectKind.Human, database.CallerId);
        await using var context = database.Context();
        var writer = AttributionWriter(context, database, review.Caller);
        var content = await writer.AttributeAsync(contentRequest, default);
        var human = await writer.AttributeAsync(humanRequest, default);
        await Assert.That(content).IsNotNull();
        await Assert.That(human).IsNotNull();
        var withdrawalId = Guid.CreateVersion7();
        var withdrawn = await writer.WithdrawAsync(contentRequest.OperationId, withdrawalId, 1, default);
        await Assert.That(withdrawn).IsNotNull();
        var replay = await writer.WithdrawAsync(contentRequest.OperationId, withdrawalId, 1, default);
        await Assert.That(replay!.WithdrawnAt).IsEqualTo(withdrawn!.WithdrawnAt);
        await Assert.That(await writer.WithdrawAsync(contentRequest.OperationId, Guid.CreateVersion7(), 1, default)).IsNull();
        await Assert.That(await writer.AttributeAsync(contentRequest, default)).IsNull();
        await using var qualified = database.Context();
        qualified.CurrentUserService = review.Caller;
        await using var transaction = await qualified.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = new ModerationCaseTargetProofOwner(qualified, review.Caller, database.Machine, database.Clock);
        await Assert.That(await owner.AcquireCurrentAttributionAsync(content!.Id, default)).IsNull();
        await Assert.That(await owner.AcquireCurrentAttributionAsync(human!.Id, default)).IsNotNull();
        await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        await Assert.That(await qualified.ModerationCases.CountAsync()).IsEqualTo(3);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("host")]
    [Arguments("evidence")]
    [Arguments("root-revision")]
    [Arguments("unrelated-caller")]
    public async Task AttributionRejectsSubstitutedAuthorityOrOccurrenceCoordinates(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        var request = review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId);
        await using var context = database.Context();
        var writer = AttributionWriter(context, database, review.Caller);
        if (defect == "tenant") context.TenantContext = new SelectedTenant(Guid.CreateVersion7());
        else if (defect == "host") context.ModerationTrustDomainId = Guid.CreateVersion7();
        else if (defect == "evidence") request = request with { EvidenceCaseRevision = 2 };
        else if (defect == "root-revision") request = request with { ExpectedCaseRevision = 2 };
        else review.Caller.UserId = database.CallerId;
        await Assert.That(await writer.AttributeAsync(request, default)).IsNull();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseAttributionSources.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ActualSubmitterCannotQualifyTheirOwnResponsibilityClaim()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var review = await PrepareAttributionReviewAsync(database);
        Guid selfGrantId;
        await using (var context = database.Context())
        {
            var source = await context.ModerationCaseTargetSources.SingleAsync();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = database.Owner(context);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), review.RootId, 1,
                source.Id, database.CallerId, review.Until), default);
            await Assert.That(grant).IsNotNull();
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
            selfGrantId = grant!.Id;
        }
        await using var denied = database.Context();
        var request = review.Request(ModerationResponsibilityBasis.EventSubmission, ModerationSubjectKind.Human, database.CallerId)
            with { ReadGrantId = selfGrantId };
        await Assert.That(await AttributionWriter(denied, database, database.Caller).AttributeAsync(request, default)).IsNull();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseAttributionSources.AnyAsync()).IsFalse();
    }

    private static ModerationCaseAttributionRepository AttributionWriter(
        ExploreDbContext context, NativeDatabase database, NativeCaller reviewer)
    {
        context.CurrentUserService = reviewer;
        context.ModerationTrustDomainId = null;
        var owner = new ModerationCaseTargetProofOwner(context, reviewer, database.Machine, database.Clock);
        return new(context, owner, reviewer, database.Machine);
    }

    private static async Task<AttributionReview> PrepareAttributionReviewAsync(NativeDatabase database, bool standalone = false)
    {
        var issued = await database.IssueAsync(standalone
            ? ModerationCaseRelationshipKind.InstanceAdministrator : ModerationCaseRelationshipKind.EventRole);
        Guid rootId;
        await using (var intake = database.Context())
        {
            var result = await Committer(intake, database).SubmitAsync(
                new(ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, Guid.CreateVersion7(), 0,
                    issued.Receipt.Id, issued.Token) { Narrative = database.Request().Material }, default);
            await Assert.That(result.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
            rootId = result.Acknowledgement!.CaseId;
        }
        var reviewer = new NativeCaller(Guid.CreateVersion7());
        var adminId = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            setup.Users.Add(new User
            {
                Id = reviewer.UserId!.Value, Pii = new UserPii
                {
                    UserId = reviewer.UserId.Value, Email = $"{reviewer.UserId:N}@example.invalid",
                    FirstName = "Qualified", LastName = "Reviewer"
                }
            });
            setup.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = adminId, UserId = reviewer.UserId.Value, RoleId = (int)RoleEnum.Admin,
                User = null!, Role = null!, GrantedAt = database.Clock.Now.UtcDateTime
            });
            await setup.SaveChangesAsync();
        }
        await using var context = database.Context();
        context.CurrentUserService = reviewer;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = new ModerationCaseTargetProofOwner(context, reviewer, database.Machine, database.Clock);
        var until = database.Clock.Now.AddMinutes(1);
        var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), rootId, 1,
            issued.Receipt.SourceId, reviewer.UserId!.Value, until), default);
        await Assert.That(grant).IsNotNull();
        await context.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
        await transaction.CommitAsync();
        return new(reviewer, adminId, rootId, grant!.Id, until);
    }

    private sealed record AttributionReview(NativeCaller Caller, Guid AdminGrantId, Guid RootId, Guid GrantId, DateTimeOffset Until)
    {
        public ModerationCaseAttributionRequest Request(ModerationResponsibilityBasis basis, ModerationSubjectKind kind, Guid subject) =>
            new(Guid.CreateVersion7(), RootId, 1, 1, GrantId, basis, kind, subject, Until);
    }

    private sealed class AttributionSavedCheckpoint(Func<ExploreDbContext, Task> mutate) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationCaseAttributionSource>().Any())
            {
                Observed = true;
                await mutate(context);
            }
            return result;
        }
    }
}
