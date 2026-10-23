using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RecognitionDisposition_RetainedCommitSurvivesLostConsumptionAndLaterWithdrawal_OnlyWithAcceptedOwnerProof(
        bool loseOwnerAcceptance)
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        Guid dispositionId;
        PrivacyErasureIntent fact;
        long revision;
        await using (var context = fixture.Database.Context())
        {
            var repository = fixture.Repository(context);
            await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
            fixture.Keys.Available = false;
            await repository.ComputeAsync(fixture.Request, CancellationToken.None);
            fixture.Clock.UtcNow = Now.AddMinutes(5);
            var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
            revision = pending.Revision;
            var adoption = await context.Set<ModerationRecognitionPrivacyAdoption>()
                .IgnoreAllFilters("Exact accepted-owner crash fixture").AsNoTracking()
                .SingleAsync(row => row.Id == fixture.AdoptionId);
            var assessment = await repository.AssessAsync(new(Guid.CreateVersion7(), pending.IntentId, pending.Revision,
                adoption.SourceId, adoption.SourceRevision, adoption.SourceWriterGeneration,
                adoption.Id, adoption.Revision, adoption.WriterGeneration,
                ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable, fixture.Clock.UtcNow.AddMinutes(5)),
                pending.RecoveryOwnerHumanId, CancellationToken.None);
            dispositionId = assessment.Id;
            await repository.AuthorizeAsync(assessment.Id, 1, pending.PrivacyOwnerHumanId, CancellationToken.None);
            fact = await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await Assert.That(await repository.HasLawfulDispositionAsync(fixture.Request, ct)).IsTrue();
                var committed = await fixture.Retained.Repository.AppendAsync(fixture.Request, ct);
                await repository.BindLawfulDispositionAsync(committed, ct);
                // The retained authority commit is real, while all native consumption changes vanish.
                await transaction.RollbackAsync(ct);
                return committed;
            }, CancellationToken.None);
        }
        fixture.Clock.UtcNow = Now.AddHours(2);
        await fixture.WithdrawPrivacySourceAsync();
        await using (var loss = fixture.Database.Context())
        {
            await loss.UserExternalLogins.Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync();
            await loss.Set<UserPii>().Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync();
            if (loseOwnerAcceptance)
                await loss.Set<ModerationRecognitionDispositionAcceptance>()
                    .IgnoreAllFilters("Lost independently accepted owner receipt")
                    .Where(row => row.DispositionId == dispositionId
                        && row.Role == ModerationRecognitionDispositionAcceptanceRole.PrivacyOwner).ExecuteDeleteAsync();
        }
        fixture.Clock.UtcNow = Now.AddDays(9);
        await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var restored = fixture.Database.Context();
            await using var transaction = await restored.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var repository = fixture.Repository(restored);
            await Assert.That(await repository.HasConsumedLawfulDispositionAsync(fixture.Request, ct)).IsFalse();
            await Assert.That(await repository.HasRecoverableLawfulDispositionAsync(fixture.Request, ct))
                .IsEqualTo(!loseOwnerAcceptance);
            if (loseOwnerAcceptance)
                await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BindLawfulDispositionAsync(fact, ct));
            else
            {
                await repository.BindLawfulDispositionAsync(fact, ct);
                await Assert.That(await repository.VerifyLawfulDispositionAsync(fact, ct)).IsTrue();
            }
            var pending = (await repository.FindAsync(fixture.Request.IntentId, ct))!;
            await Assert.That(pending.Revision).IsEqualTo(revision);
            await Assert.That(pending.HasCompleteMaterial).IsFalse();
            await Assert.That(pending.AuthoritySequence).IsNull();
            if (!loseOwnerAcceptance) await transaction.CommitAsync(ct);
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task RecognitionDisposition_OneSourcesOwnersCannotAuthorizeAnotherSourcesContribution()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await AddIndependentDispositionContributionAsync(fixture);
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        fixture.Keys.Available = false;
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(5);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        var sources = await context.Set<ModerationRecognitionPrivacySource>()
            .IgnoreAllFilters("Exact independent-owner fixture").AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
        await Assert.That(sources.Length).IsEqualTo(2);
        await Assert.That(sources[0].PrivacyOwnerHumanId).IsNotEqualTo(sources[1].PrivacyOwnerHumanId);
        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            var adoption = await context.Set<ModerationRecognitionPrivacyAdoption>()
                .IgnoreAllFilters("Exact independent-owner fixture").AsNoTracking()
                .SingleAsync(row => row.SourceId == source.Id);
            var assessment = await repository.AssessAsync(new(Guid.CreateVersion7(), pending.IntentId, pending.Revision,
                source.Id, source.Revision, source.WriterGeneration, adoption.Id, adoption.Revision, adoption.WriterGeneration,
                ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable, fixture.Clock.UtcNow.AddMinutes(5)),
                source.RecoveryOwnerHumanId, CancellationToken.None);
            await repository.AuthorizeAsync(assessment.Id, 1, source.PrivacyOwnerHumanId, CancellationToken.None);
            var complete = await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                return await repository.HasLawfulDispositionAsync(fixture.Request, ct);
            }, CancellationToken.None);
            await Assert.That(complete).IsEqualTo(index == 1);
        }
        await Assert.That((await repository.ReadRemediesAsync(fixture.ReceiptHash, CancellationToken.None)).Count).IsEqualTo(2);
        await Assert.That(await fixture.Retained.Repository.ReadAfterAsync(0, 100, CancellationToken.None)).IsEmpty();
    }

    [Test]
    public async Task RecognitionDisposition_RequiresTwoCurrentOwners_AndReplaysOnlyExactRetainedFact()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        fixture.Keys.Available = false;
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(5);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        var adoption = await context.Set<ModerationRecognitionPrivacyAdoption>()
            .IgnoreAllFilters("Exact native disposition fixture").SingleAsync(row => row.Id == fixture.AdoptionId);
        var source = await context.Set<ModerationRecognitionPrivacySource>()
            .IgnoreAllFilters("Exact native disposition fixture").Include(row => row.Retention)
            .SingleAsync(row => row.Id == adoption.SourceId);
        context.ChangeTracker.Clear();
        var assessment = new ModerationRecognitionRecoveryAssessment(Guid.CreateVersion7(), pending.IntentId,
            pending.Revision, source.Id, source.Revision, source.WriterGeneration, adoption.Id, adoption.Revision,
            adoption.WriterGeneration, ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable,
            fixture.Clock.UtcNow.AddMinutes(5));
        var row = await repository.AssessAsync(assessment, pending.RecoveryOwnerHumanId, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AuthorizeAsync(row.Id, 1, pending.RecoveryOwnerHumanId, CancellationToken.None));
        await repository.AuthorizeAsync(row.Id, 1, pending.PrivacyOwnerHumanId, CancellationToken.None);
        var retained = await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await Assert.That(await repository.HasLawfulDispositionAsync(fixture.Request, ct)).IsTrue();
            var fact = await fixture.Retained.Repository.AppendAsync(fixture.Request, ct);
            await repository.BindLawfulDispositionAsync(fact, ct);
            await transaction.CommitAsync(ct);
            return fact;
        }, CancellationToken.None);
        var proof = (await repository.ReadRemediesAsync(fixture.ReceiptHash, CancellationToken.None)).Single();
        await Assert.That(proof.IntentId).IsEqualTo(pending.IntentId);
        await Assert.That(proof.PendingRevision).IsEqualTo(pending.Revision);
        // A fresh native context must prove actual consumption, not a primary marker or key.
        fixture.Clock.UtcNow = Now.AddDays(9);
        await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var restored = fixture.Database.Context();
            await using var transaction = await restored.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var recovery = fixture.Repository(restored);
            await Assert.That(await recovery.VerifyLawfulDispositionAsync(retained, ct)).IsTrue();
            var received = (await recovery.FindAsync(pending.IntentId, ct))!;
            await Assert.That(received.HasCompleteMaterial).IsFalse();
            await Assert.That(received.AuthoritySequence).IsNull();
            // The protected correction route survives ordinary receipt/source expiry.
            // This is separate proof, not renewed matching or identity enrollment authority.
            await Assert.That((await recovery.ReadRemediesAsync(fixture.ReceiptHash, ct)).Single().Id).IsEqualTo(proof.Id);
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task RecognitionDisposition_WithdrawalAfterAssessmentRejectsAuthorizationWithoutErasing()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(lawfulDeadline: TimeSpan.FromHours(1));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        fixture.Keys.Available = false;
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(5);
        var pending = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        var adoption = await context.Set<ModerationRecognitionPrivacyAdoption>()
            .IgnoreAllFilters("Exact native disposition fixture").AsNoTracking()
            .SingleAsync(row => row.Id == fixture.AdoptionId);
        var row = await repository.AssessAsync(new(Guid.CreateVersion7(), pending.IntentId, pending.Revision,
            adoption.SourceId, adoption.SourceRevision, adoption.SourceWriterGeneration, adoption.Id,
            adoption.Revision, adoption.WriterGeneration, ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable,
            fixture.Clock.UtcNow.AddMinutes(5)), pending.RecoveryOwnerHumanId, CancellationToken.None);
        await fixture.WithdrawPrivacySourceAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AuthorizeAsync(row.Id, 1, pending.PrivacyOwnerHumanId, CancellationToken.None));
        await Assert.That((await repository.FindAsync(pending.IntentId, CancellationToken.None))!.AuthoritySequence).IsNull();
        await Assert.That(await fixture.Retained.Repository.ReadAfterAsync(0, 100, CancellationToken.None)).IsEmpty();
    }

    private static async Task AddIndependentDispositionContributionAsync(RecognitionErasureFixture fixture)
    {
        await using var context = fixture.Database.Context();
        var database = fixture.Database;
        var scope = database.Scope;
        var privacy = Guid.CreateVersion7();
        var recovery = Guid.CreateVersion7();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in new[] { privacy, recovery })
        {
            var user = new User { Id = id, Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Independent", LastName = "Owner" } };
            context.Users.Add(user);
            context.PlatformUserRoles.Add(new PlatformUserRole
                { Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var epoch = await context.Set<ModerationQualificationEpoch>().AsNoTracking()
            .SingleAsync(row => row.Id == database.EpochId);
        var request = database.Request(ModerationReferenceProfileId.CFG05);
        request = request with
        {
            ExpectedQualificationRevision = epoch.Revision,
            Proposed = request.Proposed with
            {
                RequestedState = ModerationPolicyRequestedState.Active,
                Profiles = [request.Proposed.Profiles.Single() with
                {
                    Rule = ModerationRuleReference.Create(scope.Id, "independent.disposition", 1),
                    Recognition = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
                }]
            }
        };
        var identity = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, identity.Admin, identity.Machine,
            database.Inventory, null, fixture.Clock);
        var prepared = await publications.PrepareAsync(request, Now.AddDays(10));
        if (prepared.Outcome != ModerationPolicyPublicationOutcome.Known)
            throw new InvalidOperationException("Second native policy must be genuinely prepared.");
        var original = await context.Set<ModerationRecognitionPrivacySource>()
            .IgnoreAllFilters("Exact independent-owner fixture").AsNoTracking().Include(row => row.Retention).SingleAsync();
        ModerationQualificationRepository Writer(Guid human)
        {
            var principal = DeploymentIdentity(context, human);
            return new(context, principal.Admin, principal.Machine, publications,
                new ModerationNativeActivationQualification(context, database.Inventory, null, null, fixture.Clock),
                fixture.Clock, fixture.Retained.Repository, deploymentInventory: database.Inventory);
        }
        epoch = await context.Set<ModerationQualificationEpoch>().AsNoTracking().SingleAsync(row => row.Id == database.EpochId);
        var source = new RecordModerationRecognitionPrivacySource
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = epoch.Revision,
            SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded, ValidUntil = Now.AddDays(8), Terms = original.Terms,
            Ownership = new(privacy, privacy, recovery, recovery, database.AdminId,
                "Distinct independently qualified controllers for the second contribution.",
                "Independent privacy mandate covering only the second policy.",
                "Independent recovery mandate covering only the second policy."),
            Retention = [.. original.Retention.Select(row => row.ToTerms())]
        };
        if ((await Writer(database.AdminId).ExecuteAsync(source)).Outcome != ModerationQualificationWriteOutcome.Known)
            throw new InvalidOperationException("Second native source must have a real qualification receipt.");
        epoch = await context.Set<ModerationQualificationEpoch>().AsNoTracking().SingleAsync(row => row.Id == database.EpochId);
        var persisted = await context.Set<ModerationRecognitionPrivacySource>().IgnoreAllFilters("Exact independent-owner fixture")
            .AsNoTracking().SingleAsync(row => row.Id == source.SourceId);
        if ((await Writer(privacy).ExecuteAsync(new AdoptModerationRecognitionPrivacySource
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = epoch.Revision,
            SourceId = source.SourceId, ExpectedSourceRevision = persisted.Revision,
            ExpectedSourceWriterGeneration = persisted.WriterGeneration, AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddDays(7)
        })).Outcome != ModerationQualificationWriteOutcome.Known)
            throw new InvalidOperationException("Second independent privacy owner must genuinely adopt its source.");
        var instance = scope.Parent!;
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, fixture.Subject, instance);
        var moderator = SubjectReference.Create(ModerationSubjectKind.Human, database.AdminId, instance);
        var moderationCase = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
        var ruleReference = request.Proposed.Profiles.Single().Rule;
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), moderationCase, moderationCase.Revision,
            moderationCase.Scope, null, null,
            ruleReference, ruleReference, ruleReference, ruleReference, moderator,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), moderationCase.Id, moderationCase.Revision, Now),
            "Second independent canonical contribution for complete legal coverage.", Now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), moderationCase.Id, subject, moderator,
            instance, scope, ruleReference, [ModerationCapability.Publish], Now, Now.AddDays(2), null, 1, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(finding.Id, 2, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, privacy, instance), instance, 3, Now);
        var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
        context.ModerationCases.Add(moderationCase);
        context.ModerationFindings.Add(finding);
        context.ModerationDecisions.Add(decision);
        context.ModerationSanctions.Add(sanction);
        await context.SaveChangesAsync();
    }
}
