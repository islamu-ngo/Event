using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    /// <summary>
    /// FIRST UNRUN witnesses: the ACTIVE seed is only a declared read precondition.
    /// Original observations and independently accepted assessments use real native writers.
    /// Consent metadata supplies neither remaining prerequisites nor before-body authority.
    /// </summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileConsent_ActualNonAdminSelfRetainsIndependentAssessmentAndOriginalWindows(bool provider)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, observation, assessment, request) = await ConsentOriginalAsync(fixture, provider);
        var receipts = await fixture.Context.ModerationQualificationMutationReceipts.CountAsync();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var consent = await ConsentOwner(fixture, http).GrantAsync(request);
        await Assert.That(consent).IsNotNull();
        await Assert.That(consent!.SubjectHumanId).IsEqualTo(fixture.SubjectId);
        await Assert.That(consent.Binding).IsEqualTo(observation.ToBinding());
        await Assert.That(consent.Assessment.Reference.Id).IsEqualTo(assessment.Id);
        await Assert.That(consent.Assessment.Reference.Generation).IsEqualTo(assessment.Generation);
        await Assert.That(consent.Assessment.Reference.ObservedAt).IsEqualTo(assessment.AdoptedAt);
        await Assert.That(consent.Assessment.Reference.ValidUntil).IsEqualTo(assessment.ValidUntil);
        await Assert.That(consent.Assessment.Required.SequenceEqual(AllAssessmentRequirements())).IsTrue();
        await Assert.That(consent.Generation).IsEqualTo(1L);
        await Assert.That(consent.GrantedAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(consent.ValidUntil).IsEqualTo(request.ValidUntil);
        await Assert.That(await fixture.Context.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
        await Assert.That(await fixture.Context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(receipts);
        await Assert.That(await fixture.Context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(8L);
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync()).IsEqualTo(1);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        fixture.Clock.UtcNow = Now.AddMinutes(2);
        var withdrawn = await ConsentOwner(fixture, http).WithdrawAsync(ConsentWithdrawal(consent));
        await Assert.That(withdrawn).IsNotNull();
        await Assert.That(withdrawn!.Revision).IsEqualTo(2L);
        await Assert.That(withdrawn.WithdrawnAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(withdrawn.Generation).IsEqualTo(consent.Generation);
        await Assert.That(withdrawn.GrantedAt).IsEqualTo(consent.GrantedAt);
        await Assert.That(withdrawn.ValidUntil).IsEqualTo(consent.ValidUntil);
        await Assert.That(withdrawn.Assessment.Reference).IsEqualTo(consent.Assessment.Reference);
    }

    [Test]
    [Arguments("empty")]
    [Arguments("account-only")]
    [Arguments("guardian-only")]
    [Arguments("unaccepted")]
    [Arguments("withdrawn-assessment")]
    [Arguments("other")]
    [Arguments("administrator")]
    [Arguments("owner")]
    [Arguments("reviewer")]
    [Arguments("observation")]
    [Arguments("observation-revision")]
    [Arguments("observation-generation")]
    [Arguments("policy")]
    [Arguments("publication")]
    [Arguments("profile")]
    [Arguments("mode")]
    [Arguments("procedure")]
    [Arguments("procedure-revision")]
    [Arguments("procedure-generation")]
    [Arguments("procedure-adoption")]
    [Arguments("procedure-adoption-revision")]
    [Arguments("procedure-adoption-generation")]
    [Arguments("assessment-source")]
    [Arguments("assessment-source-revision")]
    [Arguments("assessment-source-generation")]
    [Arguments("assessment-adoption")]
    [Arguments("assessment-adoption-revision")]
    [Arguments("assessment-adoption-generation")]
    [Arguments("slot-cas")]
    [Arguments("infinite")]
    [Arguments("past")]
    [Arguments("beyond-assessment")]
    public async Task ProfileConsent_RejectsGratuitousConsentDelegationAndCoordinateSubstitution(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var required = change switch
        {
            "empty" => Array.Empty<ModerationCollectionPrerequisiteKind>(),
            "account-only" => [ModerationCollectionPrerequisiteKind.AccountEligibility],
            "guardian-only" => [ModerationCollectionPrerequisiteKind.GuardianConsent],
            _ => AllAssessmentRequirements()
        };
        var (http, observation, assessment, request) = await ConsentOriginalAsync(fixture, false, required);
        var expected = request.Assessment;
        var original = expected.Original;
        var actor = change switch
        {
            "other" => fixture.OtherSubjectId, "administrator" => fixture.Database.AdminId,
            "owner" => fixture.OwnerId, "reviewer" => fixture.ReviewerId, _ => fixture.SubjectId
        };
        http.HttpContext!.User = IndividualPrincipal(actor);
        if (change == "unaccepted")
            await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().ExecuteDeleteAsync();
        if (change == "withdrawn-assessment")
            await Assert.That(await ApplicabilityOwner(fixture,
                await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false)).WithdrawAsync(new(
                    observation.ToBinding().Jurisdiction, fixture.SubjectId, observation.PolicyId,
                    observation.PolicyRevision, observation.PublicationId, observation.ProfileId,
                    (ModerationNativeMode)observation.ModeCode, assessment.SourceId,
                    assessment.SourceGeneration, assessment.Id, assessment.Generation, 1))).IsNotNull();
        expected = change switch
        {
            "observation" => expected with { Original = original with { ObservationId = Guid.CreateVersion7() } },
            "observation-revision" => expected with { Original = original with { ExpectedObservationRevision = 2 } },
            "observation-generation" => expected with { Original = original with { ExpectedObservationGeneration = 2 } },
            "policy" => expected with { Original = original with { PolicyId = Guid.CreateVersion7() } },
            "publication" => expected with { Original = original with { PublicationId = Guid.CreateVersion7() } },
            "profile" => expected with { Original = original with { ProfileId = Guid.CreateVersion7() } },
            "mode" => expected with { Original = original with { Mode = (ModerationNativeMode)999 } },
            "procedure" => expected with { Original = original with { ProcedureSourceId = fixture.Record.SourceId } },
            "procedure-revision" => expected with { Original = original with { ExpectedProcedureSourceRevision = 2 } },
            "procedure-generation" => expected with { Original = original with { ExpectedProcedureSourceWriterGeneration = 1 } },
            "procedure-adoption" => expected with { Original = original with { ProcedureAdoptionId = fixture.Adopt.AdoptionId } },
            "procedure-adoption-revision" => expected with { Original = original with { ExpectedProcedureAdoptionRevision = 2 } },
            "procedure-adoption-generation" => expected with { Original = original with { ExpectedProcedureAdoptionWriterGeneration = 1 } },
            "assessment-source" => expected with { SourceId = Guid.CreateVersion7() },
            "assessment-source-revision" => expected with { ExpectedSourceRevision = 2 },
            "assessment-source-generation" => expected with { ExpectedSourceGeneration = 2 },
            "assessment-adoption" => expected with { AdoptionId = Guid.CreateVersion7() },
            "assessment-adoption-revision" => expected with { ExpectedAdoptionRevision = 2 },
            "assessment-adoption-generation" => expected with { ExpectedAdoptionGeneration = 1 },
            _ => expected
        };
        request = request with { Assessment = expected };
        request = change switch
        {
            "slot-cas" => request with { ExpectedGeneration = 1 },
            "infinite" => request with { ValidUntil = DateTimeOffset.MaxValue },
            "past" => request with { ValidUntil = Now },
            "beyond-assessment" => request with { ValidUntil = assessment.ValidUntil.AddTicks(1) },
            _ => request
        };
        await Assert.That(await ConsentOwner(fixture, http).GrantAsync(request)).IsNull();
        await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileConsent_NewIdsCannotRenewEvenTerminalOriginalAndSlotDoesNotReset(bool terminal)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, request) = await ConsentOriginalAsync(fixture, true);
        var owner = ConsentOwner(fixture, http);
        var consent = (await owner.GrantAsync(request))!;
        if (terminal) await Assert.That(await owner.WithdrawAsync(ConsentWithdrawal(consent))).IsNotNull();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        await Assert.That(await owner.GrantAsync(request)).IsNull();
        await Assert.That(await owner.GrantAsync(request with { ExpectedGeneration = 1 })).IsNull();
        await Assert.That(await owner.GrantAsync(request with
        { ConsentId = Guid.CreateVersion7(), ExpectedGeneration = 1, ValidUntil = Now.AddMinutes(3) })).IsNull();
        var nextObservation = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        var source = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(AssessmentRecord(nextObservation, AllAssessmentRequirements()) with { ExpectedGeneration = 2 }))!;
        var accepted = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false))
            .AdoptAsync(AssessmentAdopt(nextObservation, source)))!;
        var nextRequest = ConsentGrant(nextObservation, accepted) with { ExpectedGeneration = 1 };
        await Assert.That(await owner.GrantAsync(nextRequest with { ExpectedGeneration = 0 })).IsNull();
        var next = await owner.GrantAsync(nextRequest);
        await Assert.That(next).IsNotNull();
        await Assert.That(next!.Generation).IsEqualTo(2L);
        await Assert.That(next.Binding.SourceId).IsEqualTo(nextObservation.Id);
        var first = await fixture.Context.Set<ModerationProfileSubjectConsent>().AsNoTracking().SingleAsync(row => row.Id == consent.Id);
        await Assert.That(first.ValidUntil).IsEqualTo(consent.ValidUntil);
        await Assert.That(first.GrantedAt).IsEqualTo(consent.GrantedAt);
    }

    [Test]
    [Arguments("source")]
    [Arguments("expired-source")]
    [Arguments("expired-adoption")]
    [Arguments("superseded-observation")]
    public async Task ProfileConsent_LatestNativeAssessmentAndObservationNeverFallBack(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, assessment, request) = await ConsentOriginalAsync(fixture, false);
        if (change is "source" or "expired-source")
            await fixture.Context.Set<ModerationProfileCollectionAssessmentSource>().Where(row => row.Id == assessment.SourceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Generation, 3L));
        if (change == "expired-source")
            fixture.Clock.UtcNow = assessment.Source.ValidUntil;
        if (change == "expired-adoption")
            fixture.Clock.UtcNow = assessment.ValidUntil;
        if (change == "superseded-observation")
            await Assert.That(await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture))).IsNotNull();
        await Assert.That(await ConsentOwner(fixture, http).GrantAsync(request)).IsNull();
        await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileConsent_UnacceptedOrTiedLatestOriginalCannotUseEarlierAcceptedReference(bool tie)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, prior, priorRequest) = await ConsentOriginalAsync(fixture, false);
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var nextObservation = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        var nextSource = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(AssessmentRecord(nextObservation, AllAssessmentRequirements()) with { ExpectedGeneration = 2 }))!;
        var nextRequest = priorRequest with
        {
            Assessment = priorRequest.Assessment with
            {
                Original = AssessmentOriginal(nextObservation), SourceId = nextSource.Id,
                ExpectedSourceRevision = nextSource.Revision, ExpectedSourceGeneration = nextSource.Generation
            }
        };
        if (tie)
        {
            var accepted = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false))
                .AdoptAsync(AssessmentAdopt(nextObservation, nextSource)))!;
            // Fault the two real native acceptances into a tied slot sequence; no fabricated
            // assessment or accepted reference is inserted by the witness.
            await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().Where(row => row.Id == prior.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Generation, accepted.Generation));
            nextRequest = ConsentGrant(nextObservation, accepted);
        }
        await Assert.That(await ConsentOwner(fixture, http).GrantAsync(priorRequest)).IsNull();
        await Assert.That(await ConsentOwner(fixture, http).GrantAsync(nextRequest)).IsNull();
        await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ProfileConsent_LatestTerminalConsentDoesNotRestoreEarlierOriginalAndHistoricalCorrectionUsesCurrentCas()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, request) = await ConsentOriginalAsync(fixture, false);
        var owner = ConsentOwner(fixture, http);
        var first = (await owner.GrantAsync(request))!;
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var observation = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        var source = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(AssessmentRecord(observation, AllAssessmentRequirements()) with { ExpectedGeneration = 2 }))!;
        var assessment = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false))
            .AdoptAsync(AssessmentAdopt(observation, source)))!;
        var latestRequest = ConsentGrant(observation, assessment) with { ExpectedGeneration = 1 };
        var latest = (await owner.GrantAsync(latestRequest))!;
        await Assert.That(await owner.WithdrawAsync(ConsentWithdrawal(latest))).IsNotNull();
        await Assert.That(await owner.GrantAsync(latestRequest with
        { ConsentId = Guid.CreateVersion7(), ExpectedGeneration = 2 })).IsNull();
        await Assert.That(await owner.GrantAsync(request with
        { ConsentId = Guid.CreateVersion7(), ExpectedGeneration = 2 })).IsNull();
        await Assert.That(await owner.WithdrawAsync(ConsentWithdrawal(first))).IsNull();
        var historical = await owner.WithdrawAsync(ConsentWithdrawal(first) with { ExpectedGeneration = 2 });
        await Assert.That(historical).IsNotNull();
        await Assert.That(historical!.Revision).IsEqualTo(2L);
        await Assert.That(historical.Generation).IsEqualTo(1L);
        await Assert.That(historical.ValidUntil).IsEqualTo(first.ValidUntil);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileConsent_FinalSamePrincipalIdentityMutationCannotCommit(bool provider)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, request) = await ConsentOriginalAsync(fixture, provider);
        var boundary = new AssessmentCommitBoundary(fixture.Privacy, _ =>
        {
            var identity = (System.Security.Claims.ClaimsIdentity)http.HttpContext!.User.Identity!;
            var claim = identity.FindFirst("sub")!;
            identity.RemoveClaim(claim);
            identity.AddClaim(new("sub", provider ? "unlinked-subject" : fixture.OtherSubjectId.ToString()));
            return Task.CompletedTask;
        });
        await Assert.That(await ConsentOwner(fixture, http, boundary).GrantAsync(request)).IsNull();
        await Assert.That(boundary.Reached).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>().CountAsync()).IsEqualTo(0);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileConsent_HistoricalWithdrawalNeedsNoLiveAssessmentPolicyOrParent(bool provider)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, request) = await ConsentOriginalAsync(fixture, provider);
        var owner = ConsentOwner(fixture, http);
        var consent = (await owner.GrantAsync(request))!;
        await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(new WithdrawModerationChildAgePolicyAdoption
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Database.Scope, ExpectedQualificationRevision = 8,
            AdoptionId = fixture.ParentAdoption.AdoptionId, ExpectedAdoptionRevision = 1
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().ExecuteDeleteAsync();
        await fixture.Context.Set<ModerationProfileCollectionAssessmentSource>().ExecuteDeleteAsync();
        await fixture.Context.Set<ModerationProfileCollectionSubjectObservation>().ExecuteDeleteAsync();
        await fixture.Context.ModerationPolicySelections.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null));
        await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>().ExecuteDeleteAsync();
        fixture.Clock.UtcNow = Now.AddMinutes(20);
        var withdrawn = await owner.WithdrawAsync(ConsentWithdrawal(consent));
        await Assert.That(withdrawn).IsNotNull();
        await Assert.That(withdrawn!.Revision).IsEqualTo(2L);
        await Assert.That(withdrawn.ValidUntil).IsEqualTo(consent.ValidUntil);
        await Assert.That(await owner.WithdrawAsync(ConsentWithdrawal(consent))).IsNull();
        await Assert.That(await owner.GrantAsync(request with
        { ConsentId = Guid.CreateVersion7(), ExpectedGeneration = 1, ValidUntil = Now.AddMinutes(21) })).IsNull();
    }

    [Test]
    [Arguments("other")]
    [Arguments("administrator")]
    [Arguments("owner")]
    [Arguments("reviewer")]
    [Arguments("id")]
    [Arguments("revision")]
    [Arguments("consent-generation")]
    [Arguments("slot-cas")]
    public async Task ProfileConsent_WithdrawalRequiresExactOriginalSelfRevisionAndSlot(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, request) = await ConsentOriginalAsync(fixture, false);
        var consent = (await ConsentOwner(fixture, http).GrantAsync(request))!;
        var actor = change switch
        {
            "other" => fixture.OtherSubjectId, "administrator" => fixture.Database.AdminId,
            "owner" => fixture.OwnerId, "reviewer" => fixture.ReviewerId, _ => fixture.SubjectId
        };
        http.HttpContext!.User = IndividualPrincipal(actor);
        var withdraw = ConsentWithdrawal(consent);
        withdraw = change switch
        {
            "id" => withdraw with { ConsentId = Guid.CreateVersion7() },
            "revision" => withdraw with { ExpectedConsentRevision = 2 },
            "consent-generation" => withdraw with { ExpectedConsentGeneration = 2 },
            "slot-cas" => withdraw with { ExpectedGeneration = 0 },
            _ => withdraw
        };
        await Assert.That(await ConsentOwner(fixture, http).WithdrawAsync(withdraw)).IsNull();
        var retained = await fixture.Context.Set<ModerationProfileSubjectConsent>().AsNoTracking().SingleAsync();
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(retained.WithdrawnAt).IsNull();
    }

    [Test]
    [Arguments("principal")]
    [Arguments("login")]
    [Arguments("pii")]
    [Arguments("privacy")]
    [Arguments("observation")]
    [Arguments("assessment-source")]
    [Arguments("assessment-adoption")]
    [Arguments("parent")]
    [Arguments("procedure")]
    [Arguments("policy")]
    [Arguments("epoch")]
    [Arguments("tenant")]
    [Arguments("trust")]
    [Arguments("host")]
    [Arguments("slot")]
    [Arguments("consent")]
    [Arguments("expiry")]
    [Arguments("cancel")]
    [Arguments("rollback")]
    public async Task ProfileConsent_GrantFinalRereadAtTrueOuterCommitRollsBackWholeWrite(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, observation, assessment, request) = await ConsentOriginalAsync(fixture, true);
        var context = fixture.Context;
        using var cancellation = new CancellationTokenSource();
        var originalTrust = context.ModerationTrustDomainId;
        var boundary = new AssessmentCommitBoundary(fixture.Privacy, async token =>
        {
            await Assert.That(context.IdentityFenceOwnsTransaction).IsTrue();
            await Assert.That(await context.Set<ModerationProfileSubjectConsent>().AnyAsync(row => row.Id == request.ConsentId, token)).IsTrue();
            await ConsentIdentityMutationAsync(fixture, http, change, token);
            switch (change)
            {
                case "observation":
                    await context.Set<ModerationProfileCollectionSubjectObservation>().Where(row => row.Id == observation.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ObservationStamp, Guid.CreateVersion7()), token); break;
                case "assessment-source":
                    await context.Set<ModerationProfileCollectionAssessmentSource>().Where(row => row.Id == assessment.SourceId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(2)), token); break;
                case "assessment-adoption":
                    await context.Set<ModerationProfileCollectionAssessmentAdoption>().Where(row => row.Id == assessment.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(2)), token); break;
                case "parent":
                    await context.Set<ModerationChildAgePolicyAdoption>().Where(row => row.Id == fixture.ParentAdoption.AdoptionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(1)), token); break;
                case "procedure":
                    await context.Set<ModerationProfileCollectionProcedureAdoption>().Where(row => row.Id == observation.ProcedureAdoptionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(1)), token); break;
                case "policy": await context.ModerationPolicySelections.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null), token); break;
                case "slot": await context.Set<ModerationProfileSubjectConsent>().ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Generation, 2L), token); break;
                case "consent": await context.Set<ModerationProfileSubjectConsent>().ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(1)), token); break;
                case "expiry": fixture.Clock.UtcNow = request.ValidUntil; break;
                case "cancel": cancellation.Cancel(); break;
                case "rollback": throw new AssessmentOuterRollback();
            }
        });
        async Task Write() => await Assert.That(await ConsentOwner(fixture, http, boundary)
            .GrantAsync(request, cancellation.Token)).IsNull();
        if (change == "cancel") await Assert.That(Write).Throws<OperationCanceledException>();
        else if (change == "rollback") await Assert.That(Write).Throws<AssessmentOuterRollback>();
        else await Write();
        await Assert.That(boundary.Reached).IsTrue();
        await Assert.That(context.ModerationTrustDomainId).IsEqualTo(originalTrust);
        context.TenantContext = fixture.Database;
        context.ChangeTracker.Clear();
        await Assert.That(await context.Set<ModerationProfileSubjectConsent>().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.Id == fixture.LoginId && row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(await context.Set<ModerationProfileCollectionAssessmentAdoption>()
            .Where(row => row.Id == assessment.Id).Select(row => row.ValidUntil).SingleAsync()).IsEqualTo(assessment.ValidUntil);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("principal")]
    [Arguments("login")]
    [Arguments("pii")]
    [Arguments("privacy")]
    [Arguments("epoch")]
    [Arguments("tenant")]
    [Arguments("trust")]
    [Arguments("host")]
    [Arguments("slot")]
    [Arguments("cancel")]
    [Arguments("rollback")]
    public async Task ProfileConsent_WithdrawalFinalIdentityAndSlotLossRollsBackTerminalRevision(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, request) = await ConsentOriginalAsync(fixture, true);
        var consent = (await ConsentOwner(fixture, http).GrantAsync(request))!;
        fixture.Clock.UtcNow = Now.AddMinutes(20);
        using var cancellation = new CancellationTokenSource();
        var boundary = new AssessmentCommitBoundary(fixture.Privacy, async token =>
        {
            await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsTrue();
            await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>()
                .Where(row => row.Id == consent.Id).Select(row => row.Revision).SingleAsync(token)).IsEqualTo(2L);
            await ConsentIdentityMutationAsync(fixture, http, change, token);
            if (change == "slot") await fixture.Context.Set<ModerationProfileSubjectConsent>()
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Generation, 2L), token);
            if (change == "cancel") cancellation.Cancel();
            if (change == "rollback") throw new AssessmentOuterRollback();
        });
        async Task Write() => await Assert.That(await ConsentOwner(fixture, http, boundary)
            .WithdrawAsync(ConsentWithdrawal(consent), cancellation.Token)).IsNull();
        if (change == "cancel") await Assert.That(Write).Throws<OperationCanceledException>();
        else if (change == "rollback") await Assert.That(Write).Throws<AssessmentOuterRollback>();
        else await Write();
        await Assert.That(boundary.Reached).IsTrue();
        fixture.Context.TenantContext = fixture.Database;
        fixture.Context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        fixture.Context.ChangeTracker.Clear();
        var retained = await fixture.Context.Set<ModerationProfileSubjectConsent>().AsNoTracking().SingleAsync();
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(retained.WithdrawnAt).IsNull();
        await Assert.That(retained.Generation).IsEqualTo(consent.Generation);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task ProfileConsent_NestedIdentityOwnerCannotDiscardOuterCommitValidation()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, request) = await ConsentOriginalAsync(fixture, false);
        var owner = ConsentOwner(fixture, http);
        var consent = (await owner.GrantAsync(request))!;
        await fixture.Privacy.ExecuteEnrollmentSerializedAsync(async token =>
        {
            await Assert.That(async () => await owner.GrantAsync(request, token)).Throws<InvalidOperationException>();
            await Assert.That(async () => await owner.WithdrawAsync(ConsentWithdrawal(consent), token)).Throws<InvalidOperationException>();
            return true;
        }, _ => Task.CompletedTask, default);
        await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>()
            .Select(row => row.Revision).SingleAsync()).IsEqualTo(1L);
    }

    private static ModerationProfileSubjectConsentOwner ConsentOwner(
        IndividualPolicyFixture fixture, HttpContextAccessor http,
        Explore.Application.Contracts.PrivacyErasure.IPrivacyIdentityFenceAuthority? privacy = null) =>
        new(fixture.Context, ObservationAdmin(fixture, http),
            new Explore.Infrastructure.Identity.MachinePrincipalAccessor(http), http,
            privacy ?? fixture.Privacy, fixture.Database.Inventory, fixture.Clock);

    private static GrantModerationProfileSubjectConsent ConsentGrant(
        ModerationProfileCollectionSubjectObservation observation, ModerationProfileCollectionAssessmentAdoption assessment) =>
        new(new(AssessmentOriginal(observation), assessment.SourceId, assessment.SourceRevision,
            assessment.SourceGeneration, assessment.Id, assessment.Revision, assessment.Generation),
            Guid.CreateVersion7(), 0, Now.AddMinutes(2));

    private static WithdrawModerationProfileSubjectConsent ConsentWithdrawal(ModerationProfileSubjectConsent consent) =>
        new(consent.Id, 1, consent.Generation, consent.Generation);

    private static async Task<(HttpContextAccessor Http, ModerationProfileCollectionSubjectObservation Observation,
        ModerationProfileCollectionAssessmentAdoption Assessment, GrantModerationProfileSubjectConsent Request)>
        ConsentOriginalAsync(IndividualPolicyFixture fixture, bool provider,
            IReadOnlyList<ModerationCollectionPrerequisiteKind>? required = null)
    {
        await AssessmentVocabularyAsync(fixture);
        var http = ObservationPrincipal(fixture, provider);
        var observation = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        var source = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(AssessmentRecord(observation, required ?? AllAssessmentRequirements())))!;
        var adoption = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false))
            .AdoptAsync(AssessmentAdopt(observation, source)))!;
        return (http, observation, adoption, ConsentGrant(observation, adoption));
    }

    private static async Task ConsentIdentityMutationAsync(
        IndividualPolicyFixture fixture, HttpContextAccessor http, string change, CancellationToken token)
    {
        var context = fixture.Context;
        switch (change)
        {
            case "principal": http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId); break;
            case "login": await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UserId, fixture.OtherSubjectId), token); break;
            case "pii": await context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync(token); break;
            case "privacy":
                await using (var authority = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions))
                {
                    await authority.Database.UseTransactionAsync(context.Database.CurrentTransaction!.GetDbTransaction(), token);
                    authority.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                        PrivacyErasureSubjectKind.User, fixture.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest,
                        1, Now.UtcDateTime, Now.UtcDateTime));
                    await authority.SaveChangesAsync(token);
                }
                break;
            case "epoch": await context.ModerationQualificationEpochs.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), token); break;
            case "tenant": context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7()); break;
            case "trust": context.ModerationTrustDomainId = Guid.CreateVersion7(); break;
            case "host": await context.ModerationHostBindings.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.InstanceId, Guid.CreateVersion7()), token); break;
        }
    }
}
