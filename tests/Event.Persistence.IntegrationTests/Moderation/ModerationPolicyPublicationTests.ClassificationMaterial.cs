using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.OrganizationMembers.Handlers.Commands;
using Explore.Application.Moderation;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    public enum MaterialChange
    {
        None, OmittedSession, OmittedComponent, WrongCategory, WrongAncestry,
        AdminWithoutOwnership, SharedController, PublisherReviewer, ManagerReviewer,
        NoAcceptance, NewUnacceptedHead, WithdrawnHead, CorrectedAfterExpiry,
        TaxonomyReplaced, TaxonomyWithdrawn, OwnerAssignmentLost, ReviewerAvailabilityLost,
        FormLabelRaw, TranslationRaw, OptionAdded, SessionAdded, ResourceReparented,
        DestinationRaw, PurposeRaw, WrongTransaction, ForeignContext, DisposedSession,
        DirtyContext, ReceiptLostAfterSave, MaterialChangedAfterSave, FinalFlushReceiptLoss,
        RoleLostAfterSave, ImmutableRetry, RolledBackTransaction, TrackedOptionMutation, EpochChangedAfterSave,
        ActorDisplayNameRaw, ActorPictureUriRaw, TrackedActorDisplayName,
        OrganizationReviewerPromoted, OrganizationControllerPromoted, GroupReviewerPromoted, GroupControllerPromoted,
        ActorParentChangedBeforeMaterialTenant
    }

    [Test]
    [Arguments(MaterialChange.None)]
    [Arguments(MaterialChange.OmittedSession)]
    [Arguments(MaterialChange.OmittedComponent)]
    [Arguments(MaterialChange.WrongCategory)]
    [Arguments(MaterialChange.WrongAncestry)]
    [Arguments(MaterialChange.AdminWithoutOwnership)]
    [Arguments(MaterialChange.SharedController)]
    [Arguments(MaterialChange.PublisherReviewer)]
    [Arguments(MaterialChange.ManagerReviewer)]
    [Arguments(MaterialChange.NoAcceptance)]
    [Arguments(MaterialChange.NewUnacceptedHead)]
    [Arguments(MaterialChange.WithdrawnHead)]
    [Arguments(MaterialChange.CorrectedAfterExpiry)]
    [Arguments(MaterialChange.TaxonomyReplaced)]
    [Arguments(MaterialChange.TaxonomyWithdrawn)]
    [Arguments(MaterialChange.OwnerAssignmentLost)]
    [Arguments(MaterialChange.ReviewerAvailabilityLost)]
    [Arguments(MaterialChange.FormLabelRaw)]
    [Arguments(MaterialChange.TranslationRaw)]
    [Arguments(MaterialChange.OptionAdded)]
    [Arguments(MaterialChange.SessionAdded)]
    [Arguments(MaterialChange.ResourceReparented)]
    [Arguments(MaterialChange.DestinationRaw)]
    [Arguments(MaterialChange.PurposeRaw)]
    [Arguments(MaterialChange.WrongTransaction)]
    [Arguments(MaterialChange.ForeignContext)]
    [Arguments(MaterialChange.DisposedSession)]
    [Arguments(MaterialChange.DirtyContext)]
    [Arguments(MaterialChange.ReceiptLostAfterSave)]
    [Arguments(MaterialChange.MaterialChangedAfterSave)]
    [Arguments(MaterialChange.FinalFlushReceiptLoss)]
    [Arguments(MaterialChange.RoleLostAfterSave)]
    [Arguments(MaterialChange.ImmutableRetry)]
    [Arguments(MaterialChange.RolledBackTransaction)]
    [Arguments(MaterialChange.TrackedOptionMutation)]
    [Arguments(MaterialChange.EpochChangedAfterSave)]
    [Arguments(MaterialChange.ActorDisplayNameRaw)]
    [Arguments(MaterialChange.ActorPictureUriRaw)]
    [Arguments(MaterialChange.TrackedActorDisplayName)]
    [Arguments(MaterialChange.OrganizationReviewerPromoted)]
    [Arguments(MaterialChange.OrganizationControllerPromoted)]
    [Arguments(MaterialChange.GroupReviewerPromoted)]
    [Arguments(MaterialChange.GroupControllerPromoted)]
    [Arguments(MaterialChange.ActorParentChangedBeforeMaterialTenant)]
    public async Task MaterialReview_RequiresCompleteNativeInventoryAndIndependentCurrentAcceptance(MaterialChange change)
        => await RunClassificationMaterialAsync(change, ModerationReferenceProfileId.CFG28);

    private sealed class MaterialParentBeforeTenant : DbCommandInterceptor
    {
        public Guid EventId { get; set; }
        public Guid ReplacementId { get; set; }
        private ModerationEventGenerationWriterTests.ReplaceOwnerBeforeTenant? _inner;
        public bool Observed => _inner?.Observed == true;
        public IReadOnlyList<Guid> Actors => _inner?.ActorAnchors ?? [];
        public IReadOnlyList<Guid> ActorsBeforeTenant => _inner?.ActorAnchorsBeforeTenant ?? [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => EventId == Guid.Empty
                ? ValueTask.FromResult(result)
                : (_inner ??= new(EventId, ReplacementId, false))
                    .NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private sealed record ClassificationMaterialFixture(
        Database Database, ExploreDbContext Context, DeploymentClock Clock, ModerationPolicyCompiledDraft Policy,
        Guid Owner, Guid Reviewer, Guid Adopter, RecordModerationClassificationMaterialSource Source,
        AcceptModerationClassificationMaterialSource Review, RecordModerationEventGenerationSource Generation,
        Func<Guid, ModerationQualificationRepository> Writer);

    private async Task RunClassificationMaterialAsync(MaterialChange change, ModerationReferenceProfileId reference,
        Func<ClassificationMaterialFixture, Task>? classificationControl = null,
        params IInterceptor[] classificationControlObservers)
        => await RunClassificationMaterialFixtureAsync(change, reference, classificationControl, false,
            classificationControlObservers);

    private async Task RunClassificationMaterialFixtureAsync(MaterialChange change, ModerationReferenceProfileId reference,
        Func<ClassificationMaterialFixture, Task>? classificationControl, bool publicCalendar,
        params IInterceptor[] classificationControlObservers)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var finalFlush = new MaterialFinalFlush();
        var flush = new MaterialFlushObserver { FinalFlush = finalFlush };
        var parentChange = new MaterialParentBeforeTenant();
        await using var context = database.Context(new IInterceptor[] { flush, finalFlush, parentChange }
            .Concat(classificationControlObservers).ToArray());
        var privacy = database.Privacy.Authority(context, clock);
        if (change == MaterialChange.FinalFlushReceiptLoss
            || classificationControlObservers.Any(observer => observer is MaterialFinalFlush))
        {
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().Options;
            await using var schema = new EmbeddedPrivacyErasureAuthorityDbContext(options);
            await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
            privacy = new(new TaxonomyAuthorityFactory(options), clock,
                Options.Create(new PrivacyErasureOptions()), applicationContext: context);
        }
        var request = database.Request(reference);
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var adopter = Guid.CreateVersion7();
        var reviewerController = change is MaterialChange.OrganizationControllerPromoted or MaterialChange.GroupControllerPromoted
            ? Guid.CreateVersion7() : reviewer;
        foreach (var human in new[] { owner, reviewer, adopter, reviewerController }.Distinct())
        {
            context.Users.Add(new User
            {
                Id = human,
                Pii = new UserPii
                { UserId = human, Email = $"{human:N}@example.invalid", FirstName = "Material", LastName = "Reviewer" }
            });
            context.PlatformUserRoles.Add(new PlatformUserRole
            { Id = Guid.CreateVersion7(), UserId = human, User = null!, RoleId = (int)RoleEnum.Admin, Role = null! });
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                UserId = human,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active
            });
        }
        var actorId = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = actorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = owner,
            Pii = new ActorPii { ActorId = actorId, DisplayName = "Material publisher" }
        };
        Guid? promotedMembershipId = null;
        var groupPromotion = change is MaterialChange.GroupReviewerPromoted or MaterialChange.GroupControllerPromoted;
        if (groupPromotion)
        {
            var group = new Group { Id = Guid.CreateVersion7(), FullName = "Material publishing group" };
            var participation = new GroupTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                GroupId = group.Id,
                Group = group,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!
            };
            GroupMember Membership(Guid human, RoleEnum role) => new()
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                GroupTenantId = participation.Id,
                GroupTenant = participation,
                UserId = human,
                User = null!,
                RoleId = (int)role,
                Role = null!
            };
            var promoted = Membership(change == MaterialChange.GroupReviewerPromoted ? reviewer : reviewerController, RoleEnum.GroupMember);
            promotedMembershipId = promoted.Id;
            context.AddRange(group, participation, Membership(owner, RoleEnum.GroupAdmin), promoted);
            actor.ActorTypeId = (int)ActorTypeEnum.Group;
            actor.UserId = null;
            actor.GroupId = group.Id;
        }
        if (change is MaterialChange.OrganizationReviewerPromoted or MaterialChange.OrganizationControllerPromoted)
        {
            var organization = new Organization
            {
                Id = Guid.CreateVersion7(),
                Pii = new OrganizationPii { FullName = "Material publishing organization" }
            };
            var participation = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationId = organization.Id,
                Organization = organization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!
            };
            OrganizationMember Membership(Guid human, RoleEnum role) => new()
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationTenantId = participation.Id,
                OrganizationTenant = participation,
                UserId = human,
                User = null!,
                RoleId = (int)role,
                Role = null!
            };
            var promoted = Membership(change == MaterialChange.OrganizationReviewerPromoted ? reviewer : reviewerController,
                RoleEnum.OrgMember);
            promotedMembershipId = promoted.Id;
            context.AddRange(organization, participation, Membership(owner, RoleEnum.OrgAdmin), promoted);
            actor.ActorTypeId = (int)ActorTypeEnum.Organization;
            actor.UserId = null;
            actor.OrganizationId = organization.Id;
        }
        if (publicCalendar) context.TenantUsers.Local.Single(row => row.UserId == owner).ActorId = actorId;
        var owningEvent = new Explore.Domain.Event(publicCalendar ? EventStatusEnum.Published : EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            ActorId = actorId,
            Actor = actor,
            Title = "Exact native material",
            SubmittedByUserId = owner,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)(publicCalendar ? VisibilityTypeEnum.Public : VisibilityTypeEnum.Private),
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!
        };
        var session = new EventSession(publicCalendar ? EventSessionStatusEnum.Published : EventSessionStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            EventId = owningEvent.Id,
            Event = owningEvent,
            TenantId = database.TenantId,
            Tenant = null!,
            Title = "Reviewed session",
            StartTime = publicCalendar ? Now.AddDays(1) : null,
            EndTime = publicCalendar ? Now.AddDays(1).AddHours(1) : null
        };
        var resourceId = Guid.CreateVersion7();
        var resource = EventResource.CreateDraft(resourceId, database.TenantId, owningEvent.Id, session.Id,
            new EventResourceMetadata
            {
                Title = "Reviewed resource",
                Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            },
            EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(database.TenantId, owningEvent.Id, resourceId, EventResourceAudienceKindEnum.Organizer)],
            owner, Now.UtcDateTime);
        resource.SetExternalDestination("protected-material-test-envelope", 1, "https://example.invalid",
            resource.ConcurrencyStamp, owner, Now.UtcDateTime);
        var ownership = EventRoleAssignment.Create(database.TenantId, owningEvent.Id, owner, (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active, Now.AddHours(-1).UtcDateTime, Now.AddHours(1).UtcDateTime, database.AdminId);
        var form = RegistrationForm.Create(database.TenantId, owningEvent.Id, "event", "registration", "Reviewed form", Now.UtcDateTime);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now.UtcDateTime);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Reviewed prompt", Now.UtcDateTime);
        version.AddSection(section);
        form.AddVersion(version);
        var option = new EventCustomPropertyOption
        {
            Id = Guid.CreateVersion7(),
            Namespace = "event",
            Key = "safe",
            DisplayName = "Reviewed option",
            Value = "safe",
            IsActive = true
        };
        var definition = new EventCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            EventId = owningEvent.Id,
            TenantId = database.TenantId,
            Namespace = "event",
            Key = "topic",
            DisplayName = "Topic",
            IsActive = true,
            PropertyType = PropertyType.Option,
            ExposureLevel = ExposureLevel.Public
        };
        option.EventCustomPropertyDefinitionId = definition.Id;
        var workflow = RegistrationWorkflow.Create(database.TenantId, owningEvent.Id, "Reviewed registration purpose", Now.UtcDateTime);
        context.AddRange(actor, owningEvent, session, resource, ownership, form, definition, option, workflow);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        ModerationQualificationRepository Writer(Guid human)
        {
            var identity = DeploymentIdentity(context, human);
            return new(context, identity.Admin, identity.Machine, publications,
                new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
                clock, privacy, deploymentInventory: database.Inventory);
        }
        long generation = 2;
        async Task<ModerationQualificationMutationReceipt> Known(Guid human, ModerationQualificationMutation mutation)
        {
            var result = await Writer(human).ExecuteAsync(mutation);
            await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            generation = result.Receipt!.QualificationRevision;
            return result.Receipt;
        }
        var ownerAvailability = Guid.CreateVersion7();
        var reviewerAvailability = Guid.CreateVersion7();
        await Known(owner, new RecordModerationConstitutionAvailability
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            AvailabilityId = ownerAvailability,
            MaximumCaseload = 5,
            ValidUntil = Now.AddMinutes(9)
        });
        await Known(reviewer, new RecordModerationConstitutionAvailability
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            AvailabilityId = reviewerAvailability,
            MaximumCaseload = 5,
            ValidUntil = Now.AddMinutes(9)
        });
        var constitution = new RecordModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = Guid.CreateVersion7(),
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId,
            ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded,
            ValidUntil = Now.AddMinutes(8),
            Terms = new(ModerationScopeKind.Tenant, ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
                ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
                ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
                ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
                ModerationConstitutionOverload.AcknowledgeAndEscalate, 2, 6, TimeSpan.FromDays(30), TimeSpan.FromHours(1),
                "https://example.invalid/support", "https://example.invalid/redress", owner, adopter),
            Reviewers =
            [
                new(ownerAvailability, 1, 3, owner, owner, "Canonical Event owner.",
                    "Qualified content meaning and child-risk review.", "Independent accountable owner mandate."),
                new(reviewerAvailability, 1, 4, reviewer, reviewerController, "Distinct canonical controller.",
                    "Qualified independent classification and child safeguarding reviewer.", "No material publisher or manager interest.")
            ]
        };
        await Known(database.AdminId, constitution);
        await Known(adopter, new AdoptModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = constitution.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(7)
        });
        var age = new RecordModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = Guid.CreateVersion7(),
            PolicyId = constitution.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = constitution.PublicationId,
            ProfileId = constitution.ProfileId,
            Mode = constitution.Mode,
            ValidUntil = Now.AddMinutes(7),
            Terms = ChildAgeTerms(),
            Ownership = new(reviewer, reviewerController, owner, owner, database.AdminId,
                "Independent controllers.", "Privacy mandate.", "Child safeguarding mandate.", "Collection mandate.")
        };
        await Known(database.AdminId, age);
        await Known(reviewer, new AdoptModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = age.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(6)
        });
        var taxonomy = new RecordModerationClassificationTaxonomySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = Guid.CreateVersion7(),
            TaxonomyId = Guid.CreateVersion7(),
            ExpectedTaxonomyGeneration = 0,
            PolicyId = constitution.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = constitution.PublicationId,
            ProfileId = constitution.ProfileId,
            Mode = constitution.Mode,
            ValidUntil = Now.AddMinutes(5),
            Mandate = new(owner, owner, reviewer, reviewerController, "Adopted taxonomy coordinator.", "Independent category meaning review."),
            Categories = [new(Guid.CreateVersion7(), 1, publicCalendar ? "general" : "debate",
                publicCalendar ? EventContentClassificationVersion.CategoryKind.General : EventContentClassificationVersion.CategoryKind.Debate,
                publicCalendar ? 0 : 18, publicCalendar ? "Reviewed General category." : "Reviewed Debate category.",
                "Adopted operator audience floor.")]
        };
        await Known(owner, taxonomy);
        var taxonomyAdoption = new AdoptModerationClassificationTaxonomySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = taxonomy.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(4)
        };
        await Known(reviewer, taxonomyAdoption);
        var eventGeneration = new RecordModerationEventGenerationSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = Guid.CreateVersion7(),
            EventId = owningEvent.Id,
            ExpectedEventRevision = 0,
            ValidUntil = Now.AddMinutes(4)
        };
        await Known(database.AdminId, eventGeneration);
        if (change == MaterialChange.ActorParentChangedBeforeMaterialTenant)
        {
            var replacement = new Actor
            {
                Id = Guid.CreateVersion7(),
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = adopter,
                Pii = new ActorPii { DisplayName = "Replacement material publisher" }
            };
            context.Actors.Add(replacement);
            await context.SaveChangesAsync();
            parentChange.EventId = owningEvent.Id;
            parentChange.ReplacementId = replacement.Id;
        }
        var mandate = new ModerationClassificationMaterialMandate(owner, owner, ownership.Id, 1, reviewer, reviewerController,
            "Current Event owner proposes the complete native material.",
            "Independent qualified review of all content, forms, options, languages, purposes and destinations.");
        ModerationClassificationMaterialSourceOwner Reader(ExploreDbContext db) => new(db,
            new ModerationPolicyDefinitionSourceOwner(db, admin, machine, database.Inventory, clock), clock);
        RecordModerationClassificationMaterialSource record;
        await using (var acquisition = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var authority = await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireAsync(database.Scope, constitution.PublicationId, default);
            using var evidence = publications.BorrowConsumerEvidence(authority!, policy);
            var inventory = await Reader(context).AcquireInventoryAsync(evidence, acquisition.TransactionId,
                eventGeneration.SourceId, taxonomy.TaxonomyId, constitution.ProfileId, constitution.Mode, mandate, Now, default);
            if (change == MaterialChange.ActorParentChangedBeforeMaterialTenant)
            {
                await Assert.That(parentChange.Observed).IsTrue();
                await Assert.That(parentChange.ActorsBeforeTenant.Contains(actorId)).IsTrue();
                await Assert.That(parentChange.Actors.Contains(parentChange.ReplacementId)).IsFalse();
                await Assert.That(inventory).IsNull();
                await acquisition.RollbackAsync();
                await Assert.That(await context.Events.AsNoTracking().Where(row => row.Id == owningEvent.Id)
                    .Select(row => row.ActorId).SingleAsync()).IsEqualTo(actorId);
                await Assert.That(await context.Set<ModerationClassificationMaterialSource>().AnyAsync()).IsFalse();
                return;
            }
            await Assert.That(inventory).IsNotNull();
            record = new()
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                SourceId = Guid.CreateVersion7(),
                ExpectedMaterialGeneration = 0,
                EventGenerationId = eventGeneration.SourceId,
                PolicyId = constitution.PolicyId,
                ExpectedPolicyRevision = 1,
                PublicationId = constitution.PublicationId,
                ProfileId = constitution.ProfileId,
                Mode = constitution.Mode,
                TaxonomyId = taxonomy.TaxonomyId,
                ExpectedTaxonomySourceId = taxonomy.SourceId,
                ExpectedTaxonomyAdoptionId = taxonomyAdoption.AdoptionId,
                Mandate = mandate,
                Components = inventory!.Components,
                Materials = inventory.Materials.Select(material => new ModerationClassificationMaterialSelection(
                    Guid.CreateVersion7(), material, [taxonomy.Categories[0].VersionId])).ToImmutableArray(),
                ValidUntil = Now.AddMinutes(3)
            };
        }
        if (change is MaterialChange.OmittedSession or MaterialChange.OmittedComponent or MaterialChange.WrongCategory
            or MaterialChange.WrongAncestry or MaterialChange.AdminWithoutOwnership or MaterialChange.SharedController)
        {
            var invalid = change switch
            {
                MaterialChange.OmittedSession => record with
                {
                    Materials = record.Materials.Where(row =>
                    row.Material.SessionId is null || row.Material.ResourceId is not null).ToImmutableArray()
                },
                MaterialChange.OmittedComponent => record with { Components = record.Components.RemoveAt(0) },
                MaterialChange.WrongCategory => record with
                {
                    Materials = record.Materials.SetItem(0,
                    record.Materials[0] with { CategoryVersionIds = [Guid.CreateVersion7()] })
                },
                MaterialChange.WrongAncestry => record with
                {
                    Materials = record.Materials.SetItem(0,
                    record.Materials[0] with { Material = record.Materials[0].Material with { EventStamp = Guid.NewGuid() } })
                },
                MaterialChange.AdminWithoutOwnership => record with
                {
                    Mandate = mandate with
                    { OwnerHumanId = database.AdminId, OwnerControllerHumanId = database.AdminId }
                },
                _ => record with { Mandate = mandate with { ReviewerControllerHumanId = owner } }
            };
            await Assert.That((await Writer(change == MaterialChange.AdminWithoutOwnership ? database.AdminId : owner)
                .ExecuteAsync(invalid)).Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            return;
        }
        flush.Change = change;
        flush.OwnerAssignmentId = ownership.Id;
        flush.SectionId = section.Id;
        var recorded = await Writer(owner).ExecuteAsync(record);
        if (change is MaterialChange.ReceiptLostAfterSave or MaterialChange.MaterialChangedAfterSave or MaterialChange.EpochChangedAfterSave)
        {
            await Assert.That(recorded.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationClassificationMaterialSource>().AnyAsync()).IsFalse();
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            return;
        }
        await Assert.That(recorded.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        generation = recorded.Receipt!.QualificationRevision;
        var accept = new AcceptModerationClassificationMaterialSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            ReviewId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(2)
        };
        if (promotedMembershipId is { } memberId)
        {
            if (groupPromotion)
            {
                var identity = GroupMemberMutationBoundaryTests.CreateBoundary(context, privacy, owner);
                var promotion = new Explore.Application.Features.GroupMembers.Handlers.Commands.UpdateGroupMemberRoleCommandHandler(
                    new GroupMemberRepository(context), new UserContext(identity.Http),
                    context.TenantContext!, identity.Boundary);
                await Assert.That((await promotion.ExecuteAsync(new()
                {
                    UpdateGroupMemberRoleDto = new()
                    { Id = memberId, Role = RoleEnum.GroupAdmin }
                }, default)).IsSuccess).IsTrue();
            }
            else
            {
                var identity = OrganizationMemberMutationBoundaryTests.CreateBoundary(context, privacy, owner);
                var promotion = new UpdateOrganizationMemberRoleCommandHandler(new OrganizationMemberRepository(context),
                    new OrganizationRepository(context), new UserContext(identity.Http), identity.Boundary);
                await Assert.That((await promotion.ExecuteAsync(new()
                {
                    UpdateOrganizationMemberRoleDto = new()
                    { Id = memberId, Role = RoleEnum.OrgAdmin }
                }, default)).IsSuccess).IsTrue();
            }
            var updatedGeneration = await context.ModerationQualificationEpochs.AsNoTracking()
                .Select(row => row.Revision).SingleAsync();
            await Assert.That(updatedGeneration).IsEqualTo(generation + 1);
            var denied = await Writer(reviewer).ExecuteAsync(accept with { ExpectedQualificationRevision = updatedGeneration });
            await Assert.That(denied.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationClassificationMaterialReview>()
                .AnyAsync(row => row.Id == accept.ReviewId)).IsFalse();
            return;
        }
        if (change == MaterialChange.PublisherReviewer)
        {
            await Assert.That((await Writer(owner).ExecuteAsync(accept)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
            return;
        }
        if (change == MaterialChange.ManagerReviewer)
        {
            context.EventRoleAssignments.Add(EventRoleAssignment.Create(database.TenantId, owningEvent.Id, reviewer,
                (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Now.UtcDateTime, null, database.AdminId));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await Assert.That((await Writer(reviewer).ExecuteAsync(accept)).Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            return;
        }
        if (change != MaterialChange.NoAcceptance)
        {
            var accepted = await Writer(reviewer).ExecuteAsync(accept);
            if (change is MaterialChange.FinalFlushReceiptLoss or MaterialChange.RoleLostAfterSave)
            {
                await Assert.That(accepted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                context.ModerationTrustDomainId = database.Scope.TrustDomainId;
                await Assert.That(await context.Set<ModerationClassificationMaterialReview>().AnyAsync()).IsFalse();
                if (change == MaterialChange.FinalFlushReceiptLoss) await Assert.That(finalFlush.Observed).IsTrue();
                return;
            }
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            generation = accepted.Receipt!.QualificationRevision;
        }
        if (classificationControl is not null)
        {
            await classificationControl(new(database, context, clock, policy, owner, reviewer, adopter,
                record, accept, eventGeneration, Writer));
            return;
        }
        if (change == MaterialChange.ImmutableRetry)
        {
            var replay = await Writer(owner).ExecuteAsync(record);
            await Assert.That(replay.Receipt!.CompletedAt).IsEqualTo(recorded.Receipt.CompletedAt);
            await Assert.That(replay.Receipt.QualificationRevision).IsEqualTo(recorded.Receipt.QualificationRevision);
            await Assert.That((await Writer(owner).ExecuteAsync(record with { ValidUntil = record.ValidUntil.AddTicks(-1) })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        }
        if (change == MaterialChange.NewUnacceptedHead)
            await Known(owner, record with
            {
                OperationId = Guid.CreateVersion7(),
                SourceId = Guid.CreateVersion7(),
                ExpectedMaterialGeneration = recorded.Receipt.QualificationRevision,
                ExpectedQualificationRevision = generation,
                Materials = record.Materials.Select(row => row with { ReviewId = Guid.CreateVersion7() }).ToImmutableArray()
            });
        if (change == MaterialChange.TaxonomyReplaced)
            await Known(owner, taxonomy with
            {
                OperationId = Guid.CreateVersion7(),
                SourceId = Guid.CreateVersion7(),
                ExpectedQualificationRevision = generation,
                ExpectedTaxonomyGeneration = taxonomy.ExpectedQualificationRevision + 1,
                Categories = [taxonomy.Categories[0] with { VersionId = Guid.CreateVersion7() }]
            });
        if (change == MaterialChange.TaxonomyWithdrawn)
            await Known(adopter, new WithdrawModerationClassificationTaxonomyAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                AdoptionId = taxonomyAdoption.AdoptionId,
                ExpectedAdoptionRevision = 1
            });
        if (change == MaterialChange.ReviewerAvailabilityLost)
            await Known(reviewer, new WithdrawModerationConstitutionAvailability
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                AvailabilityId = reviewerAvailability,
                ExpectedAvailabilityRevision = 1
            });
        if (change == MaterialChange.OwnerAssignmentLost)
            await context.EventRoleAssignments.Where(row => row.Id == ownership.Id).ExecuteDeleteAsync();
        if (change == MaterialChange.FormLabelRaw)
            await context.Set<RegistrationFormSection>().Where(row => row.Id == section.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Title, "Changed prompt without root stamp"));
        if (change == MaterialChange.TranslationRaw)
            await context.Set<RegistrationFormVersion>().Where(row => row.Id == version.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LanguageTag, "fr"));
        if (change == MaterialChange.DestinationRaw)
            await context.Set<EventResource>().Where(row => row.Id == resource.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalDestinationCiphertext, "changed-protected-destination"));
        if (change == MaterialChange.PurposeRaw)
            await context.Set<RegistrationWorkflow>().Where(row => row.Id == workflow.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Purpose, "Changed collection purpose"));
        if (change == MaterialChange.ActorDisplayNameRaw)
            await context.ActorPii.Where(row => row.ActorId == actorId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisplayName, "Unreviewed public actor text"));
        if (change == MaterialChange.ActorPictureUriRaw)
            await context.ActorPii.Where(row => row.ActorId == actorId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalProfilePictureUri,
                    "https://example.invalid/unreviewed-public-image"));
        if (change == MaterialChange.ResourceReparented)
        {
            await context.Set<EventResourceAudienceRule>().Where(row => row.EventResourceId == resource.Id).ExecuteDeleteAsync();
            await context.Set<EventResource>().Where(row => row.Id == resource.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EventSessionId, (Guid?)null)
                    .SetProperty(row => row.SessionScopeId, owningEvent.Id));
        }
        if (change == MaterialChange.OptionAdded)
        {
            context.Set<EventCustomPropertyOption>().Add(new()
            {
                Id = Guid.CreateVersion7(),
                EventCustomPropertyDefinitionId = definition.Id,
                Namespace = "event",
                Key = "new",
                DisplayName = "Unreviewed option",
                Value = "new",
                IsActive = true
            });
            await context.SaveChangesAsync();
        }
        if (change == MaterialChange.SessionAdded)
        {
            context.EventSessions.Add(new()
            {
                Id = Guid.CreateVersion7(),
                EventId = owningEvent.Id,
                Event = null!,
                TenantId = database.TenantId,
                Tenant = null!,
                Title = "Unreviewed child"
            });
            await context.SaveChangesAsync();
        }
        if (change == MaterialChange.TrackedOptionMutation)
        {
            var priorStamp = await context.Events.Where(row => row.Id == owningEvent.Id).Select(row => row.ConcurrencyStamp).SingleAsync();
            var tracked = await context.Set<EventCustomPropertyOption>().SingleAsync(row => row.Id == option.Id);
            tracked.DisplayName = "Changed option through native save";
            await context.SaveChangesAsync();
            await Assert.That(await context.Events.AsNoTracking().Where(row => row.Id == owningEvent.Id)
                .Select(row => row.ConcurrencyStamp).SingleAsync()).IsNotEqualTo(priorStamp);
        }
        if (change == MaterialChange.TrackedActorDisplayName)
        {
            var priorStamp = await context.Events.Where(row => row.Id == owningEvent.Id)
                .Select(row => row.ConcurrencyStamp).SingleAsync();
            var tracked = await context.ActorPii.SingleAsync(row => row.ActorId == actorId);
            tracked.DisplayName = "Unreviewed public actor text through native save";
            await context.SaveChangesAsync();
            await Assert.That(await context.Events.AsNoTracking().Where(row => row.Id == owningEvent.Id)
                .Select(row => row.ConcurrencyStamp).SingleAsync()).IsNotEqualTo(priorStamp);
        }
        if (change is MaterialChange.WithdrawnHead or MaterialChange.CorrectedAfterExpiry)
        {
            if (change == MaterialChange.CorrectedAfterExpiry)
            {
                clock.UtcNow = Now.AddDays(1);
                await context.EventRoleAssignments.Where(row => row.Id == ownership.Id).ExecuteDeleteAsync();
                await context.PlatformUserRoles.Where(row => row.UserId == reviewer || row.UserId == owner).ExecuteDeleteAsync();
            }
            var end = new EndModerationClassificationMaterialReview
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                ReviewId = accept.ReviewId,
                ExpectedReviewRevision = 1,
                End = change == MaterialChange.WithdrawnHead ? ModerationClassificationMaterialEnd.Withdrawn : ModerationClassificationMaterialEnd.Corrected,
                Grounds = "Accountable correction preserving the original finite acceptance."
            };
            await Known(adopter, end);
            await Assert.That((await Writer(adopter).ExecuteAsync(end)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var currentAuthority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, constitution.PublicationId, default);
        using var currentSession = publications.BorrowConsumerEvidence(currentAuthority!, policy);
        if (change == MaterialChange.DisposedSession) currentSession.Dispose();
        if (change == MaterialChange.RolledBackTransaction) await transaction.RollbackAsync();
        if (change == MaterialChange.DirtyContext)
            (await context.Set<RegistrationFormSection>().SingleAsync(row => row.Id == section.Id)).IsDeleted = true;
        await using var foreign = database.Context();
        var observed = await Reader(change == MaterialChange.ForeignContext ? foreign : context).AcquireCurrentAsync(currentSession,
            change == MaterialChange.WrongTransaction ? Guid.CreateVersion7() : transaction.TransactionId,
            owningEvent.Id, constitution.ProfileId, constitution.Mode, clock.UtcNow, default);
        if (change is MaterialChange.None or MaterialChange.ImmutableRetry)
        {
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.Source.Id).IsEqualTo(record.SourceId);
            await Assert.That(observed.Reviews.Length).IsEqualTo(3);
            var compiled = ModerationAudiencePolicyCompiler.Compile(observed.Generation,
                observed.Reviews.ToDictionary(row => row.Material,
                    row => (IReadOnlyCollection<EventContentClassificationVersion>)row.Categories),
                observed.Taxonomy.Categories, observed.Reviews, observed.AgeSource, observed.AgeAdoption, clock.UtcNow);
            await Assert.That(compiled.Status).IsEqualTo(ModerationAudiencePolicyCompiler.CompilationStatus.CurrentMetadata);
            await Assert.That(compiled.WholeEventMinimumAge).IsEqualTo(18);
            await Assert.That(await context.ModerationSanctions.CountAsync()).IsEqualTo(0);
        }
        else await Assert.That(observed).IsNull();
    }

    private sealed class MaterialFlushObserver : SaveChangesInterceptor
    {
        public MaterialChange Change { get; set; }
        public Guid OwnerAssignmentId { get; set; }
        public Guid SectionId { get; set; }
        public MaterialFinalFlush FinalFlush { get; init; } = null!;

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var primary = (ExploreDbContext)eventData.Context!;
            var source = primary.ChangeTracker.Entries<ModerationClassificationMaterialSource>().Select(row => row.Entity).SingleOrDefault();
            var review = primary.ChangeTracker.Entries<ModerationClassificationMaterialReview>().Select(row => row.Entity).SingleOrDefault();
            if (source is not null && Change == MaterialChange.ReceiptLostAfterSave)
                await primary.ModerationQualificationMutationReceipts.Where(row => row.SourceId == source.Id).ExecuteDeleteAsync(cancellationToken);
            if (source is not null && Change == MaterialChange.MaterialChangedAfterSave)
                await primary.Set<RegistrationFormSection>().Where(row => row.Id == SectionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Title, "Late changed material"), cancellationToken);
            if (source is not null && Change == MaterialChange.EpochChangedAfterSave)
                await primary.ModerationQualificationEpochs.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            if (review is not null && Change == MaterialChange.RoleLostAfterSave)
                await primary.EventRoleAssignments.Where(row => row.Id == OwnerAssignmentId).ExecuteDeleteAsync(cancellationToken);
            if (review is not null && Change == MaterialChange.FinalFlushReceiptLoss)
            {
                if (!primary.IdentityFenceOwnsTransaction) throw new InvalidOperationException("Requires the original identity transaction.");
                primary.DisclosureMutations.Enlist([review.TenantId]);
                FinalFlush.ReviewId = review.Id;
                FinalFlush.Transaction = primary.Database.CurrentTransaction!.GetDbTransaction();
            }
            return result;
        }
    }

    private sealed class MaterialFinalFlush : DbCommandInterceptor
    {
        public Guid? ReviewId { get; set; }
        public DbTransaction? Transaction { get; set; }
        public bool Observed { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Observed || ReviewId is not { } id || eventData.Context is not ExploreDbContext primary) return result;
            var entity = primary.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = primary.GetService<ISqlGenerationHelper>();
            var column = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoveryRevision.DisclosureEpoch))!.GetColumnName(table)!);
            if (command.Parameters.Count != 3 || !command.CommandText.StartsWith(
                $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {column} = ", StringComparison.Ordinal)) return result;
            if (result != 1 || !ReferenceEquals(command.Transaction, Transaction) || !primary.IdentityFenceOwnsTransaction)
                throw new InvalidOperationException("Material final flush lost the original native transaction.");
            Observed = true;
            await primary.ModerationQualificationMutationReceipts.Where(row => row.SourceId == id).ExecuteDeleteAsync(cancellationToken);
            return result;
        }
    }
}
