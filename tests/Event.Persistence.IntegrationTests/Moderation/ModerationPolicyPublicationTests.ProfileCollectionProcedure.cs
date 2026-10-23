using System.Data;
using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    public enum ProcedureChange
    {
        None, Machine, Anonymous, WrongOwner, WrongReviewer, SharedController,
        StaleParentSourceRevision, StaleParentSourceWriter, WrongParentAdoption,
        StaleParentAdoptionRevision, StaleParentAdoptionWriter, WrongScope,
        SourceRenewal, AdoptionRenewal, ChangedReplayBody, ChangedReplayGeneration,
        ChangedReplayIdentity, ParentWithdrawn, ParentExpired, ParentSuperseded,
        MissingSourceReceipt, MissingAdoptionReceipt, SourceReceiptDrift, AdoptionReceiptDrift,
        OwnerRoleLost, ReviewerRoleLost, NoAdoption, NewUnadoptedSource, Expired,
        WrongTransaction, ForeignContext, DisposedSession, RolledBackTransaction,
        WrongProfile, WrongMode, TrustDrift, TenantDrift,
        SourceExpiredAtSave, AdoptionExpiredAtSave, ParentWithdrawnAtSave,
        ParentReceiptLostAtSave, WriterEpochDriftAtSave, OwnerLostAtSave,
        FinalFlushReceiptLoss, FinalFlushParentMutation, WithdrawAfterExpiry,
        ParentPolicyDrift, ParentRevisionDrift, ParentPublicationDrift, ParentProfileDrift, ParentModeDrift,
        ParentDefinitionDrift, ParentWriterDrift, ParentMandateTooLong,
        WrongSourceUuidVersion, SourceBodyDrift, ReceiptPrincipalDrift, ReceiptGenerationDrift,
        ProcedureStringBoundary, SourceIdentityCannotRenew, AdoptedSourceCannotRenew
    }

    [Test]
    [Arguments(ProcedureChange.None)]
    [Arguments(ProcedureChange.Machine)]
    [Arguments(ProcedureChange.Anonymous)]
    [Arguments(ProcedureChange.WrongOwner)]
    [Arguments(ProcedureChange.WrongReviewer)]
    [Arguments(ProcedureChange.SharedController)]
    [Arguments(ProcedureChange.StaleParentSourceRevision)]
    [Arguments(ProcedureChange.StaleParentSourceWriter)]
    [Arguments(ProcedureChange.WrongParentAdoption)]
    [Arguments(ProcedureChange.StaleParentAdoptionRevision)]
    [Arguments(ProcedureChange.StaleParentAdoptionWriter)]
    [Arguments(ProcedureChange.WrongScope)]
    [Arguments(ProcedureChange.SourceRenewal)]
    [Arguments(ProcedureChange.AdoptionRenewal)]
    [Arguments(ProcedureChange.ChangedReplayBody)]
    [Arguments(ProcedureChange.ChangedReplayGeneration)]
    [Arguments(ProcedureChange.ChangedReplayIdentity)]
    [Arguments(ProcedureChange.ParentWithdrawn)]
    [Arguments(ProcedureChange.ParentExpired)]
    [Arguments(ProcedureChange.ParentSuperseded)]
    [Arguments(ProcedureChange.MissingSourceReceipt)]
    [Arguments(ProcedureChange.MissingAdoptionReceipt)]
    [Arguments(ProcedureChange.SourceReceiptDrift)]
    [Arguments(ProcedureChange.AdoptionReceiptDrift)]
    [Arguments(ProcedureChange.OwnerRoleLost)]
    [Arguments(ProcedureChange.ReviewerRoleLost)]
    [Arguments(ProcedureChange.NoAdoption)]
    [Arguments(ProcedureChange.NewUnadoptedSource)]
    [Arguments(ProcedureChange.Expired)]
    [Arguments(ProcedureChange.WrongTransaction)]
    [Arguments(ProcedureChange.ForeignContext)]
    [Arguments(ProcedureChange.DisposedSession)]
    [Arguments(ProcedureChange.RolledBackTransaction)]
    [Arguments(ProcedureChange.WrongProfile)]
    [Arguments(ProcedureChange.WrongMode)]
    [Arguments(ProcedureChange.TrustDrift)]
    [Arguments(ProcedureChange.TenantDrift)]
    [Arguments(ProcedureChange.SourceExpiredAtSave)]
    [Arguments(ProcedureChange.AdoptionExpiredAtSave)]
    [Arguments(ProcedureChange.ParentWithdrawnAtSave)]
    [Arguments(ProcedureChange.ParentReceiptLostAtSave)]
    [Arguments(ProcedureChange.WriterEpochDriftAtSave)]
    [Arguments(ProcedureChange.OwnerLostAtSave)]
    [Arguments(ProcedureChange.FinalFlushReceiptLoss)]
    [Arguments(ProcedureChange.FinalFlushParentMutation)]
    [Arguments(ProcedureChange.WithdrawAfterExpiry)]
    [Arguments(ProcedureChange.ParentPolicyDrift)]
    [Arguments(ProcedureChange.ParentRevisionDrift)]
    [Arguments(ProcedureChange.ParentPublicationDrift)]
    [Arguments(ProcedureChange.ParentProfileDrift)]
    [Arguments(ProcedureChange.ParentModeDrift)]
    [Arguments(ProcedureChange.ParentDefinitionDrift)]
    [Arguments(ProcedureChange.ParentWriterDrift)]
    [Arguments(ProcedureChange.ParentMandateTooLong)]
    [Arguments(ProcedureChange.WrongSourceUuidVersion)]
    [Arguments(ProcedureChange.SourceBodyDrift)]
    [Arguments(ProcedureChange.ReceiptPrincipalDrift)]
    [Arguments(ProcedureChange.ReceiptGenerationDrift)]
    [Arguments(ProcedureChange.ProcedureStringBoundary)]
    [Arguments(ProcedureChange.SourceIdentityCannotRenew)]
    [Arguments(ProcedureChange.AdoptedSourceCannotRenew)]
    public async Task ProfileProcedure_RequiresOriginalNativeParentAndIndependentAcceptance(ProcedureChange change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var finalFlush = new ProcedureFinalFlush(clock);
        var flush = new ProcedureFlushObserver(clock, finalFlush);
        await using var context = database.Context(flush, finalFlush);
        var privacy = database.Privacy.Authority(context, clock);
        if (change is ProcedureChange.FinalFlushReceiptLoss or ProcedureChange.FinalFlushParentMutation)
        {
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().Options;
            await using var schema = new EmbeddedPrivacyErasureAuthorityDbContext(options);
            await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
            privacy = new(new ProcedureAuthorityFactory(options), clock,
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
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in new[] { owner, reviewer })
        {
            var human = new User
            {
                Id = id,
                Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Procedure", LastName = "Owner" }
            };
            context.Users.Add(human);
            context.PlatformUserRoles.Add(new PlatformUserRole
            { Id = Guid.CreateVersion7(), UserId = id, User = human, RoleId = role.Id, Role = role });
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
        var age = new RecordModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            SourceId = Guid.CreateVersion7(),
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId,
            ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded,
            ValidUntil = Now.AddMinutes(8),
            Terms = ChildAgeTerms(),
            Ownership = new(reviewer, reviewer, owner, owner, database.AdminId,
                "Distinct controllers.", "Independent privacy mandate.", "Child-risk mandate.", "Collection owner mandate.")
        };
        await Assert.That((await Writer(database.AdminId).ExecuteAsync(age)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var parentAdoption = new AdoptModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            SourceId = age.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 3,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(7)
        };
        await Assert.That((await Writer(reviewer).ExecuteAsync(parentAdoption)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var record = new RecordModerationProfileCollectionProcedureSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            SourceId = Guid.CreateVersion7(),
            ChildAgeSourceId = age.SourceId,
            ExpectedChildAgeSourceRevision = 1,
            ExpectedChildAgeSourceWriterGeneration = 3,
            ChildAgeAdoptionId = parentAdoption.AdoptionId,
            ExpectedChildAgeAdoptionRevision = 1,
            ExpectedChildAgeAdoptionWriterGeneration = 4,
            Procedure = ProcedureVocabulary(),
            ValidUntil = Now.AddMinutes(6)
        };
        if (change == ProcedureChange.ProcedureStringBoundary)
        {
            var grounds = string.Concat(Enumerable.Repeat("\U0001F642", 1024));
            record = record with
            {
                Procedure = new(ModerationIndividualPurpose.ProfileCompletion,
                ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
                [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration],
                [ModerationProfileNormalFieldKind.DateOfBirth], grounds, grounds)
            };
        }
        flush.ParentId = parentAdoption.AdoptionId;
        flush.ParentReceiptId = parentAdoption.OperationId;
        flush.OwnerId = owner;
        if (change is ProcedureChange.Machine or ProcedureChange.Anonymous)
        {
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var anonymous = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var caller = new MachinePrincipalAccessor(http);
            if (change == ProcedureChange.Machine)
                caller.SetPrincipal(new("procedure-machine", database.TenantId, ExternalApiKeyOwnerType.User, owner, []));
            var writer = new ModerationQualificationRepository(context, anonymous, caller, publications,
                new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
                clock, privacy, deploymentInventory: database.Inventory);
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(change == ProcedureChange.Machine
                ? ModerationQualificationWriteOutcome.Denied : ModerationQualificationWriteOutcome.Unauthenticated);
            return;
        }
        if (change == ProcedureChange.SharedController)
            await context.Set<ModerationChildAgePolicySource>().Where(row => row.Id == age.SourceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CollectionControllerHumanId, reviewer));
        if (change == ProcedureChange.WrongSourceUuidVersion)
        {
            await Assert.That(async () => await Writer(owner).ExecuteAsync(record with { SourceId = Guid.NewGuid() }))
                .Throws<ArgumentException>();
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationProfileCollectionProcedureSource>().CountAsync()).IsEqualTo(0);
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4);
            return;
        }
        var invalid = change switch
        {
            ProcedureChange.StaleParentSourceRevision => record with { ExpectedChildAgeSourceRevision = 2 },
            ProcedureChange.StaleParentSourceWriter => record with { ExpectedChildAgeSourceWriterGeneration = 2 },
            ProcedureChange.WrongParentAdoption => record with { ChildAgeAdoptionId = Guid.CreateVersion7() },
            ProcedureChange.StaleParentAdoptionRevision => record with { ExpectedChildAgeAdoptionRevision = 2 },
            ProcedureChange.StaleParentAdoptionWriter => record with { ExpectedChildAgeAdoptionWriterGeneration = 3 },
            ProcedureChange.WrongScope => record with { Target = database.Scope.Parent! },
            ProcedureChange.SourceRenewal => record with { ValidUntil = parentAdoption.ValidUntil.AddTicks(1) },
            _ => record
        };
        if (change is ProcedureChange.WrongOwner or ProcedureChange.SharedController
            or ProcedureChange.StaleParentSourceRevision or ProcedureChange.StaleParentSourceWriter
            or ProcedureChange.WrongParentAdoption or ProcedureChange.StaleParentAdoptionRevision
            or ProcedureChange.StaleParentAdoptionWriter or ProcedureChange.WrongScope or ProcedureChange.SourceRenewal)
        {
            await Assert.That((await Writer(change == ProcedureChange.WrongOwner ? database.AdminId : owner)
                .ExecuteAsync(invalid)).Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            return;
        }
        if (change is ProcedureChange.SourceExpiredAtSave or ProcedureChange.ParentWithdrawnAtSave
            or ProcedureChange.ParentReceiptLostAtSave or ProcedureChange.WriterEpochDriftAtSave or ProcedureChange.OwnerLostAtSave)
        {
            flush.Change = change;
            await Assert.That((await Writer(owner).ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationProfileCollectionProcedureSource>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4);
            return;
        }
        var recorded = await Writer(owner).ExecuteAsync(record);
        await Assert.That(recorded.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(recorded.Receipt!.QualificationRevision).IsEqualTo(5);
        await Assert.That((await Writer(owner).ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == ProcedureChange.SourceIdentityCannotRenew)
        {
            await Assert.That((await Writer(owner).ExecuteAsync(record with
            { OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 5, ValidUntil = record.ValidUntil.AddTicks(1) })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
            return;
        }
        if (change is ProcedureChange.ChangedReplayBody or ProcedureChange.ChangedReplayGeneration or ProcedureChange.ChangedReplayIdentity)
        {
            var replay = change == ProcedureChange.ChangedReplayBody ? record with { Procedure = ProcedureVocabulary(true) }
                : change == ProcedureChange.ChangedReplayGeneration ? record with { ExpectedChildAgeSourceWriterGeneration = 4 } : record;
            await Assert.That((await Writer(change == ProcedureChange.ChangedReplayIdentity ? reviewer : owner).ExecuteAsync(replay)).Outcome)
                .IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            return;
        }
        var adopt = new AdoptModerationProfileCollectionProcedureSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 5,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 5,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(5)
        };
        if (change is ProcedureChange.WrongReviewer or ProcedureChange.AdoptionRenewal)
        {
            await Assert.That((await Writer(change == ProcedureChange.WrongReviewer ? owner : reviewer).ExecuteAsync(
                change == ProcedureChange.AdoptionRenewal ? adopt with { ValidUntil = record.ValidUntil.AddTicks(1) } : adopt)).Outcome)
                .IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            return;
        }
        if (change is ProcedureChange.AdoptionExpiredAtSave or ProcedureChange.FinalFlushReceiptLoss or ProcedureChange.FinalFlushParentMutation)
        {
            flush.Change = change;
            await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            if (change != ProcedureChange.AdoptionExpiredAtSave) await Assert.That(finalFlush.Observed).IsTrue();
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationProfileCollectionProcedureAdoption>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == adopt.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5);
            return;
        }
        if (change != ProcedureChange.NoAdoption)
        {
            await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == ProcedureChange.AdoptedSourceCannotRenew)
        {
            await Assert.That((await Writer(reviewer).ExecuteAsync(adopt with
            { OperationId = Guid.CreateVersion7(), AdoptionId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
            return;
        }
        if (change is ProcedureChange.ParentWithdrawn or ProcedureChange.WithdrawAfterExpiry)
        {
            if (change == ProcedureChange.WithdrawAfterExpiry) clock.UtcNow = Now.AddMinutes(20);
            var withdrawal = new WithdrawModerationProfileCollectionProcedureAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 6,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1
            };
            if (change == ProcedureChange.WithdrawAfterExpiry)
            {
                await Assert.That((await Writer(database.AdminId).ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                await Assert.That((await Writer(database.AdminId).ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            }
            else await Assert.That((await Writer(database.AdminId).ExecuteAsync(new WithdrawModerationChildAgePolicyAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 6,
                AdoptionId = parentAdoption.AdoptionId,
                ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == ProcedureChange.ParentSuperseded)
            await Assert.That((await Writer(database.AdminId).ExecuteAsync(age with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == ProcedureChange.NewUnadoptedSource)
            await Assert.That((await Writer(owner).ExecuteAsync(record with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var parentRows = context.Set<ModerationChildAgePolicySource>().Where(row => row.Id == age.SourceId);
        switch (change)
        {
            case ProcedureChange.ParentPolicyDrift:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PolicyId, Guid.CreateVersion7()));
                break;
            case ProcedureChange.ParentRevisionDrift:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PolicyRevision, 2));
                break;
            case ProcedureChange.ParentPublicationDrift:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublicationId, Guid.CreateVersion7()));
                break;
            case ProcedureChange.ParentProfileDrift:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProfileId, Guid.CreateVersion7()));
                break;
            case ProcedureChange.ParentModeDrift:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ModeCode, 2));
                break;
            case ProcedureChange.ParentDefinitionDrift:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DefinitionId, Guid.CreateVersion7()));
                break;
            case ProcedureChange.ParentWriterDrift:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.WriterGeneration, 7));
                break;
            case ProcedureChange.ParentMandateTooLong:
                await parentRows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CollectionMandateBasis, new string('a', 2049)));
                break;
            case ProcedureChange.SourceBodyDrift:
                await context.Set<ModerationProfileCollectionProcedureSource>().Where(row => row.Id == record.SourceId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Procedure, ProcedureVocabulary(true)));
                break;
            case ProcedureChange.ReceiptPrincipalDrift:
                await context.ModerationQualificationMutationReceipts.Where(row => row.Id == record.OperationId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PrincipalId, reviewer));
                break;
            case ProcedureChange.ReceiptGenerationDrift:
                await context.ModerationQualificationMutationReceipts.Where(row => row.Id == adopt.OperationId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.QualificationRevision, 7));
                break;
        }
        if (change is ProcedureChange.MissingSourceReceipt or ProcedureChange.MissingAdoptionReceipt
            or ProcedureChange.SourceReceiptDrift or ProcedureChange.AdoptionReceiptDrift)
        {
            var id = change is ProcedureChange.MissingSourceReceipt or ProcedureChange.SourceReceiptDrift ? record.OperationId : adopt.OperationId;
            if (change is ProcedureChange.MissingSourceReceipt or ProcedureChange.MissingAdoptionReceipt)
                await context.ModerationQualificationMutationReceipts.Where(row => row.Id == id).ExecuteDeleteAsync();
            else await context.ModerationQualificationMutationReceipts.Where(row => row.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestDigest, new string('a', 64)));
        }
        if (change is ProcedureChange.OwnerRoleLost or ProcedureChange.ReviewerRoleLost)
            await context.PlatformUserRoles.Where(row => row.UserId == (change == ProcedureChange.OwnerRoleLost ? owner : reviewer)).ExecuteDeleteAsync();
        if (change == ProcedureChange.ParentExpired) clock.UtcNow = parentAdoption.ValidUntil;
        if (change == ProcedureChange.Expired) clock.UtcNow = adopt.ValidUntil;
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        using var session = publications.BorrowConsumerEvidence(authority!, policy);
        if (change == ProcedureChange.DisposedSession) session.Dispose();
        if (change == ProcedureChange.RolledBackTransaction) await transaction.RollbackAsync();
        if (change == ProcedureChange.TrustDrift) context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == ProcedureChange.TenantDrift) context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        await using var foreign = database.Context();
        var readerContext = change == ProcedureChange.ForeignContext ? foreign : context;
        var reader = new ModerationProfileCollectionProcedureSourceOwner(readerContext,
            new ModerationPolicyDefinitionSourceOwner(readerContext, admin, machine, database.Inventory, clock), clock);
        var observed = await reader.AcquireCurrentAsync(session,
            change == ProcedureChange.WrongTransaction ? Guid.CreateVersion7() : transaction.TransactionId,
            change == ProcedureChange.WrongProfile ? Guid.CreateVersion7() : age.ProfileId,
            change == ProcedureChange.WrongMode ? ModerationNativeMode.RemoteStrict : age.Mode, clock.UtcNow, default);
        if (change is ProcedureChange.None or ProcedureChange.ProcedureStringBoundary)
        {
            await Assert.That(observed!.Source.Id).IsEqualTo(record.SourceId);
            await Assert.That(observed.Source.ChildAgeSourceWriterGeneration).IsEqualTo(3);
            await Assert.That(observed.Source.ChildAgeAdoptionWriterGeneration).IsEqualTo(4);
            await Assert.That(observed.Adoption.Id).IsEqualTo(adopt.AdoptionId);
            await Assert.That(observed.Adoption.WriterGeneration).IsEqualTo(6);
            await Assert.That(observed.ValidUntil).IsEqualTo(adopt.ValidUntil);
            await Assert.That(observed.Source.Procedure.NormalFields.Count).IsEqualTo(1);
            if (change == ProcedureChange.ProcedureStringBoundary)
                await Assert.That(observed.Source.Procedure.GuardianRelationshipReviewGrounds.Length).IsEqualTo(2048);
            await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        }
        else await Assert.That(observed).IsNull();
    }

    private static ModerationProfileCollectionProcedure ProcedureVocabulary(bool includeName = false) => new(
        ModerationIndividualPurpose.ProfileCompletion,
        ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
        [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration],
        includeName ? [ModerationProfileNormalFieldKind.DateOfBirth, ModerationProfileNormalFieldKind.FirstName]
            : [ModerationProfileNormalFieldKind.DateOfBirth],
        "Independent native guardian relationship review.", "Independent native manual issuer qualification.");

    private sealed class ProcedureFlushObserver(DeploymentClock clock, ProcedureFinalFlush finalFlush) : SaveChangesInterceptor
    {
        public ProcedureChange Change { get; set; }
        public Guid ParentId { get; set; }
        public Guid ParentReceiptId { get; set; }
        public Guid OwnerId { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            var source = context.ChangeTracker.Entries<ModerationProfileCollectionProcedureSource>().Select(row => row.Entity).SingleOrDefault();
            var adoption = context.ChangeTracker.Entries<ModerationProfileCollectionProcedureAdoption>().Select(row => row.Entity).SingleOrDefault();
            if (source is null && adoption is null) return result;
            if (Change == ProcedureChange.SourceExpiredAtSave) clock.UtcNow = source!.ValidUntil;
            if (Change == ProcedureChange.AdoptionExpiredAtSave) clock.UtcNow = adoption!.ValidUntil;
            if (Change == ProcedureChange.ParentWithdrawnAtSave)
                await context.Set<ModerationChildAgePolicyAdoption>().Where(row => row.Id == ParentId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, 2)
                        .SetProperty(row => row.WithdrawnByHumanId, OwnerId).SetProperty(row => row.WithdrawnAt, Now), cancellationToken);
            if (Change == ProcedureChange.ParentReceiptLostAtSave)
                await context.Set<ModerationQualificationMutationReceipt>().Where(row => row.Id == ParentReceiptId).ExecuteDeleteAsync(cancellationToken);
            if (Change == ProcedureChange.WriterEpochDriftAtSave)
                await context.Set<ModerationQualificationEpoch>().ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            if (Change == ProcedureChange.OwnerLostAtSave)
                await context.Set<PlatformUserRole>().Where(row => row.UserId == OwnerId).ExecuteDeleteAsync(cancellationToken);
            if (adoption is not null && Change is ProcedureChange.FinalFlushReceiptLoss or ProcedureChange.FinalFlushParentMutation)
            {
                var primary = (ExploreDbContext)context;
                if (!primary.IdentityFenceOwnsTransaction) throw new InvalidOperationException("Procedure witness requires colocated privacy custody.");
                primary.DisclosureMutations.Enlist([adoption.TenantId]);
                finalFlush.Change = Change;
                finalFlush.AdoptionId = adoption.Id;
                finalFlush.ParentId = ParentId;
                finalFlush.Transaction = primary.Database.CurrentTransaction!.GetDbTransaction();
            }
            return result;
        }
    }

    private sealed class ProcedureFinalFlush(DeploymentClock clock) : DbCommandInterceptor
    {
        public ProcedureChange Change { get; set; }
        public Guid? AdoptionId { get; set; }
        public Guid ParentId { get; set; }
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
            if (command.Parameters.Count != 3 || !command.CommandText.StartsWith(
                $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {column} = ", StringComparison.Ordinal)) return result;
            if (result != 1 || !ReferenceEquals(command.Transaction, Transaction) || !primary.IdentityFenceOwnsTransaction)
                throw new InvalidOperationException("Procedure final mutation must use its original native transaction and connection.");
            Observed = true;
            if (Change == ProcedureChange.FinalFlushReceiptLoss)
                await primary.Set<ModerationQualificationMutationReceipt>().Where(row => row.SourceId == id).ExecuteDeleteAsync(cancellationToken);
            else
            {
                clock.UtcNow = Now.AddTicks(1);
                await primary.Set<ModerationChildAgePolicyAdoption>().Where(row => row.Id == ParentId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, clock.UtcNow), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ProcedureAuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }
}
