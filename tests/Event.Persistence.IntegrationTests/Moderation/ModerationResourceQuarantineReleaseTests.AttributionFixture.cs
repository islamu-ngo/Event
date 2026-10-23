using System.Data;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Moderation;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    private sealed partial class Fixture
    {
        public Guid AttributionId { get; private set; }
        public Guid AttributionGrantId { get; private set; }
        public Guid OccurrenceCaseId { get; private set; }
        public Guid AttributionMaterialId { get; private set; }
        public DateTimeOffset AttributionDeadline { get; private set; }

        private async Task OpenAttributedResourceAsync(Guid eventId, bool attributeSiblingResource)
        {
            var targetId = ResourceId;
            if (attributeSiblingResource)
            {
                await using var setup = Context();
                targetId = Guid.CreateVersion7();
                var sibling = EventResource.CreateDraft(targetId, Database.TenantId, eventId, null,
                    new EventResourceMetadata
                    {
                        Title = "Separate native Resource",
                        Kind = EventResourceKindEnum.GeneralDocument,
                        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                    }, EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                    [EventResourceAudienceRule.Create(Database.TenantId, eventId, targetId, EventResourceAudienceKindEnum.Organizer)],
                    Database.AdministratorId, Clock.Now.UtcDateTime);
                sibling.SetExternalDestination("separate-material-envelope", 1, "https://example.invalid",
                    sibling.ConcurrencyStamp, Database.AdministratorId, Clock.Now.UtcDateTime);
                setup.EventResources.Add(sibling);
                await setup.SaveChangesAsync();
            }
            var narrativeId = Guid.CreateVersion7();
            await SourceAsync(Database.AdministratorId, new RecordModerationCaseNarrativePolicySource
            {
                OperationId = Guid.CreateVersion7(),
                Target = Database.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = narrativeId,
                TargetKind = ModerationCaseTargetKind.Resource,
                Terms = new(ModerationCaseNarrativePurpose.ConfidentialAllegation, TimeSpan.FromHours(1), 4096),
                Ownership = new(Database.UrgencyAssessorId, Database.UrgencyAssessorId, Database.AdministratorId,
                    "Separately controlled native privacy and governance accounts.",
                    "Finite confidential custody for this Resource allegation only."),
                ValidUntil = Clock.Now.AddHours(1)
            });
            long generation;
            await using (var reader = Context())
            {
                reader.ModerationTrustDomainId = Database.Scope.TrustDomainId;
                generation = await reader.Set<ModerationCaseNarrativePolicySource>().Where(row => row.Id == narrativeId)
                    .Select(row => row.WriterGeneration).SingleAsync();
            }
            await SourceAsync(Database.UrgencyAssessorId, new AdoptModerationCaseNarrativePolicySource
            {
                OperationId = Guid.CreateVersion7(),
                Target = Database.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = narrativeId,
                ExpectedSourceRevision = 1,
                ExpectedSourceWriterGeneration = generation,
                AdoptionId = Guid.CreateVersion7(),
                ValidUntil = Clock.Now.AddHours(1)
            });
            var caller = new AttributionCaller(Database.AdministratorId);
            var budgetId = Guid.CreateVersion7();
            await SourceAsync(Database.AdministratorId, new RecordModerationOperationalBudgetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = Database.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = budgetId,
                OperatorHumanId = Database.UrgencyAssessorId,
                Terms = new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 8, TimeSpan.FromHours(1)),
                ValidUntil = Clock.Now.AddHours(1)
            });
            await using (var reader = Context())
            {
                reader.ModerationTrustDomainId = Database.Scope.TrustDomainId;
                generation = await reader.Set<ModerationOperationalBudgetSource>().Where(row => row.Id == budgetId)
                    .Select(row => row.WriterGeneration).SingleAsync();
            }
            await SourceAsync(Database.UrgencyAssessorId, new AdoptModerationOperationalBudgetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = Database.Scope,
                ExpectedQualificationRevision = 1,
                SourceId = budgetId,
                ExpectedSourceRevision = 1,
                ExpectedSourceWriterGeneration = generation,
                AdoptionId = Guid.CreateVersion7(),
                ValidUntil = Clock.Now.AddHours(1)
            });
            await using (var context = Context())
            {
                context.CurrentUserService = caller;
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var owner = AttributionOwner(context, caller);
                await Assert.That(await owner.SetLifetimePolicyAsync(ModerationCaseTargetKind.Resource, 0,
                    TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(20), true, default)).IsNotNull();
                await context.SaveChangesAsync();
                await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
                await transaction.CommitAsync();
            }
            var material = Encoding.UTF8.GetBytes("Fresh observation of the exact quarantined Resource.");
            var reporter = new AttributionCaller(Database.SubjectId);
            ModerationCaseIssuedReceipt issued;
            await using (var context = Context())
            {
                context.CurrentUserService = reporter;
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var owner = AttributionOwner(context, reporter);
                issued = await owner.IssueSubmissionAsync(new(ModerationCaseTargetKind.Resource,
                    ModerationCaseTargetKind.Resource, eventId, targetId,
                    ModerationCaseRelationshipKind.EventSubmitter, eventId,
                    Encoding.UTF8.GetBytes("Fresh independent material-safety occurrence."), material), default)
                    ?? throw new InvalidOperationException("Native Resource submission issuance failed.");
                await context.SaveChangesAsync();
                await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
                await transaction.CommitAsync();
            }
            await using (var context = Context())
            {
                context.CurrentUserService = reporter;
                var identity = Identity(context, reporter.UserId!.Value);
                var owner = AttributionOwner(context, reporter);
                var intake = new ModerationCaseIntakeRepository(context,
                    new ModerationCaseTargetReceiptRepository(context, reporter, identity.Machine, Clock), owner, Clock);
                var committer = new ModerationCaseIntakeCommitter(context, intake, owner, reporter, identity.Machine, Clock,
                    new ModerationCaseNarrativeRepository(context, identity.Admin, reporter, identity.Machine,
                        intake, Clock, new EphemeralDataProtectionProvider()), identity.Admin);
                var accepted = await committer.SubmitAsync(new(ModerationCaseAccessPurpose.SubmitAllegation,
                    Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
                { Narrative = material }, default);
                await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
                OccurrenceCaseId = accepted.Acknowledgement!.CaseId;
            }
            AttributionMaterialId = issued.Receipt.SourceId;
            AttributionDeadline = Clock.Now.AddMinutes(15);
            AttributionGrantId = await GrantAttributionReadAsync(AttributionDeadline);
            await using (var context = Context())
            {
                context.CurrentUserService = caller;
                var owner = AttributionOwner(context, caller);
                var attribution = await new ModerationCaseAttributionRepository(context, owner, caller,
                    Identity(context, caller.UserId!.Value).Machine).AttributeAsync(
                    new(Guid.CreateVersion7(), OccurrenceCaseId, 1, 1, AttributionGrantId,
                        ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, eventId,
                        AttributionDeadline), default)
                    ?? throw new InvalidOperationException("Native Tenant-owned Resource attribution failed.");
                CaseId = attribution.Id;
                AttributionId = attribution.AttributionSourceId!.Value;
            }
        }

        public async Task<Guid> GrantAttributionReadAsync(DateTimeOffset until)
        {
            var caller = new AttributionCaller(Database.AdministratorId);
            await using var context = Context();
            context.CurrentUserService = caller;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = AttributionOwner(context, caller);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), OccurrenceCaseId, 1,
                AttributionMaterialId, caller.UserId!.Value, until), default)
                ?? throw new InvalidOperationException("Native attribution disclosure grant failed.");
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
            return grant.Id;
        }

        public async Task RevokeAttributionDisclosureAsync()
        {
            var caller = new AttributionCaller(Database.AdministratorId);
            await using var context = Context();
            context.CurrentUserService = caller;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = AttributionOwner(context, caller);
            await Assert.That(await owner.RevokeReadAsync(AttributionGrantId, Guid.CreateVersion7(), 1, default)).IsNotNull();
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }

        public async Task RenewExpiredAttributionAsync()
        {
            Clock.Now = AttributionDeadline;
            var nextDeadline = Clock.Now.AddMinutes(15);
            var grant = await GrantAttributionReadAsync(nextDeadline);
            var caller = new AttributionCaller(Database.AdministratorId);
            await using var context = Context();
            context.CurrentUserService = caller;
            context.ModerationTrustDomainId = Database.Scope.TrustDomainId;
            var head = await context.ModerationCaseAttributionHeads.AsNoTracking()
                .SingleAsync(row => row.CaseId == CaseId);
            var owner = await context.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == CaseId);
            context.ModerationTrustDomainId = null;
            var renewed = await new ModerationCaseAttributionRepository(context, AttributionOwner(context, caller),
                caller, Identity(context, caller.UserId!.Value).Machine).RenewAsync(
                new(CaseId, owner.Revision, head.SourceId, head.Generation, head.Revision,
                    new(Guid.CreateVersion7(), OccurrenceCaseId, 1, 1, grant,
                        ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content,
                        owner.Subject!.Id, nextDeadline)), default)
                ?? throw new InvalidOperationException("Native expected-head source renewal failed.");
            AttributionId = renewed.Id;
            AttributionGrantId = grant;
            AttributionDeadline = renewed.ValidUntil;
            Deadline = Clock.Now.AddMinutes(10);
        }

        private ModerationCaseTargetProofOwner AttributionOwner(ExploreDbContext context, AttributionCaller caller) =>
            new(context, caller, Identity(context, caller.UserId!.Value).Machine, Clock);
    }

    private sealed class AttributionCaller(Guid id) : ICurrentUserService
    {
        public Guid? UserId => id;
        public bool IsAuthenticated => true;
    }
}
