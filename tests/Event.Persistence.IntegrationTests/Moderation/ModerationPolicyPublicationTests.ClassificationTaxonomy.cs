using System.Data;
using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
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
    public enum TaxonomyChange
    {
        None, MissingReceipt, FabricatedReceipt, MissingAdoptionReceipt, ReviewerRoleLost,
        OwnerRoleLost, GovernanceWithdrawn, ReviewerAvailabilityWithdrawn, OwnerAvailabilityWithdrawn,
        NewSource, WithdrawLatest, Expired, WrongTransaction, DisposedSession, ForeignContext,
        WrongProfile, WrongMode, TrustDrift, TenantDrift, SourceReceiptLostAfterSave,
        ReviewerLostAfterSave, EpochDriftAfterSave, SourceExpiredAfterSave, WithdrawAfterDeparture,
        AdminWithoutMandate, UnknownReplay, AgeSourceReplaced, FinalFlushReceiptLoss,
        RolledBackTransaction, WrongPolicy, WrongPolicyRevision, WrongController, NoAcceptance,
        StaleSourceReplay, GovernanceReplaced, WithdrawReplacement, AdoptionExpiredAfterSave
    }

    [Test]
    [Arguments(TaxonomyChange.None)]
    [Arguments(TaxonomyChange.MissingReceipt)]
    [Arguments(TaxonomyChange.FabricatedReceipt)]
    [Arguments(TaxonomyChange.MissingAdoptionReceipt)]
    [Arguments(TaxonomyChange.ReviewerRoleLost)]
    [Arguments(TaxonomyChange.OwnerRoleLost)]
    [Arguments(TaxonomyChange.GovernanceWithdrawn)]
    [Arguments(TaxonomyChange.ReviewerAvailabilityWithdrawn)]
    [Arguments(TaxonomyChange.OwnerAvailabilityWithdrawn)]
    [Arguments(TaxonomyChange.NewSource)]
    [Arguments(TaxonomyChange.WithdrawLatest)]
    [Arguments(TaxonomyChange.Expired)]
    [Arguments(TaxonomyChange.WrongTransaction)]
    [Arguments(TaxonomyChange.DisposedSession)]
    [Arguments(TaxonomyChange.ForeignContext)]
    [Arguments(TaxonomyChange.WrongProfile)]
    [Arguments(TaxonomyChange.WrongMode)]
    [Arguments(TaxonomyChange.TrustDrift)]
    [Arguments(TaxonomyChange.TenantDrift)]
    [Arguments(TaxonomyChange.SourceReceiptLostAfterSave)]
    [Arguments(TaxonomyChange.ReviewerLostAfterSave)]
    [Arguments(TaxonomyChange.EpochDriftAfterSave)]
    [Arguments(TaxonomyChange.SourceExpiredAfterSave)]
    [Arguments(TaxonomyChange.WithdrawAfterDeparture)]
    [Arguments(TaxonomyChange.AdminWithoutMandate)]
    [Arguments(TaxonomyChange.UnknownReplay)]
    [Arguments(TaxonomyChange.AgeSourceReplaced)]
    [Arguments(TaxonomyChange.FinalFlushReceiptLoss)]
    [Arguments(TaxonomyChange.RolledBackTransaction)]
    [Arguments(TaxonomyChange.WrongPolicy)]
    [Arguments(TaxonomyChange.WrongPolicyRevision)]
    [Arguments(TaxonomyChange.WrongController)]
    [Arguments(TaxonomyChange.NoAcceptance)]
    [Arguments(TaxonomyChange.StaleSourceReplay)]
    [Arguments(TaxonomyChange.GovernanceReplaced)]
    [Arguments(TaxonomyChange.WithdrawReplacement)]
    [Arguments(TaxonomyChange.AdoptionExpiredAfterSave)]
    public async Task Taxonomy_RequiresQualifiedNativeRecordAndSeparateCurrentAcceptance(TaxonomyChange change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var flush = new TaxonomyFlushObserver(clock);
        var finalFlush = new TaxonomyFinalFlush();
        flush.FinalFlush = finalFlush;
        await using var context = database.Context(flush, finalFlush);
        var privacy = database.Privacy.Authority(context, clock);
        if (change == TaxonomyChange.FinalFlushReceiptLoss)
        {
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().Options;
            await using var schema = new EmbeddedPrivacyErasureAuthorityDbContext(options);
            await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
            privacy = new(new TaxonomyAuthorityFactory(options), clock,
                Options.Create(new PrivacyErasureOptions()), applicationContext: context);
        }
        var request = database.Request(ModerationReferenceProfileId.CFG28);
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var adopter = Guid.CreateVersion7();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in new[] { owner, reviewer, adopter })
        {
            var user = new User
            {
                Id = id,
                Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Taxonomy", LastName = "Reviewer" }
            };
            context.Users.Add(user);
            context.PlatformUserRoles.Add(new PlatformUserRole
            { Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role });
        }
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
            generation++;
            return result.Receipt!;
        }
        var ownerAvailability = Guid.CreateVersion7();
        var reviewerAvailability = Guid.CreateVersion7();
        foreach (var entry in new[] { (owner, ownerAvailability), (reviewer, reviewerAvailability) })
            await Known(entry.Item1, new RecordModerationConstitutionAvailability
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                AvailabilityId = entry.Item2,
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
                new(ownerAvailability, 1, 3, owner, owner, "Reviewed canonical controller.",
                    "Qualified taxonomy owner for content semantics and child-risk floors.", "Independent governance review."),
                new(reviewerAvailability, 1, 4, reviewer, reviewer, "Reviewed distinct canonical controller.",
                    "Qualified independent taxonomy reviewer for child safeguarding and audience floors.", "No owner conflict.")
            ]
        };
        await Known(database.AdminId, constitution);
        var governanceAdoption = new AdoptModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = constitution.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(7)
        };
        await Known(adopter, governanceAdoption);
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
                "Independent canonical controllers.", "Privacy mandate.", "Child safeguarding mandate.", "Collection mandate.")
        };
        await Known(database.AdminId, age);
        var ageAdoption = new AdoptModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = age.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(6)
        };
        await Known(reviewer, ageAdoption);
        var record = new RecordModerationClassificationTaxonomySource
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
            Mandate = new(owner, owner, reviewer, reviewer,
                "Adopted coordinator owns this taxonomy and its finite category inventory.",
                "Independent substantive content meaning, child-risk and audience-floor review."),
            Categories =
            [
                new(Guid.CreateVersion7(), 1, "general", EventContentClassificationVersion.CategoryKind.General,
                    6, "Reviewed general audience content.", "General content has an explicitly reviewed floor."),
                new(Guid.CreateVersion7(), 2, "debate", EventContentClassificationVersion.CategoryKind.Debate,
                    18, "Debate content independent of event format.", "Adopted operator Debate rule.")
            ]
        };
        if (change == TaxonomyChange.AdminWithoutMandate)
        {
            await Assert.That((await Writer(database.AdminId).ExecuteAsync(record with
            { Mandate = record.Mandate with { OwnerHumanId = database.AdminId, OwnerControllerHumanId = database.AdminId } })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        if (change is TaxonomyChange.WrongPolicy or TaxonomyChange.WrongPolicyRevision or TaxonomyChange.WrongController)
        {
            var invalid = change switch
            {
                TaxonomyChange.WrongPolicy => record with { PolicyId = Guid.CreateVersion7() },
                TaxonomyChange.WrongPolicyRevision => record with { ExpectedPolicyRevision = 2 },
                _ => record with { Mandate = record.Mandate with { OwnerControllerHumanId = adopter } }
            };
            await Assert.That((await Writer(owner).ExecuteAsync(invalid)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        if (change is TaxonomyChange.SourceReceiptLostAfterSave or TaxonomyChange.EpochDriftAfterSave or TaxonomyChange.SourceExpiredAfterSave)
        {
            flush.Change = change;
            await Assert.That((await Writer(owner).ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationClassificationTaxonomySource>().AnyAsync()).IsFalse();
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            return;
        }
        var recorded = await Known(owner, record);
        var replay = await Writer(owner).ExecuteAsync(record);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(replay.Receipt!.Id).IsEqualTo(recorded.Id);
        await Assert.That(replay.Receipt.CompletedAt).IsEqualTo(recorded.CompletedAt);
        await Assert.That(replay.Receipt.QualificationRevision).IsEqualTo(recorded.QualificationRevision);
        await Assert.That((await Writer(owner).ExecuteAsync(record with { ValidUntil = record.ValidUntil.AddTicks(-1) })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        if (change == TaxonomyChange.NoAcceptance)
        {
            await using var original = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var current = await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireAsync(database.Scope, record.PublicationId, default);
            using var pending = publications.BorrowConsumerEvidence(current!, policy);
            var reader = new ModerationClassificationTaxonomySourceOwner(context,
                new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, clock), clock);
            await Assert.That(await reader.AcquireSourceAsync(pending, original.TransactionId,
                record.TaxonomyId, record.ProfileId, record.Mode, clock.UtcNow, default)).IsNotNull();
            await Assert.That(await reader.AcquireCurrentAsync(pending, original.TransactionId,
                record.TaxonomyId, record.ProfileId, record.Mode, clock.UtcNow, default)).IsNull();
            return;
        }
        var adopt = new AdoptModerationClassificationTaxonomySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = generation,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = generation,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await Writer(owner).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Writer(adopter).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        if (change is TaxonomyChange.ReviewerLostAfterSave or TaxonomyChange.FinalFlushReceiptLoss or TaxonomyChange.AdoptionExpiredAfterSave)
        {
            flush.Change = change;
            flush.Reviewer = reviewer;
            await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationClassificationTaxonomyAdoption>().AnyAsync()).IsFalse();
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == adopt.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(generation);
            if (change == TaxonomyChange.FinalFlushReceiptLoss)
                await Assert.That(finalFlush.Observed).IsTrue();
            return;
        }
        await Known(reviewer, adopt);
        await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        if (change == TaxonomyChange.UnknownReplay)
        {
            await Assert.That((await Writer(owner).ExecuteAsync(record with { OperationId = Guid.CreateVersion7() })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
            return;
        }
        if (change is TaxonomyChange.MissingReceipt or TaxonomyChange.MissingAdoptionReceipt)
            await context.ModerationQualificationMutationReceipts.Where(row =>
                row.Id == (change == TaxonomyChange.MissingReceipt ? record.OperationId : adopt.OperationId)).ExecuteDeleteAsync();
        if (change == TaxonomyChange.FabricatedReceipt)
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == record.OperationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestDigest, new string('a', 64)));
        if (change is TaxonomyChange.OwnerRoleLost or TaxonomyChange.ReviewerRoleLost)
            await context.PlatformUserRoles.Where(row => row.UserId ==
                (change == TaxonomyChange.OwnerRoleLost ? owner : reviewer)).ExecuteDeleteAsync();
        if (change == TaxonomyChange.GovernanceWithdrawn)
            await Known(adopter, new WithdrawModerationConstitutionAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                AdoptionId = governanceAdoption.AdoptionId,
                ExpectedAdoptionRevision = 1
            });
        if (change is TaxonomyChange.OwnerAvailabilityWithdrawn or TaxonomyChange.ReviewerAvailabilityWithdrawn)
            await Known(change == TaxonomyChange.OwnerAvailabilityWithdrawn ? owner : reviewer,
                new WithdrawModerationConstitutionAvailability
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = database.Scope,
                    ExpectedQualificationRevision = generation,
                    AvailabilityId = change == TaxonomyChange.OwnerAvailabilityWithdrawn ? ownerAvailability : reviewerAvailability,
                    ExpectedAvailabilityRevision = 1
                });
        if (change == TaxonomyChange.AgeSourceReplaced)
            await Known(database.AdminId, age with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = generation });
        if (change == TaxonomyChange.GovernanceReplaced)
            await Known(database.AdminId, constitution with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = generation });
        if (change is TaxonomyChange.NewSource or TaxonomyChange.StaleSourceReplay or TaxonomyChange.WithdrawReplacement)
        {
            var replacement = record with
            {
                OperationId = Guid.CreateVersion7(),
                SourceId = Guid.CreateVersion7(),
                ExpectedQualificationRevision = generation,
                ExpectedTaxonomyGeneration = record.ExpectedQualificationRevision + 1,
                Categories = [record.Categories[0] with { VersionId = Guid.CreateVersion7() }]
            };
            await Known(owner, replacement);
            if (change == TaxonomyChange.StaleSourceReplay)
            {
                await Assert.That((await Writer(owner).ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                return;
            }
            if (change == TaxonomyChange.WithdrawReplacement)
            {
                adopt = adopt with
                {
                    OperationId = Guid.CreateVersion7(),
                    SourceId = replacement.SourceId,
                    AdoptionId = Guid.CreateVersion7(),
                    ExpectedQualificationRevision = generation,
                    ExpectedSourceWriterGeneration = generation
                };
                await Known(reviewer, adopt);
            }
        }
        if (change is TaxonomyChange.WithdrawLatest or TaxonomyChange.WithdrawAfterDeparture or TaxonomyChange.WithdrawReplacement)
        {
            if (change == TaxonomyChange.WithdrawAfterDeparture)
            {
                clock.UtcNow = Now.AddHours(1);
                await context.PlatformUserRoles.Where(row => row.UserId == owner || row.UserId == reviewer).ExecuteDeleteAsync();
            }
            var withdrawal = new WithdrawModerationClassificationTaxonomyAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = generation,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1
            };
            await Known(adopter, withdrawal);
            await Assert.That((await Writer(adopter).ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == TaxonomyChange.Expired) clock.UtcNow = adopt.ValidUntil;
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        using var session = publications.BorrowConsumerEvidence(authority!, policy);
        if (change == TaxonomyChange.DisposedSession) session.Dispose();
        if (change == TaxonomyChange.RolledBackTransaction) await transaction.RollbackAsync();
        if (change == TaxonomyChange.TrustDrift) context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == TaxonomyChange.TenantDrift) context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        await using var foreign = database.Context();
        var ownerContext = change == TaxonomyChange.ForeignContext ? foreign : context;
        var nativeOwner = new ModerationClassificationTaxonomySourceOwner(ownerContext,
            new ModerationPolicyDefinitionSourceOwner(ownerContext, admin, machine, database.Inventory, clock), clock);
        var observed = await nativeOwner.AcquireCurrentAsync(session,
            change == TaxonomyChange.WrongTransaction ? Guid.CreateVersion7() : transaction.TransactionId, record.TaxonomyId,
            change == TaxonomyChange.WrongProfile ? Guid.CreateVersion7() : record.ProfileId,
            change == TaxonomyChange.WrongMode ? ModerationNativeMode.RemoteStrict : record.Mode, clock.UtcNow, default);
        if (change == TaxonomyChange.None)
        {
            await Assert.That(observed!.Source.Id).IsEqualTo(record.SourceId);
            await Assert.That(observed.Adoption.Id).IsEqualTo(adopt.AdoptionId);
            await Assert.That(observed.Categories.Length).IsEqualTo(2);
            await Assert.That(observed.Categories[1].MinimumAge).IsEqualTo(18);
            await Assert.That(observed.Categories[0].OwnerHumanId).IsEqualTo(owner);
            await Assert.That(observed.Source.RecordedAt).IsEqualTo(Now);
            await Assert.That(observed.Source.ValidUntil).IsEqualTo(record.ValidUntil);
            await Assert.That(observed.Source.WriterGeneration).IsEqualTo(recorded.QualificationRevision);
            await Assert.That(observed.Adoption.AdoptedAt).IsEqualTo(Now);
            await Assert.That(observed.ValidUntil).IsEqualTo(adopt.ValidUntil);
        }
        else await Assert.That(observed).IsNull();
    }

    private sealed class TaxonomyFlushObserver(DeploymentClock clock) : SaveChangesInterceptor
    {
        public TaxonomyChange Change { get; set; }
        public Guid Reviewer { get; set; }
        public TaxonomyFinalFlush? FinalFlush { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            var source = context.ChangeTracker.Entries<ModerationClassificationTaxonomySource>().Select(row => row.Entity).SingleOrDefault();
            var adoption = context.ChangeTracker.Entries<ModerationClassificationTaxonomyAdoption>().Select(row => row.Entity).SingleOrDefault();
            if (source is not null && Change == TaxonomyChange.SourceExpiredAfterSave) clock.UtcNow = source.ValidUntil;
            if (source is not null && Change == TaxonomyChange.EpochDriftAfterSave)
                await context.Set<ModerationQualificationEpoch>().ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            if (source is not null && Change == TaxonomyChange.SourceReceiptLostAfterSave)
                await context.Set<ModerationQualificationMutationReceipt>().Where(row => row.SourceId == source.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            if (adoption is not null && Change == TaxonomyChange.ReviewerLostAfterSave)
                await context.Set<PlatformUserRole>().Where(row => row.UserId == Reviewer).ExecuteDeleteAsync(cancellationToken);
            if (adoption is not null && Change == TaxonomyChange.AdoptionExpiredAfterSave) clock.UtcNow = adoption.ValidUntil;
            if (adoption is not null && Change == TaxonomyChange.FinalFlushReceiptLoss)
            {
                var primary = (ExploreDbContext)context;
                if (!primary.IdentityFenceOwnsTransaction)
                    throw new InvalidOperationException("Final-flush witness requires the actual colocated identity transaction.");
                primary.DisclosureMutations.Enlist([adoption.TenantId]);
                FinalFlush!.AdoptionId = adoption.Id;
                FinalFlush.Transaction = primary.Database.CurrentTransaction!.GetDbTransaction();
            }
            return result;
        }
    }

    private sealed class TaxonomyFinalFlush : DbCommandInterceptor
    {
        public Guid? AdoptionId { get; set; }
        public DbTransaction? Transaction { get; set; }
        public bool Observed { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Observed || AdoptionId is not { } id || eventData.Context is not ExploreDbContext primary) return result;
            var entity = primary.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = primary.GetService<ISqlGenerationHelper>();
            var column = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoveryRevision.DisclosureEpoch))!.GetColumnName(table)!);
            var update = $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {column} = ";
            if (command.Parameters.Count != 3 || !command.CommandText.StartsWith(update, StringComparison.Ordinal)) return result;
            if (result != 1 || !ReferenceEquals(command.Transaction, Transaction) || !primary.IdentityFenceOwnsTransaction)
                throw new InvalidOperationException("Taxonomy dependency must flush on its original native identity transaction.");
            Observed = true;
            await primary.Set<ModerationQualificationMutationReceipt>().Where(row => row.SourceId == id)
                .ExecuteDeleteAsync(cancellationToken);
            return result;
        }
    }

    private sealed class TaxonomyAuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }
}
