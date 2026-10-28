using System.Data.Common;
using Event.Persistence.IntegrationTests.Privacy;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task Native_erasure_disposes_subject_profile_metadata_before_pii_without_borrowing_current_policy(
        bool retired, bool previouslyRead)
    {
        var probe = new ProfileErasureBoundary();
        await using var fixture = await IndividualPolicyFixture.CreateAsync(probe);
        var original = await PrepareProfileErasureAsync(fixture);
        if (retired)
        {
            var consent = await fixture.Context.Set<ModerationProfileSubjectConsent>()
                .SingleAsync(row => row.Id == original.Subject.Consent);
            var withdrawn = await ConsentOwner(fixture, original.Http).WithdrawAsync(ConsentWithdrawal(consent));
            await Assert.That(withdrawn).IsNotNull();
            await Assert.That(withdrawn!.WithdrawnAt).IsNotNull();
            fixture.Clock.UtcNow = Now.AddDays(1);
        }
        fixture.Context.ChangeTracker.Clear();
        fixture.Context.TenantContext = null;
        fixture.Context.ModerationTrustDomainId = null;
        probe.SubjectId = fixture.SubjectId;
        probe.SavedRows = previouslyRead ? original.Subject : null;
        await using (var runtime = GlobalLocationPrivacyErasureTests.CreateRuntime(
            fixture.Context, fixture.Privacy, fixture.Clock))
        {
            var result = await runtime.Service.EraseUserAsync(fixture.SubjectId, Guid.CreateVersion7(), default);
            await Assert.That(result.Status).IsEqualTo("completed");
        }
        await Assert.That(probe.PiiDeleteObserved).IsTrue();
        await Assert.That(probe.SavedRowsTrackedAtDisposal).IsEqualTo(previouslyRead);
        await AssertProfileErasureAsync(fixture, original.Other);
    }

    [Test]
    public async Task Failed_profile_metadata_disposal_rolls_back_primary_settlement_and_native_replay_finishes()
    {
        var probe = new ProfileErasureBoundary { FailAfterObservationDelete = true };
        await using var fixture = await IndividualPolicyFixture.CreateAsync(probe);
        var original = await PrepareProfileErasureAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        probe.SubjectId = fixture.SubjectId;
        var intentId = Guid.CreateVersion7();
        await using (var runtime = GlobalLocationPrivacyErasureTests.CreateRuntime(
            fixture.Context, fixture.Privacy, fixture.Clock))
            await Assert.ThrowsAsync<IOException>(() => runtime.Service.EraseUserAsync(fixture.SubjectId, intentId, default));
        await Assert.That(probe.Injected).IsTrue();
        await Assert.That(probe.PiiDeleteObserved).IsFalse();
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == original.Subject.Consent)).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == original.Subject.Adoption)).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionSubjectObservation>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == original.Subject.Observation)).IsTrue();
        await Assert.That(await fixture.Context.PrivacyErasurePolicyCoverage.AnyAsync()).IsFalse();
        var saga = await fixture.Context.PrivacyErasureSagas.SingleAsync(row => row.IntentId == intentId);
        await Assert.That(saga.LocalSettledAtUtc).IsNull();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        fixture.Context.ChangeTracker.Clear();
        await using (var restarted = GlobalLocationPrivacyErasureTests.CreateRuntime(
            fixture.Context, fixture.Privacy, fixture.Clock))
            await restarted.Service.ReplayPendingAsync(default);
        await Assert.That(probe.PiiDeleteObserved).IsTrue();
        await AssertProfileErasureAsync(fixture, original.Other);
    }

    private static async Task<(Microsoft.AspNetCore.Http.HttpContextAccessor Http,
        ProfileErasureRows Subject, ProfileErasureRows Other)> PrepareProfileErasureAsync(IndividualPolicyFixture fixture)
    {
        var (http, observation, assessment, request) = await ConsentOriginalAsync(fixture, false,
            [ModerationCollectionPrerequisiteKind.SubjectConsent]);
        var consent = await ConsentOwner(fixture, http).GrantAsync(request);
        await Assert.That(consent).IsNotNull();
        var admission = AdmissionOwner(fixture, http);
        await Assert.That(await admission.ExecuteAsync(request.Assessment, consent!.Id, async (_, token) =>
            await admission.RecordBirthDateAsync(fixture.SubjectId, new(2000, 2, 29), 0, token))).IsTrue();
        var account = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://profile-policy.example.invalid", "other-profile-erasure-human");
        var loginId = Guid.CreateVersion7();
        fixture.Context.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = loginId,
            UserId = fixture.OtherSubjectId,
            User = null!,
            AuthenticationProviderId = (int)account.ProviderKind,
            AuthenticationProvider = null!,
            ProviderKey = account.Value
        });
        await fixture.Context.SaveChangesAsync();
        var other = new IndividualPolicyFixture
        {
            Database = fixture.Database,
            Context = fixture.Context,
            Privacy = fixture.Privacy,
            PrivacyOptions = fixture.PrivacyOptions,
            Clock = fixture.Clock,
            Age = fixture.Age,
            ParentAdoption = fixture.ParentAdoption,
            Record = fixture.Record,
            Adopt = fixture.Adopt,
            SubjectId = fixture.OtherSubjectId,
            OtherSubjectId = fixture.SubjectId,
            OwnerId = fixture.OwnerId,
            ReviewerId = fixture.ReviewerId,
            LoginId = loginId
        };
        // The shared procedure vocabulary was published once for both independent subjects.
        var otherHttp = ObservationPrincipal(other, false);
        var otherObservation = (await ObservationOwner(other, otherHttp).ObserveAsync(ObservationIntent(other)))!;
        var otherSource = (await ApplicabilityOwner(other, await ApplicabilityActorAsync(other, other.OwnerId, false))
            .RecordAsync(AssessmentRecord(otherObservation, [ModerationCollectionPrerequisiteKind.SubjectConsent])))!;
        var otherAssessment = (await ApplicabilityOwner(other, await ApplicabilityActorAsync(other, other.ReviewerId, false))
            .AdoptAsync(AssessmentAdopt(otherObservation, otherSource)))!;
        var otherRequest = ConsentGrant(otherObservation, otherAssessment);
        var otherConsent = await ConsentOwner(other, otherHttp).GrantAsync(otherRequest);
        await Assert.That(otherConsent).IsNotNull();
        return (http, new(observation.Id, assessment.SourceId, assessment.Id, consent.Id),
            new(otherObservation.Id, otherAssessment.SourceId, otherAssessment.Id, otherConsent!.Id));
    }

    private static async Task AssertProfileErasureAsync(IndividualPolicyFixture fixture, ProfileErasureRows other)
    {
        var context = fixture.Context;
        context.ChangeTracker.Clear();
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
        await Assert.That(await context.Set<UserBirthDateCollectionReceipt>().AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
        await Assert.That(await context.Users.IgnoreQueryFilters().AnyAsync(row => row.Id == fixture.SubjectId && row.IsDeleted)).IsTrue();
        await Assert.That(await context.Set<ModerationProfileCollectionSubjectObservation>().IgnoreQueryFilters()
            .AnyAsync(row => row.SubjectHumanId == fixture.SubjectId)).IsFalse();
        await Assert.That(await context.Set<ModerationProfileCollectionAssessmentSource>().IgnoreQueryFilters()
            .AnyAsync(row => row.SubjectHumanId == fixture.SubjectId)).IsFalse();
        await Assert.That(await context.Set<ModerationProfileSubjectConsent>().IgnoreQueryFilters()
            .AnyAsync(row => row.SubjectHumanId == fixture.SubjectId)).IsFalse();
        await Assert.That(await context.Set<ModerationProfileCollectionSubjectObservation>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == other.Observation)).IsTrue();
        await Assert.That(await context.Set<ModerationProfileCollectionAssessmentSource>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == other.Assessment)).IsTrue();
        await Assert.That(await context.Set<ModerationProfileCollectionAssessmentAdoption>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == other.Adoption)).IsTrue();
        await Assert.That(await context.Set<ModerationProfileSubjectConsent>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == other.Consent)).IsTrue();
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.OtherSubjectId)).IsTrue();
        await Assert.That(await context.Set<ModerationProfileCollectionProcedureSource>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == fixture.Record.SourceId)).IsTrue();
        await Assert.That(await context.Set<ModerationProfileCollectionProcedureAdoption>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == fixture.Adopt.AdoptionId)).IsTrue();
        await Assert.That(await context.Set<ModerationChildAgePolicySource>().IgnoreQueryFilters()
            .AnyAsync(row => row.Id == fixture.Age.SourceId)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed record ProfileErasureRows(Guid Observation, Guid Assessment, Guid Adoption, Guid Consent);

    private sealed class ProfileErasureBoundary : DbCommandInterceptor
    {
        public Guid SubjectId { get; set; }
        public bool FailAfterObservationDelete { get; init; }
        public bool Injected { get; private set; }
        public bool PiiDeleteObserved { get; private set; }
        public ProfileErasureRows? SavedRows { get; set; }
        public bool SavedRowsTrackedAtDisposal { get; private set; }
        private bool _loadingSavedRows;
        private bool _savedRowsLoaded;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context)
            {
                CheckTrackedDisposal(context, command);
                await CheckPiiDisposalAsync(context, command, cancellationToken);
            }
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context)
            {
                if (!_loadingSavedRows && !_savedRowsLoaded && SavedRows is { } saved
                    && Reads<ModerationProfileSubjectConsent>(context, command))
                {
                    // Reconciliation clears earlier reads; load at the first actual disposal query instead.
                    _loadingSavedRows = true;
                    try
                    {
                        if (context.Database.CurrentTransaction is null
                            || !await context.PrivacyErasureSagas.AsNoTracking()
                                .AnyAsync(row => row.SubjectId == SubjectId, cancellationToken))
                            throw new InvalidOperationException("Saved profile metadata must be loaded inside native settlement.");
                        await context.Set<ModerationProfileSubjectConsent>().IgnoreQueryFilters()
                            .Where(row => row.Id == saved.Consent).LoadAsync(cancellationToken);
                        await context.Set<ModerationProfileCollectionAssessmentAdoption>().IgnoreQueryFilters()
                            .Where(row => row.Id == saved.Adoption).LoadAsync(cancellationToken);
                        await context.Set<ModerationProfileCollectionAssessmentSource>().IgnoreQueryFilters()
                            .Where(row => row.Id == saved.Assessment).LoadAsync(cancellationToken);
                        await context.Set<ModerationProfileCollectionSubjectObservation>().IgnoreQueryFilters()
                            .Where(row => row.Id == saved.Observation).LoadAsync(cancellationToken);
                        await context.UserPii.Where(row => row.UserId == SubjectId).LoadAsync(cancellationToken);
                        _savedRowsLoaded = true;
                    }
                    finally { _loadingSavedRows = false; }
                }
                await CheckPiiDisposalAsync(context, command, cancellationToken);
            }
            return result;
        }

        private void CheckTrackedDisposal(ExploreDbContext context, DbCommand command)
        {
            if (SavedRows is not { } saved || !Deletes<ModerationProfileSubjectConsent>(context, command)) return;
            if (!_savedRowsLoaded
                || !Tracked<ModerationProfileSubjectConsent>(context, saved.Consent)
                || !Tracked<ModerationProfileCollectionAssessmentAdoption>(context, saved.Adoption)
                || !Tracked<ModerationProfileCollectionAssessmentSource>(context, saved.Assessment)
                || !Tracked<ModerationProfileCollectionSubjectObservation>(context, saved.Observation))
                throw new InvalidOperationException("Saved profile rows must remain tracked at the disposal boundary.");
            SavedRowsTrackedAtDisposal = true;
        }

        private static bool Tracked<T>(ExploreDbContext context, Guid id) where T : class =>
            context.ChangeTracker.Entries<T>().Any(entry => entry.Property<Guid>("Id").CurrentValue == id
                && entry.State == EntityState.Unchanged);

        private async Task CheckPiiDisposalAsync(ExploreDbContext context, DbCommand command, CancellationToken cancellationToken)
        {
            if (SubjectId == Guid.Empty || !Deletes<UserPii>(context, command)) return;
            if (SavedRows is { } saved
                && (Tracked<ModerationProfileSubjectConsent>(context, saved.Consent)
                    || Tracked<ModerationProfileCollectionAssessmentAdoption>(context, saved.Adoption)
                    || Tracked<ModerationProfileCollectionAssessmentSource>(context, saved.Assessment)
                    || Tracked<ModerationProfileCollectionSubjectObservation>(context, saved.Observation)))
                throw new InvalidOperationException("Disposed profile metadata must not be cascaded again by native PII deletion.");
            if (await context.Set<ModerationProfileCollectionSubjectObservation>().IgnoreQueryFilters()
                    .AnyAsync(row => row.SubjectHumanId == SubjectId, cancellationToken)
                || await context.Set<ModerationProfileCollectionAssessmentSource>().IgnoreQueryFilters()
                    .AnyAsync(row => row.SubjectHumanId == SubjectId, cancellationToken)
                || await context.Set<ModerationProfileCollectionAssessmentAdoption>().IgnoreQueryFilters()
                    .AnyAsync(row => row.Source.SubjectHumanId == SubjectId, cancellationToken)
                || await context.Set<ModerationProfileSubjectConsent>().IgnoreQueryFilters()
                    .AnyAsync(row => row.SubjectHumanId == SubjectId, cancellationToken))
                throw new InvalidOperationException("Subject profile metadata must settle before native PII disposal.");
            PiiDeleteObserved = true;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (SubjectId != Guid.Empty && FailAfterObservationDelete && !Injected && result > 0
                && eventData.Context is ExploreDbContext context && Deletes<ModerationProfileCollectionSubjectObservation>(context, command))
            {
                Injected = true;
                throw new IOException("Profile metadata disposal checkpoint.");
            }
            return ValueTask.FromResult(result);
        }

        private static bool Deletes<T>(ExploreDbContext context, DbCommand command)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            return command.CommandText.TrimStart().StartsWith($"DELETE FROM {table}", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Reads<T>(ExploreDbContext context, DbCommand command)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            return command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"FROM {table}", StringComparison.OrdinalIgnoreCase);
        }
    }
}
