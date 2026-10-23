using System.Collections.Immutable;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    private sealed class MaterialReadSourceOrder : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public HashSet<Guid> ActorsBeforeTenant { get; } = [];
        public HashSet<int> RolesBeforeTenant { get; } = [];
        public int LateSources { get; private set; }
        private bool tenantSeen;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not { } context) return ValueTask.FromResult(result);
            var sql = context.GetService<ISqlGenerationHelper>();
            bool Updates<T>() where T : class
            {
                var type = context.Model.FindEntityType(typeof(T))!;
                return command.CommandText.StartsWith($"UPDATE {sql.DelimitIdentifier(type.GetTableName()!, type.GetSchema())}",
                    StringComparison.OrdinalIgnoreCase);
            }
            if (Updates<Tenant>()) tenantSeen = true;
            if (Updates<Actor>())
                foreach (var parameter in command.Parameters.Cast<DbParameter>())
                    if (Guid.TryParse(parameter.Value?.ToString(), out var id))
                    {
                        if (tenantSeen && !ActorsBeforeTenant.Contains(id)) LateSources++;
                        if (!tenantSeen) ActorsBeforeTenant.Add(id);
                    }
            if (Updates<Role>())
                foreach (var parameter in command.Parameters.Cast<DbParameter>())
                    if (parameter.Value is int id)
                    {
                        if (tenantSeen && !RolesBeforeTenant.Contains(id)) LateSources++;
                        if (!tenantSeen) RolesBeforeTenant.Add(id);
                    }
            return ValueTask.FromResult(result);
        }
    }

    public enum InventoryReadChange
    {
        None, Machine, ForeignHuman, ForeignTenant, NonOwner, UnqualifiedReviewer,
        WrongProfile, WrongTaxonomy, WrongMode, Expired, TransactionSwapped, ConnectionSwapped, ProviderConnectionSwapped,
        OwnerAssignmentLost, ReviewerLost, TaxonomyChanged, AgeChanged, GovernanceChanged,
        PublicationChanged, EpochChanged, MaterialSubstituted, ComponentSubstituted,
        ActorChanged, LatestWriterChanged, HumanChanged, TenantFrameChanged, DirtyFrame, ProfileChanged,
        TaxonomyCategorySubstituted, RegressingClock, AlreadyAccepted, SourceCategoriesSubstituted,
        SourceMandateSubstituted, UnknownSource, NativeSourcesBeforeTenant
    }

    [Test]
    [Arguments(InventoryReadChange.None)]
    [Arguments(InventoryReadChange.NativeSourcesBeforeTenant)]
    [Arguments(InventoryReadChange.Machine)]
    [Arguments(InventoryReadChange.ForeignHuman)]
    [Arguments(InventoryReadChange.ForeignTenant)]
    [Arguments(InventoryReadChange.NonOwner)]
    [Arguments(InventoryReadChange.UnqualifiedReviewer)]
    [Arguments(InventoryReadChange.WrongProfile)]
    [Arguments(InventoryReadChange.WrongTaxonomy)]
    [Arguments(InventoryReadChange.WrongMode)]
    [Arguments(InventoryReadChange.Expired)]
    [Arguments(InventoryReadChange.TransactionSwapped)]
    [Arguments(InventoryReadChange.ConnectionSwapped)]
    [Arguments(InventoryReadChange.ProviderConnectionSwapped)]
    [Arguments(InventoryReadChange.OwnerAssignmentLost)]
    [Arguments(InventoryReadChange.ReviewerLost)]
    [Arguments(InventoryReadChange.TaxonomyChanged)]
    [Arguments(InventoryReadChange.AgeChanged)]
    [Arguments(InventoryReadChange.GovernanceChanged)]
    [Arguments(InventoryReadChange.PublicationChanged)]
    [Arguments(InventoryReadChange.EpochChanged)]
    [Arguments(InventoryReadChange.MaterialSubstituted)]
    [Arguments(InventoryReadChange.ComponentSubstituted)]
    [Arguments(InventoryReadChange.ActorChanged)]
    [Arguments(InventoryReadChange.LatestWriterChanged)]
    [Arguments(InventoryReadChange.HumanChanged)]
    [Arguments(InventoryReadChange.TenantFrameChanged)]
    [Arguments(InventoryReadChange.DirtyFrame)]
    [Arguments(InventoryReadChange.ProfileChanged)]
    [Arguments(InventoryReadChange.TaxonomyCategorySubstituted)]
    [Arguments(InventoryReadChange.RegressingClock)]
    public Task InventoryRead_RetainsCompleteOriginalNativeCoordinatesUntilDisposal(InventoryReadChange change) =>
        InventoryReadWitnessAsync(change, false);

    [Test]
    [Arguments(InventoryReadChange.None)]
    [Arguments(InventoryReadChange.Machine)]
    [Arguments(InventoryReadChange.ForeignHuman)]
    [Arguments(InventoryReadChange.ForeignTenant)]
    [Arguments(InventoryReadChange.NonOwner)]
    [Arguments(InventoryReadChange.UnqualifiedReviewer)]
    [Arguments(InventoryReadChange.Expired)]
    [Arguments(InventoryReadChange.TransactionSwapped)]
    [Arguments(InventoryReadChange.ConnectionSwapped)]
    [Arguments(InventoryReadChange.ProviderConnectionSwapped)]
    [Arguments(InventoryReadChange.OwnerAssignmentLost)]
    [Arguments(InventoryReadChange.ReviewerLost)]
    [Arguments(InventoryReadChange.TaxonomyChanged)]
    [Arguments(InventoryReadChange.AgeChanged)]
    [Arguments(InventoryReadChange.GovernanceChanged)]
    [Arguments(InventoryReadChange.PublicationChanged)]
    [Arguments(InventoryReadChange.EpochChanged)]
    [Arguments(InventoryReadChange.MaterialSubstituted)]
    [Arguments(InventoryReadChange.ComponentSubstituted)]
    [Arguments(InventoryReadChange.ActorChanged)]
    [Arguments(InventoryReadChange.LatestWriterChanged)]
    [Arguments(InventoryReadChange.HumanChanged)]
    [Arguments(InventoryReadChange.TenantFrameChanged)]
    [Arguments(InventoryReadChange.DirtyFrame)]
    [Arguments(InventoryReadChange.ProfileChanged)]
    [Arguments(InventoryReadChange.TaxonomyCategorySubstituted)]
    [Arguments(InventoryReadChange.RegressingClock)]
    [Arguments(InventoryReadChange.AlreadyAccepted)]
    [Arguments(InventoryReadChange.SourceCategoriesSubstituted)]
    [Arguments(InventoryReadChange.SourceMandateSubstituted)]
    [Arguments(InventoryReadChange.UnknownSource)]
    [Arguments(InventoryReadChange.NativeSourcesBeforeTenant)]
    public Task ReviewRead_RequiresNominatedIndependentHumanAndExactOriginalProposal(InventoryReadChange change) =>
        InventoryReadWitnessAsync(change, true);

    private async Task InventoryReadWitnessAsync(InventoryReadChange change, bool reviewRead)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var sourceOrder = new MaterialReadSourceOrder();
        await using var context = database.Context(sourceOrder);
        var privacy = database.Privacy.Authority(context, clock);
        var request = database.Request(ModerationReferenceProfileId.CFG28);
        var identity = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, identity.Admin, identity.Machine,
            database.Inventory, null, clock);
        var published = await publications.PublishAsync(request);
        await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var adopter = Guid.CreateVersion7();
        foreach (var human in new[] { owner, reviewer, adopter })
        {
            context.Users.Add(new User
            {
                Id = human,
                Pii = new UserPii
                { UserId = human, Email = $"{human:N}@example.invalid", FirstName = "Inventory", LastName = "Witness" }
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
            Pii = new ActorPii { ActorId = actorId, DisplayName = "Inventory publisher" }
        };
        var owningEvent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            ActorId = actorId,
            Actor = actor,
            Title = "Native inventory",
            SubmittedByUserId = owner,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Private,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!
        };
        var child = new EventSession
        {
            Id = Guid.CreateVersion7(),
            EventId = owningEvent.Id,
            Event = owningEvent,
            TenantId = database.TenantId,
            Tenant = null!,
            Title = "Original session"
        };
        var ownership = EventRoleAssignment.Create(database.TenantId, owningEvent.Id, owner, (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active, Now.AddHours(-1).UtcDateTime, Now.AddHours(1).UtcDateTime, database.AdminId);
        var form = RegistrationForm.Create(database.TenantId, owningEvent.Id, "event", "registration", "Inventory form", Now.UtcDateTime);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now.UtcDateTime);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Original prompt", Now.UtcDateTime);
        version.AddSection(section);
        form.AddVersion(version);
        context.AddRange(actor, owningEvent, child, ownership, form);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        long generation = published.Publication!.QualificationRevision;
        async Task Known(Guid human, ModerationQualificationMutation mutation)
        {
            var writerIdentity = DeploymentIdentity(context, human);
            var result = await new ModerationQualificationRepository(context, writerIdentity.Admin, writerIdentity.Machine,
                publications, new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
                clock, privacy, deploymentInventory: database.Inventory).ExecuteAsync(mutation);
            await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            generation = result.Receipt!.QualificationRevision;
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
                new(ownerAvailability, 1, 3, owner, owner, "Canonical owner.", "Qualified classification.", "Accountable owner mandate."),
                new(reviewerAvailability, 1, 4, reviewer, reviewer, "Independent controller.", "Qualified child safeguarding.",
                    "No publisher or manager interest.")
            ]
        };
        await Known(database.AdminId, constitution);
        var governanceAdoption = Guid.CreateVersion7();
        await Known(adopter, new AdoptModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = constitution.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = governanceAdoption,
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
            Ownership = new(reviewer, reviewer, owner, owner, database.AdminId,
                "Distinct controllers.", "Privacy mandate.", "Safeguarding mandate.", "Collection mandate.")
        };
        await Known(database.AdminId, age);
        var ageAdoption = Guid.CreateVersion7();
        await Known(reviewer, new AdoptModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = age.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = ageAdoption,
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
            Mandate = new(owner, owner, reviewer, reviewer, "Taxonomy coordinator.", "Independent category meaning review."),
            Categories = [new(Guid.CreateVersion7(), 1, "debate", EventContentClassificationVersion.CategoryKind.Debate,
                18, "Debate category.", "Adopted audience floor.")]
        };
        await Known(owner, taxonomy);
        var taxonomyAdoption = Guid.CreateVersion7();
        await Known(reviewer, new AdoptModerationClassificationTaxonomySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = taxonomy.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = taxonomyAdoption,
            ValidUntil = Now.AddMinutes(4)
        });
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
        var mandate = new ModerationClassificationMaterialMandate(owner, owner, ownership.Id, 1, reviewer, reviewer,
            "Actual Event owner.", "Complete independently reviewed inventory.");
        Guid? proposalId = null;
        var expectedHead = 0L;
        if (reviewRead)
        {
            var proposingIdentity = DeploymentIdentity(context, owner);
            var proposingReader = new ModerationPolicyPublicationRepository(context, proposingIdentity.Admin, proposingIdentity.Machine,
                database.Inventory, null, clock);
            var inventory = await proposingReader.AcquireInventoryReadAsync(new(database.TenantId, owner,
                eventGeneration.SourceId, taxonomy.TaxonomyId, constitution.ProfileId, constitution.Mode, mandate));
            await Assert.That(inventory.Outcome).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
            var observed = inventory.Lease!.Observation;
            await inventory.Lease.DisposeAsync();
            proposalId = Guid.CreateVersion7();
            await Known(owner, new RecordModerationClassificationMaterialSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                SourceId = proposalId.Value,
                ExpectedMaterialGeneration = 0,
                EventGenerationId = eventGeneration.SourceId,
                PolicyId = constitution.PolicyId,
                ExpectedPolicyRevision = 1,
                PublicationId = constitution.PublicationId,
                ProfileId = constitution.ProfileId,
                Mode = constitution.Mode,
                TaxonomyId = taxonomy.TaxonomyId,
                ExpectedTaxonomySourceId = taxonomy.SourceId,
                ExpectedTaxonomyAdoptionId = taxonomyAdoption,
                Mandate = mandate,
                Materials = observed.Materials.Select(material => new ModerationClassificationMaterialSelection(
                    Guid.CreateVersion7(), material, [taxonomy.Categories[0].VersionId])).ToImmutableArray(),
                Components = observed.Components,
                ValidUntil = Now.AddMinutes(3)
            });
            expectedHead = generation;
            if (change == InventoryReadChange.AlreadyAccepted)
                await Known(reviewer, new AcceptModerationClassificationMaterialSource
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = database.Scope,
                    ExpectedQualificationRevision = generation,
                    SourceId = proposalId.Value,
                    ExpectedSourceRevision = 1,
                    ExpectedSourceWriterGeneration = expectedHead,
                    ReviewId = Guid.CreateVersion7(),
                    ValidUntil = Now.AddMinutes(2)
                });
        }
        var readerIdentity = DeploymentIdentity(context, change == InventoryReadChange.NonOwner ? database.AdminId : owner);
        var reader = new ModerationPolicyPublicationRepository(context, readerIdentity.Admin, readerIdentity.Machine,
            database.Inventory, null, clock);
        var readRequest = new ModerationClassificationMaterialInventoryReadRequest(database.TenantId, owner,
            eventGeneration.SourceId, taxonomy.TaxonomyId, constitution.ProfileId, constitution.Mode, mandate);
        if (change == InventoryReadChange.Machine)
            reader = database.Writer(context, machineCaller: true);
        if (change == InventoryReadChange.ForeignHuman) readRequest = readRequest with { HumanId = reviewer };
        if (change == InventoryReadChange.ForeignTenant) readRequest = readRequest with { RequestTenantId = Guid.CreateVersion7() };
        if (change == InventoryReadChange.NonOwner)
            readRequest = readRequest with
            {
                HumanId = database.AdminId,
                Mandate = mandate with
                { OwnerHumanId = database.AdminId, OwnerControllerHumanId = database.AdminId }
            };
        if (change == InventoryReadChange.UnqualifiedReviewer)
            readRequest = readRequest with { Mandate = mandate with { ReviewerHumanId = adopter, ReviewerControllerHumanId = adopter } };
        if (change == InventoryReadChange.WrongProfile) readRequest = readRequest with { ProfileId = Guid.CreateVersion7() };
        if (change == InventoryReadChange.WrongTaxonomy) readRequest = readRequest with { TaxonomyId = Guid.CreateVersion7() };
        if (change == InventoryReadChange.WrongMode) readRequest = readRequest with { Mode = (ModerationNativeMode)99 };
        var originalTenant = context.TenantContext;
        var originalTrust = context.ModerationTrustDomainId;
        var receipts = await context.ModerationQualificationMutationReceipts.CountAsync();
        sourceOrder.Armed = change == InventoryReadChange.NativeSourcesBeforeTenant;
        ModerationCurrentPolicyReadOutcome acquiredOutcome;
        IModerationClassificationMaterialInventoryReadLease? lease;
        if (reviewRead)
        {
            var readingHuman = change == InventoryReadChange.NonOwner ? owner
                : change == InventoryReadChange.UnqualifiedReviewer ? adopter : reviewer;
            var reviewingIdentity = DeploymentIdentity(context, readingHuman);
            var reviewingReader = change == InventoryReadChange.Machine ? database.Writer(context, machineCaller: true)
                : new ModerationPolicyPublicationRepository(context, reviewingIdentity.Admin, reviewingIdentity.Machine,
                    database.Inventory, null, clock);
            var reviewed = await reviewingReader.AcquireReviewReadAsync(new(
                change == InventoryReadChange.ForeignTenant ? Guid.CreateVersion7() : database.TenantId,
                change == InventoryReadChange.ForeignHuman ? owner : readingHuman,
                change == InventoryReadChange.UnknownSource ? Guid.CreateVersion7() : proposalId!.Value));
            acquiredOutcome = reviewed.Outcome;
            lease = reviewed.Lease;
        }
        else
        {
            var acquired = await reader.AcquireInventoryReadAsync(readRequest);
            acquiredOutcome = acquired.Outcome;
            lease = acquired.Lease;
        }
        if (change is InventoryReadChange.Machine or InventoryReadChange.ForeignHuman or InventoryReadChange.ForeignTenant
            or InventoryReadChange.NonOwner or InventoryReadChange.UnqualifiedReviewer or InventoryReadChange.WrongProfile
            or InventoryReadChange.WrongTaxonomy or InventoryReadChange.WrongMode
            or InventoryReadChange.AlreadyAccepted or InventoryReadChange.UnknownSource)
        {
            await Assert.That(acquiredOutcome).IsNotEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
            await Assert.That(lease).IsNull();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.TenantContext == originalTenant && context.ModerationTrustDomainId == originalTrust).IsTrue();
            return;
        }
        await Assert.That(acquiredOutcome).IsEqualTo(ModerationCurrentPolicyReadOutcome.Allowed);
        await Assert.That(lease).IsNotNull();
        if (sourceOrder.Armed)
        {
            await Assert.That(sourceOrder.ActorsBeforeTenant.Contains(actorId)).IsTrue();
            await Assert.That(sourceOrder.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
            await Assert.That(sourceOrder.LateSources).IsEqualTo(0);
        }
        await using (lease)
        {
            var transaction = context.Database.CurrentTransaction!;
            var original = lease!.Observation;
            await Assert.That(original.TransactionId).IsEqualTo(transaction.TransactionId);
            await Assert.That(original.Materials.Length).IsEqualTo(2);
            await Assert.That(original.Materials.Any(row => row.SessionId == child.Id)).IsTrue();
            await Assert.That(original.Components.Any(row => row.Kind == ModerationClassificationMaterialComponentKind.FormSection
                && row.NativeId == section.Id)).IsTrue();
            await Assert.That(lease.MaterialWriterGeneration).IsEqualTo(expectedHead);
            if (reviewRead)
                await Assert.That(((IModerationClassificationMaterialReviewReadLease)lease).Source.Id).IsEqualTo(proposalId!.Value);
            if (change == InventoryReadChange.Expired) clock.UtcNow = Now.AddMinutes(4);
            if (change == InventoryReadChange.TransactionSwapped) await transaction.RollbackAsync();
            if (change is InventoryReadChange.TransactionSwapped or InventoryReadChange.ConnectionSwapped or InventoryReadChange.ProviderConnectionSwapped)
                await context.Database.UseTransactionAsync(null);
            await using var replacementTransaction = change == InventoryReadChange.TransactionSwapped
                ? await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            using DbConnection replacementConnection = change == InventoryReadChange.ProviderConnectionSwapped
                ? new Npgsql.NpgsqlConnection() : new SqliteConnection();
            if (change is InventoryReadChange.ConnectionSwapped or InventoryReadChange.ProviderConnectionSwapped)
                context.Database.SetDbConnection(replacementConnection);
            if (change == InventoryReadChange.RegressingClock) clock.UtcNow = Now.AddTicks(-1);
            if (change == InventoryReadChange.ProfileChanged)
                await context.Set<ModerationClassificationTaxonomySource>().Where(row => row.Id == taxonomy.SourceId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProfileId, Guid.CreateVersion7()));
            if (change == InventoryReadChange.TaxonomyCategorySubstituted)
                await context.Set<ModerationClassificationTaxonomyCategory>()
                    .Where(row => row.SourceId == taxonomy.SourceId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Meaning, "Substituted category meaning"));
            if (change == InventoryReadChange.OwnerAssignmentLost)
                await context.EventRoleAssignments.Where(row => row.Id == ownership.Id).ExecuteDeleteAsync();
            if (change == InventoryReadChange.ReviewerLost)
                await context.PlatformUserRoles.Where(row => row.UserId == reviewer).ExecuteDeleteAsync();
            if (change == InventoryReadChange.TaxonomyChanged)
                await context.Set<ModerationClassificationTaxonomyAdoption>().Where(row => row.Id == taxonomyAdoption)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, 2L)
                        .SetProperty(row => row.WithdrawnAt, (DateTimeOffset?)Now).SetProperty(row => row.WithdrawnByHumanId, (Guid?)reviewer));
            if (change == InventoryReadChange.AgeChanged)
                await context.Set<ModerationChildAgePolicyAdoption>().Where(row => row.Id == ageAdoption)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, 2L)
                        .SetProperty(row => row.WithdrawnAt, (DateTimeOffset?)Now).SetProperty(row => row.WithdrawnByHumanId, (Guid?)reviewer));
            if (change == InventoryReadChange.GovernanceChanged)
                await context.Set<ModerationConstitutionAdoption>().Where(row => row.Id == governanceAdoption)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, 2L)
                        .SetProperty(row => row.WithdrawnAt, (DateTimeOffset?)Now).SetProperty(row => row.WithdrawnByHumanId, (Guid?)adopter));
            if (change == InventoryReadChange.PublicationChanged)
                await context.ModerationPolicySelections.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, 2L));
            if (change == InventoryReadChange.EpochChanged)
                await context.ModerationQualificationEpochs.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, generation + 1));
            if (change == InventoryReadChange.MaterialSubstituted)
                await context.EventSessions.Where(row => row.Id == child.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            if (change == InventoryReadChange.ComponentSubstituted)
                await context.Set<RegistrationFormSection>().Where(row => row.Id == section.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Title, "Substituted prompt"));
            if (change == InventoryReadChange.ActorChanged)
                await context.ActorPii.Where(row => row.ActorId == actorId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisplayName, "Substituted publisher"));
            if (change == InventoryReadChange.LatestWriterChanged)
            {
                var head = ModerationClassificationMaterialSource.Record(Guid.CreateVersion7(), expectedHead, original.Generation,
                    original.Taxonomy.Source, original.Taxonomy.Adoption, mandate,
                    original.Materials.Select(row => new ModerationClassificationMaterialSelection(
                        Guid.CreateVersion7(), row, [taxonomy.Categories[0].VersionId])).ToImmutableArray(),
                    original.Components, generation + 1, Now, Now.AddMinutes(3));
                context.Set<ModerationClassificationMaterialSource>().Add(head);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }
            if (change == InventoryReadChange.HumanChanged)
                await context.Users.Where(row => row.Id == owner).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            if (change == InventoryReadChange.TenantFrameChanged) context.TenantContext = null;
            if (change == InventoryReadChange.DirtyFrame)
                (await context.EventSessions.SingleAsync(row => row.Id == child.Id)).Title = "Pending change";
            if (change == InventoryReadChange.SourceCategoriesSubstituted)
                await context.Set<ModerationClassificationMaterialCategory>()
                    .Where(row => context.Set<ModerationClassificationMaterialEntry>()
                        .Any(entry => entry.SourceId == proposalId && entry.Id == row.EntryId))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.VersionId, Guid.CreateVersion7()));
            if (change == InventoryReadChange.SourceMandateSubstituted)
                await context.Set<ModerationClassificationMaterialSource>().Where(row => row.Id == proposalId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ReviewGrounds, "Substituted reviewer mandate."));
            var outcome = await lease.RevalidateAsync(default);
            await Assert.That(outcome == ModerationCurrentPolicyReadOutcome.Allowed)
                .IsEqualTo(change is InventoryReadChange.None or InventoryReadChange.NativeSourcesBeforeTenant);
            await Assert.That(lease.Observation == original).IsTrue();
            await Assert.That(lease.MaterialWriterGeneration).IsEqualTo(expectedHead);
            if (change is InventoryReadChange.None or InventoryReadChange.NativeSourcesBeforeTenant)
            {
                if (sourceOrder.Armed) await Assert.That(sourceOrder.LateSources).IsEqualTo(0);
                await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
                await Assert.That(await context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(receipts);
                await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(generation);
                await Assert.That(await context.Set<ModerationClassificationMaterialSource>().CountAsync()).IsEqualTo(reviewRead ? 1 : 0);
            }
        }
        await Assert.That(context.TenantContext == originalTenant && context.ModerationTrustDomainId == originalTrust).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(await lease!.RevalidateAsync(default)).IsEqualTo(ModerationCurrentPolicyReadOutcome.Unavailable);
    }
}
