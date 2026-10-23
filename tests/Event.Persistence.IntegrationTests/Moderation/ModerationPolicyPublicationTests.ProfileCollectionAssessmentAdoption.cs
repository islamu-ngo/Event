using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    /// <summary>
    /// ACTIVE fixture state is a declared read precondition, not activation or conformance
    /// evidence. Observations, parent/procedure writers and individual writers are real.
    /// No applicability metadata here is a consent, guardian or collection receipt.
    /// </summary>
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ProfileAssessment_NativeIndependentSessionsRetainOriginalNonAdminSubject(
        bool providerSubject, bool complete)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var subjectHttp = ObservationPrincipal(fixture, providerSubject);
        var observation = (await ObservationOwner(fixture, subjectHttp).ObserveAsync(ObservationIntent(fixture)))!;
        var required = complete ? AllAssessmentRequirements() : [];
        var request = AssessmentRecord(observation, required);
        var ownerHttp = await ApplicabilityActorAsync(fixture, fixture.OwnerId, true);
        var receipts = await fixture.Context.ModerationQualificationMutationReceipts.CountAsync();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var source = await ApplicabilityOwner(fixture, ownerHttp).RecordAsync(request);
        await Assert.That(source).IsNotNull();
        await Assert.That(source!.SubjectHumanId).IsEqualTo(fixture.SubjectId);
        await Assert.That(source.RecordedByHumanId).IsEqualTo(fixture.OwnerId);
        await Assert.That(source.Binding).IsEqualTo(observation.ToBinding());
        await Assert.That(source.Generation).IsEqualTo(1L);
        await Assert.That(source.Required.SequenceEqual(required.Order())).IsTrue();
        await Assert.That(source.ValidUntil).IsEqualTo(Now.AddMinutes(4));
        await Assert.That(await fixture.Context.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
        var reviewerHttp = await ApplicabilityActorAsync(fixture, fixture.ReviewerId, true);
        fixture.Clock.UtcNow = Now.AddMinutes(2);
        var adoption = await ApplicabilityOwner(fixture, reviewerHttp).AdoptAsync(AssessmentAdopt(observation, source));
        await Assert.That(adoption).IsNotNull();
        await Assert.That(adoption!.AdoptedByHumanId).IsEqualTo(fixture.ReviewerId);
        await Assert.That(adoption.Generation).IsEqualTo(2L);
        await Assert.That(adoption.Source.Matches(source)).IsTrue();
        await Assert.That(adoption.Source.Binding.ObservedAt).IsEqualTo(Now);
        await Assert.That(adoption.Source.Binding.ValidUntil).IsEqualTo(fixture.Adopt.ValidUntil);
        await Assert.That(adoption.ValidUntil).IsEqualTo(Now.AddMinutes(3));
        await Assert.That(await fixture.Context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync())
            .IsEqualTo(8L);
        await Assert.That(await fixture.Context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(receipts);
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionProcedureSource>().CountAsync()).IsEqualTo(2);
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionSubjectObservation>().CountAsync()).IsEqualTo(1);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("subject")]
    [Arguments("reviewer")]
    [Arguments("administrator")]
    [Arguments("other")]
    [Arguments("observation")]
    [Arguments("observation-revision")]
    [Arguments("observation-generation")]
    [Arguments("policy")]
    [Arguments("procedure")]
    [Arguments("procedure-adoption")]
    [Arguments("slot-cas")]
    [Arguments("null-required")]
    [Arguments("duplicate-required")]
    [Arguments("unknown-required")]
    [Arguments("too-many-required")]
    public async Task ProfileAssessment_RecordRejectsActorCoordinateAndCompleteSetSubstitution(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var actor = change switch
        {
            "subject" => fixture.SubjectId, "reviewer" => fixture.ReviewerId,
            "administrator" => fixture.Database.AdminId, "other" => fixture.OtherSubjectId,
            _ => fixture.OwnerId
        };
        var request = AssessmentRecord(observation, []);
        var original = request.Original;
        request = change switch
        {
            "observation" => request with { Original = original with { ObservationId = Guid.CreateVersion7() } },
            "observation-revision" => request with { Original = original with { ExpectedObservationRevision = 2 } },
            "observation-generation" => request with { Original = original with { ExpectedObservationGeneration = 2 } },
            "policy" => request with { Original = original with { PolicyId = Guid.CreateVersion7() } },
            "procedure" => request with { Original = original with { ProcedureSourceId = fixture.Record.SourceId } },
            "procedure-adoption" => request with { Original = original with { ProcedureAdoptionId = fixture.Adopt.AdoptionId } },
            "slot-cas" => request with { ExpectedGeneration = 1 },
            "null-required" => request with { Required = null! },
            "duplicate-required" => request with { Required = [ModerationCollectionPrerequisiteKind.SubjectConsent,
                ModerationCollectionPrerequisiteKind.SubjectConsent] },
            "unknown-required" => request with { Required = [(ModerationCollectionPrerequisiteKind)999] },
            "too-many-required" => request with { Required = [.. AllAssessmentRequirements(),
                ModerationCollectionPrerequisiteKind.SubjectConsent] },
            _ => request
        };
        await Assert.That(await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, actor, false))
            .RecordAsync(request)).IsNull();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentSource>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task ProfileAssessment_GuardianRelationshipAndConsentAreSeparateRequiredVocabulary(
        bool relationship, bool consent)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture, relationship, consent);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var request = AssessmentRecord(observation, [ModerationCollectionPrerequisiteKind.GuardianConsent]);
        await Assert.That(await ApplicabilityOwner(fixture,
            await ApplicabilityActorAsync(fixture, fixture.OwnerId, false)).RecordAsync(request)).IsNull();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentSource>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("owner")]
    [Arguments("subject")]
    [Arguments("administrator")]
    [Arguments("other")]
    [Arguments("source")]
    [Arguments("source-revision")]
    [Arguments("source-generation")]
    [Arguments("slot-cas")]
    [Arguments("observation")]
    [Arguments("subject-reviewer")]
    public async Task ProfileAssessment_AdoptionRequiresExactSourceAndIndependentNominatedNonSubject(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var subjectHttp = change == "subject-reviewer"
            ? await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false) : ObservationPrincipal(fixture, false);
        var observation = (await ObservationOwner(fixture, subjectHttp).ObserveAsync(ObservationIntent(fixture)))!;
        var source = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(AssessmentRecord(observation, [])))!;
        var actor = change switch
        {
            "owner" => fixture.OwnerId, "subject" => fixture.SubjectId,
            "administrator" => fixture.Database.AdminId, "other" => fixture.OtherSubjectId,
            _ => fixture.ReviewerId
        };
        var request = AssessmentAdopt(observation, source);
        request = change switch
        {
            "source" => request with { SourceId = Guid.CreateVersion7() },
            "source-revision" => request with { ExpectedSourceRevision = 2 },
            "source-generation" => request with { ExpectedSourceGeneration = 2 },
            "slot-cas" => request with { ExpectedGeneration = 2 },
            "observation" => request with { Original = request.Original with { ObservationId = Guid.CreateVersion7() } },
            _ => request
        };
        await Assert.That(await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, actor, false))
            .AdoptAsync(request)).IsNull();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileAssessment_LatestSourceHasOwnGenerationAndNeverFallsBack(bool expiredLatest)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var owner = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false));
        var firstIntent = AssessmentRecord(observation, []);
        var first = (await owner.RecordAsync(firstIntent))!;
        await Assert.That(await owner.RecordAsync(firstIntent with { SourceId = Guid.CreateVersion7() })).IsNull();
        var secondIntent = firstIntent with { SourceId = Guid.CreateVersion7(), ExpectedGeneration = 1,
            Required = [ModerationCollectionPrerequisiteKind.SubjectConsent], ValidUntil = Now.AddMinutes(2) };
        var second = (await owner.RecordAsync(secondIntent))!;
        await Assert.That(second.Generation).IsEqualTo(2L);
        var reviewer = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false));
        if (expiredLatest) fixture.Clock.UtcNow = second.ValidUntil;
        await Assert.That(await reviewer.AdoptAsync(AssessmentAdopt(observation, first) with { ExpectedGeneration = 2 })).IsNull();
        var adopted = await reviewer.AdoptAsync(AssessmentAdopt(observation, second) with
        { ValidUntil = Now.AddMinutes(expiredLatest ? 3 : 1) });
        await Assert.That(adopted is not null).IsEqualTo(!expiredLatest);
        if (!expiredLatest) await Assert.That(adopted!.Generation).IsEqualTo(3L);
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync())
            .IsEqualTo(expiredLatest ? 0 : 1);
    }

    [Test]
    public async Task ProfileAssessment_RequiredProposalIsCopiedRatherThanLaterCallerMutation()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var required = new[] { ModerationCollectionPrerequisiteKind.SubjectConsent };
        var request = AssessmentRecord(observation, required);
        required[0] = ModerationCollectionPrerequisiteKind.GuardianConsent;
        var source = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(request))!;
        var adoption = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false))
            .AdoptAsync(AssessmentAdopt(observation, source)))!;
        await Assert.That(adoption.Source.Required.SequenceEqual([ModerationCollectionPrerequisiteKind.SubjectConsent])).IsTrue();
    }

    [Test]
    public async Task ProfileAssessment_ReviewerControllerCannotReviewItsOwnNonAdminSubject()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        // The independent reviewer remains a qualified administrator. Its separately nominated
        // canonical controller is this non-admin subject, which must still forbid self-review.
        var age = fixture.Age with
        {
            OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6,
            Ownership = fixture.Age.Ownership with { ReviewerControllerHumanId = fixture.SubjectId }
        };
        await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(age)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var parent = fixture.ParentAdoption with
        {
            OperationId = Guid.CreateVersion7(), SourceId = age.SourceId, ExpectedSourceWriterGeneration = 7,
            ExpectedQualificationRevision = 7, AdoptionId = Guid.CreateVersion7()
        };
        await Assert.That((await fixture.Writer(fixture.ReviewerId).ExecuteAsync(parent)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var procedure = fixture.Record with
        {
            OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 8,
            ChildAgeSourceId = age.SourceId, ExpectedChildAgeSourceWriterGeneration = 7,
            ChildAgeAdoptionId = parent.AdoptionId, ExpectedChildAgeAdoptionWriterGeneration = 8
        };
        await Assert.That((await fixture.Writer(fixture.OwnerId).ExecuteAsync(procedure)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await fixture.Writer(fixture.ReviewerId).ExecuteAsync(fixture.Adopt with
        {
            OperationId = Guid.CreateVersion7(), SourceId = procedure.SourceId, ExpectedSourceWriterGeneration = 9,
            ExpectedQualificationRevision = 9, AdoptionId = Guid.CreateVersion7()
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var source = (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(AssessmentRecord(observation, [])))!;
        await Assert.That(await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false))
            .AdoptAsync(AssessmentAdopt(observation, source))).IsNull();
        await Assert.That(await fixture.Context.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
    }

    [Test]
    [Arguments("infinite")]
    [Arguments("past")]
    [Arguments("beyond-original")]
    public async Task ProfileAssessment_RequestedWindowsCannotExceedOriginalFiniteParents(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var owner = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false));
        var invalidEnd = change switch
        {
            "infinite" => DateTimeOffset.MaxValue,
            "past" => Now,
            _ => observation.ValidUntil.AddTicks(1)
        };
        await Assert.That(await owner.RecordAsync(AssessmentRecord(observation, []) with { ValidUntil = invalidEnd })).IsNull();
        var source = (await owner.RecordAsync(AssessmentRecord(observation, [])))!;
        await Assert.That(await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false))
            .AdoptAsync(AssessmentAdopt(observation, source) with { ValidUntil = invalidEnd })).IsNull();
    }

    [Test]
    public async Task ProfileAssessment_AcceptedSourceCannotReplayOrRenewUnderNewIdentity()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, true))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var ownerHttp = await ApplicabilityActorAsync(fixture, fixture.OwnerId, false);
        var intent = AssessmentRecord(observation, []);
        var source = (await ApplicabilityOwner(fixture, ownerHttp).RecordAsync(intent))!;
        var reviewer = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false));
        var accept = AssessmentAdopt(observation, source);
        var adoption = (await reviewer.AdoptAsync(accept))!;
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        await Assert.That(await reviewer.AdoptAsync(accept)).IsNull();
        await Assert.That(await reviewer.AdoptAsync(accept with { AdoptionId = Guid.CreateVersion7(),
            ExpectedGeneration = 2, ValidUntil = source.ValidUntil })).IsNull();
        await Assert.That(await ApplicabilityOwner(fixture, ownerHttp).RecordAsync(intent with
        { SourceId = Guid.CreateVersion7(), ExpectedGeneration = 2 })).IsNull();
        var retained = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking().SingleAsync();
        await Assert.That(retained.ValidUntil).IsEqualTo(adoption.ValidUntil);
        await Assert.That(retained.AdoptedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task ProfileAssessment_NewObservationSupersedesOldBindingWithoutGlobalGenerationReset()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var http = ObservationPrincipal(fixture, false);
        var first = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        var owner = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false));
        var source = (await owner.RecordAsync(AssessmentRecord(first, [])))!;
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var second = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        var reviewer = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false));
        await Assert.That(await reviewer.AdoptAsync(AssessmentAdopt(first, source))).IsNull();
        await Assert.That(await reviewer.AdoptAsync(AssessmentAdopt(second, source))).IsNull();
        var next = (await owner.RecordAsync(AssessmentRecord(second, []) with { ExpectedGeneration = 1 }))!;
        await Assert.That(next.Generation).IsEqualTo(2L);
        await Assert.That(next.Binding.SourceId).IsEqualTo(second.Id);
        http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId);
        var unrelated = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        var separate = (await owner.RecordAsync(AssessmentRecord(unrelated, [])))!;
        await Assert.That(separate.Generation).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false, "expiry")]
    [Arguments(true, "expiry")]
    [Arguments(false, "principal")]
    [Arguments(true, "principal")]
    [Arguments(false, "actor-login")]
    [Arguments(true, "actor-login")]
    [Arguments(false, "subject-login")]
    [Arguments(true, "subject-login")]
    [Arguments(false, "pii")]
    [Arguments(true, "pii")]
    [Arguments(false, "privacy")]
    [Arguments(true, "privacy")]
    [Arguments(false, "policy")]
    [Arguments(true, "policy")]
    [Arguments(false, "parent")]
    [Arguments(true, "parent")]
    [Arguments(false, "procedure")]
    [Arguments(true, "procedure")]
    [Arguments(false, "administrator")]
    [Arguments(true, "administrator")]
    [Arguments(false, "epoch")]
    [Arguments(true, "epoch")]
    [Arguments(false, "tenant")]
    [Arguments(true, "tenant")]
    [Arguments(false, "trust")]
    [Arguments(true, "trust")]
    [Arguments(false, "host")]
    [Arguments(true, "host")]
    [Arguments(false, "observation")]
    [Arguments(true, "observation")]
    [Arguments(false, "source")]
    [Arguments(true, "source")]
    [Arguments(false, "adoption")]
    [Arguments(true, "adoption")]
    [Arguments(false, "cancel")]
    [Arguments(true, "cancel")]
    [Arguments(false, "rollback")]
    [Arguments(true, "rollback")]
    public async Task ProfileAssessment_TrueOuterCommitRereadsOriginalAndRollsBackEntireWrite(bool adopt, string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var context = fixture.Context;
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, true))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var request = AssessmentRecord(observation, []);
        var source = adopt ? (await ApplicabilityOwner(fixture,
            await ApplicabilityActorAsync(fixture, fixture.OwnerId, false)).RecordAsync(request))! : null;
        var actor = adopt ? fixture.ReviewerId : fixture.OwnerId;
        var http = await ApplicabilityActorAsync(fixture, actor, true);
        var login = await context.UserExternalLogins.Where(row => row.UserId == actor).Select(row => row.Id).SingleAsync();
        using var cancellation = new CancellationTokenSource();
        var boundary = new AssessmentCommitBoundary(fixture.Privacy, async token =>
        {
            await Assert.That(context.IdentityFenceOwnsTransaction).IsTrue();
            await Assert.That(await context.Set<ModerationProfileCollectionAssessmentSource>().CountAsync(token)).IsEqualTo(1);
            await Assert.That(await context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync(token))
                .IsEqualTo(adopt ? 1 : 0);
            switch (change)
            {
                case "expiry": fixture.Clock.UtcNow = adopt ? Now.AddMinutes(3) : request.ValidUntil; break;
                case "principal": http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId); break;
                case "actor-login": await context.UserExternalLogins.Where(row => row.Id == login).ExecuteDeleteAsync(token); break;
                case "subject-login":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId)
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
                case "policy":
                    await context.ModerationPolicySelections.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null), token); break;
                case "parent":
                    await context.Set<ModerationChildAgePolicyAdoption>().Where(row => row.Id == fixture.ParentAdoption.AdoptionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(1)), token); break;
                case "procedure":
                    await context.Set<ModerationProfileCollectionProcedureAdoption>().Where(row => row.Id == observation.ProcedureAdoptionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(1)), token); break;
                case "administrator": await context.PlatformUserRoles.Where(row => row.UserId == actor).ExecuteDeleteAsync(token); break;
                case "epoch":
                    await context.ModerationQualificationEpochs.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), token); break;
                case "tenant": context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7()); break;
                case "trust": context.ModerationTrustDomainId = Guid.CreateVersion7(); break;
                case "host":
                    await context.ModerationHostBindings.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.InstanceId, Guid.CreateVersion7()), token); break;
                case "observation":
                    await context.Set<ModerationProfileCollectionSubjectObservation>().Where(row => row.Id == observation.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ObservationStamp, Guid.CreateVersion7()), token); break;
                case "source":
                    await context.Set<ModerationProfileCollectionAssessmentSource>().Where(row => row.Id == request.SourceId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(2)), token); break;
                case "adoption":
                    if (adopt)
                        await context.Set<ModerationProfileCollectionAssessmentAdoption>().ExecuteUpdateAsync(
                            setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(2)), token);
                    else fixture.Clock.UtcNow = observation.ValidUntil;
                    break;
                case "cancel": cancellation.Cancel(); break;
                case "rollback": throw new AssessmentOuterRollback();
            }
        });
        var owner = ApplicabilityOwner(fixture, http, boundary);
        async Task Write()
        {
            if (adopt) await Assert.That(await owner.AdoptAsync(AssessmentAdopt(observation, source!), cancellation.Token)).IsNull();
            else await Assert.That(await owner.RecordAsync(request, cancellation.Token)).IsNull();
        }
        if (change == "cancel") await Assert.That(Write).Throws<OperationCanceledException>();
        else if (change == "rollback") await Assert.That(Write).Throws<AssessmentOuterRollback>();
        else await Write();
        await Assert.That(boundary.Reached).IsTrue();
        context.TenantContext = fixture.Database;
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        context.ChangeTracker.Clear();
        await Assert.That(await context.Set<ModerationProfileCollectionAssessmentSource>().CountAsync()).IsEqualTo(adopt ? 1 : 0);
        await Assert.That(await context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.Id == fixture.LoginId && row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, "expiry")]
    [Arguments(true, "expiry")]
    [Arguments(false, "pii")]
    [Arguments(true, "pii")]
    [Arguments(false, "privacy")]
    [Arguments(true, "privacy")]
    [Arguments(false, "subject-login")]
    [Arguments(true, "subject-login")]
    [Arguments(false, "actor-login")]
    [Arguments(true, "actor-login")]
    [Arguments(false, "administrator")]
    [Arguments(true, "administrator")]
    [Arguments(false, "policy")]
    [Arguments(true, "policy")]
    [Arguments(false, "parent")]
    [Arguments(true, "parent")]
    [Arguments(false, "epoch")]
    [Arguments(true, "epoch")]
    public async Task ProfileAssessment_OriginalLossBeforeIndependentWriteCannotCreateMetadata(bool adopt, string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, true))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var source = adopt ? (await ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false))
            .RecordAsync(AssessmentRecord(observation, [])))! : null;
        var actor = adopt ? fixture.ReviewerId : fixture.OwnerId;
        var http = await ApplicabilityActorAsync(fixture, actor, true);
        if (change == "expiry") fixture.Clock.UtcNow = observation.ValidUntil;
        if (change == "pii") await fixture.Context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync();
        if (change == "subject-login") await fixture.Context.UserExternalLogins.Where(row => row.Id == fixture.LoginId).ExecuteDeleteAsync();
        if (change == "actor-login") await fixture.Context.UserExternalLogins.Where(row => row.UserId == actor).ExecuteDeleteAsync();
        if (change == "administrator") await fixture.Context.PlatformUserRoles.Where(row => row.UserId == actor).ExecuteDeleteAsync();
        if (change == "policy") await fixture.Context.ModerationPolicySelections.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null));
        if (change == "epoch") await fixture.Context.ModerationQualificationEpochs.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1));
        if (change == "parent")
            await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(new WithdrawModerationChildAgePolicyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Database.Scope, ExpectedQualificationRevision = 8,
                AdoptionId = fixture.ParentAdoption.AdoptionId, ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == "privacy")
        {
            await using var authority = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions);
            authority.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                PrivacyErasureSubjectKind.User, fixture.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest,
                1, Now.UtcDateTime, Now.UtcDateTime));
            await authority.SaveChangesAsync();
        }
        var owner = ApplicabilityOwner(fixture, http);
        if (adopt) await Assert.That(await owner.AdoptAsync(AssessmentAdopt(observation, source!))).IsNull();
        else await Assert.That(await owner.RecordAsync(AssessmentRecord(observation, []))).IsNull();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ProfileAssessment_RejectsNestedIdentityOwnerInsteadOfDroppingFinalCallback()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await AssessmentVocabularyAsync(fixture);
        var observation = (await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var owner = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.OwnerId, false));
        var source = (await owner.RecordAsync(AssessmentRecord(observation, [])))!;
        var reviewer = ApplicabilityOwner(fixture, await ApplicabilityActorAsync(fixture, fixture.ReviewerId, false));
        await fixture.Privacy.ExecuteEnrollmentSerializedAsync(async token =>
        {
            await Assert.That(async () => await owner.RecordAsync(AssessmentRecord(observation, []), token))
                .Throws<InvalidOperationException>();
            await Assert.That(async () => await reviewer.AdoptAsync(AssessmentAdopt(observation, source), token))
                .Throws<InvalidOperationException>();
            return true;
        }, _ => Task.CompletedTask, default);
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync()).IsEqualTo(0);
    }

    private static ModerationCollectionPrerequisiteKind[] AllAssessmentRequirements() =>
        [ModerationCollectionPrerequisiteKind.AccountEligibility, ModerationCollectionPrerequisiteKind.SubjectConsent,
            ModerationCollectionPrerequisiteKind.GuardianConsent];

    private static ModerationProfileCollectionAssessmentOriginal AssessmentOriginal(
        ModerationProfileCollectionSubjectObservation observation) => new(
        observation.Id, observation.Revision, observation.Generation, observation.ToBinding().Jurisdiction,
        observation.PolicyId, observation.PolicyRevision, observation.PublicationId, observation.ProfileId,
        (ModerationNativeMode)observation.ModeCode, observation.ProcedureSourceId, observation.ProcedureSourceRevision,
        observation.ProcedureSourceWriterGeneration, observation.ProcedureAdoptionId, observation.ProcedureAdoptionRevision,
        observation.ProcedureAdoptionWriterGeneration);

    private static RecordModerationProfileCollectionAssessment AssessmentRecord(
        ModerationProfileCollectionSubjectObservation observation, IReadOnlyList<ModerationCollectionPrerequisiteKind> required) =>
        new() { Original = AssessmentOriginal(observation), SourceId = Guid.CreateVersion7(),
            ExpectedGeneration = 0, Required = required, ValidUntil = Now.AddMinutes(4) };

    private static AdoptModerationProfileCollectionAssessment AssessmentAdopt(
        ModerationProfileCollectionSubjectObservation observation, ModerationProfileCollectionAssessmentSource source) =>
        new() { Original = AssessmentOriginal(observation), SourceId = source.Id, ExpectedSourceRevision = source.Revision,
            ExpectedSourceGeneration = source.Generation, ExpectedGeneration = source.Generation,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(3) };

    private static ModerationProfileCollectionAssessmentRepository ApplicabilityOwner(
        IndividualPolicyFixture fixture, HttpContextAccessor http, IPrivacyIdentityFenceAuthority? privacy = null) =>
        new(fixture.Context, ObservationAdmin(fixture, http), new Explore.Infrastructure.Identity.MachinePrincipalAccessor(http),
            http, privacy ?? fixture.Privacy, fixture.Clock, fixture.Database.Inventory);

    private static async Task<HttpContextAccessor> ApplicabilityActorAsync(IndividualPolicyFixture fixture, Guid actor, bool provider)
    {
        if (!provider) return new() { HttpContext = new DefaultHttpContext { User = IndividualPrincipal(actor) } };
        // Independent actor accounts do not rebind or replace the original subject login.
        var issuer = "https://profile-actor.example.invalid";
        var subject = actor.ToString("N");
        var account = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(issuer, subject);
        if (!await fixture.Context.UserExternalLogins.AnyAsync(row => row.UserId == actor && row.ProviderKey == account.Value))
        {
            fixture.Context.UserExternalLogins.Add(new()
            {
                Id = Guid.CreateVersion7(), UserId = actor, User = null!,
                AuthenticationProviderId = (int)account.ProviderKind, AuthenticationProvider = null!, ProviderKey = account.Value
            });
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();
        }
        return new() { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject), new Claim("iss", issuer)], "assessment-actor")) } };
    }

    private static async Task AssessmentVocabularyAsync(IndividualPolicyFixture fixture, bool relationship = true, bool consent = true)
    {
        var fields = new List<ModerationProfilePreliminaryFieldKind>
        {
            ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
            ModerationProfilePreliminaryFieldKind.AccountEligibilityReference,
            ModerationProfilePreliminaryFieldKind.SubjectConsentReference
        };
        if (relationship) fields.Add(ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference);
        if (consent) fields.Add(ModerationProfilePreliminaryFieldKind.GuardianConsentReference);
        var record = fixture.Record with
        {
            OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6,
            Procedure = new(ModerationIndividualPurpose.ProfileCompletion,
                ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment, fields,
                [ModerationProfileNormalFieldKind.DateOfBirth], "Independent relationship review.", "Independent manual qualification.")
        };
        await Assert.That((await fixture.Writer(fixture.OwnerId).ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await fixture.Writer(fixture.ReviewerId).ExecuteAsync(fixture.Adopt with
        {
            OperationId = Guid.CreateVersion7(), SourceId = record.SourceId, ExpectedSourceWriterGeneration = 7,
            ExpectedQualificationRevision = 7, AdoptionId = Guid.CreateVersion7()
        })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
    }
}
