using System.Security.Claims;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    /// <summary>
    /// Retained assessment rows are mapping-only starting states, not a native applicability
    /// determination or activation/conformance proof. The shared fixture writes actual parents
    /// and procedures but declares historical ACTIVE read state. These witnesses exercise only
    /// native terminal correction, which cannot authorize collection or manufacture acceptance.
    /// </summary>
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ProfileAssessmentWithdrawal_NominatedNonAdminCanCorrectExpiredOriginal(
        bool reviewer, bool provider)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        var actor = reviewer ? fixture.ReviewerId : fixture.OwnerId;
        fixture.Context.ChangeTracker.Clear();
        await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(
            new WithdrawModerationChildAgePolicyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Database.Scope,
                ExpectedQualificationRevision = 8, AdoptionId = fixture.ParentAdoption.AdoptionId,
                ExpectedAdoptionRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await fixture.Context.PlatformUserRoles.Where(row =>
            row.UserId == fixture.OwnerId || row.UserId == fixture.ReviewerId).ExecuteDeleteAsync();
        await fixture.Context.ModerationPolicySelections.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null));
        fixture.Clock.UtcNow = Now.AddMinutes(9);
        fixture.Context.ChangeTracker.Clear();
        var http = await AssessmentActorAsync(fixture, actor, provider);
        var receipts = await fixture.Context.ModerationQualificationMutationReceipts.CountAsync();
        var request = AssessmentWithdrawal(original.Adoption);
        var result = await AssessmentWithdrawalOwner(fixture, http).WithdrawAsync(request);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Revision).IsEqualTo(2);
        await Assert.That(result.WithdrawnByHumanId).IsEqualTo(actor);
        await Assert.That(result.WithdrawnAt).IsEqualTo(Now.AddMinutes(9));
        await Assert.That(result.Source.Matches(original.Source)).IsTrue();
        await Assert.That(result.Generation).IsEqualTo(original.Adoption.Generation);
        await Assert.That(result.ValidUntil).IsEqualTo(original.Adoption.ValidUntil);
        await Assert.That(await AssessmentWithdrawalOwner(fixture, http).WithdrawAsync(request)).IsNull();
        await Assert.That(await fixture.Context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(receipts);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        fixture.Context.ChangeTracker.Clear();
        var retained = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking().SingleAsync();
        await Assert.That(retained.WithdrawnByHumanId).IsEqualTo(actor);
        await Assert.That(retained.Revision).IsEqualTo(2);
    }

    public enum AssessmentWithdrawalDenial
    {
        Subject, UnnominatedAdministrator, Anonymous, ProviderRebound, ForeignTenant, ForeignTrust,
        MissingPii, FencedSubject, FencedActor, WrongSubject, WrongSource, WrongSourceGeneration,
        WrongAdoptionGeneration, WrongRevision, WrongPublication, WrongPolicy, WrongProfile, WrongMode
    }

    [Test]
    [Arguments(AssessmentWithdrawalDenial.Subject)]
    [Arguments(AssessmentWithdrawalDenial.UnnominatedAdministrator)]
    [Arguments(AssessmentWithdrawalDenial.Anonymous)]
    [Arguments(AssessmentWithdrawalDenial.ProviderRebound)]
    [Arguments(AssessmentWithdrawalDenial.ForeignTenant)]
    [Arguments(AssessmentWithdrawalDenial.ForeignTrust)]
    [Arguments(AssessmentWithdrawalDenial.MissingPii)]
    [Arguments(AssessmentWithdrawalDenial.FencedSubject)]
    [Arguments(AssessmentWithdrawalDenial.FencedActor)]
    [Arguments(AssessmentWithdrawalDenial.WrongSubject)]
    [Arguments(AssessmentWithdrawalDenial.WrongSource)]
    [Arguments(AssessmentWithdrawalDenial.WrongSourceGeneration)]
    [Arguments(AssessmentWithdrawalDenial.WrongAdoptionGeneration)]
    [Arguments(AssessmentWithdrawalDenial.WrongRevision)]
    [Arguments(AssessmentWithdrawalDenial.WrongPublication)]
    [Arguments(AssessmentWithdrawalDenial.WrongPolicy)]
    [Arguments(AssessmentWithdrawalDenial.WrongProfile)]
    [Arguments(AssessmentWithdrawalDenial.WrongMode)]
    public async Task ProfileAssessmentWithdrawal_IntentCannotSubstituteOriginalAuthority(AssessmentWithdrawalDenial change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var actor = change switch
        {
            AssessmentWithdrawalDenial.Subject => fixture.SubjectId,
            AssessmentWithdrawalDenial.UnnominatedAdministrator => fixture.Database.AdminId,
            _ => fixture.OwnerId
        };
        var http = await AssessmentActorAsync(fixture, actor, change == AssessmentWithdrawalDenial.ProviderRebound);
        var request = AssessmentWithdrawal(original.Adoption);
        request = change switch
        {
            AssessmentWithdrawalDenial.WrongSubject => request with { SubjectHumanId = fixture.OtherSubjectId },
            AssessmentWithdrawalDenial.WrongSource => request with { SourceId = Guid.CreateVersion7() },
            AssessmentWithdrawalDenial.WrongSourceGeneration => request with { ExpectedSourceGeneration = 3 },
            AssessmentWithdrawalDenial.WrongAdoptionGeneration => request with { ExpectedAdoptionGeneration = 4 },
            AssessmentWithdrawalDenial.WrongRevision => request with { ExpectedAdoptionRevision = 2 },
            AssessmentWithdrawalDenial.WrongPublication => request with { PublicationId = Guid.CreateVersion7() },
            AssessmentWithdrawalDenial.WrongPolicy => request with { PolicyId = Guid.CreateVersion7() },
            AssessmentWithdrawalDenial.WrongProfile => request with { ProfileId = Guid.CreateVersion7() },
            AssessmentWithdrawalDenial.WrongMode => request with { Mode = ModerationNativeMode.RemoteStrict },
            _ => request
        };
        if (change == AssessmentWithdrawalDenial.Anonymous)
            http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (change == AssessmentWithdrawalDenial.ProviderRebound)
            await fixture.Context.UserExternalLogins.Where(row => row.Id == fixture.LoginId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UserId, fixture.OtherSubjectId));
        if (change == AssessmentWithdrawalDenial.ForeignTenant)
            fixture.Context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        if (change == AssessmentWithdrawalDenial.ForeignTrust)
            fixture.Context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == AssessmentWithdrawalDenial.MissingPii)
            await fixture.Context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync();
        if (change is AssessmentWithdrawalDenial.FencedSubject or AssessmentWithdrawalDenial.FencedActor)
        {
            await using var privacy = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions);
            privacy.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                PrivacyErasureSubjectKind.User,
                change == AssessmentWithdrawalDenial.FencedActor ? actor : fixture.SubjectId,
                PrivacyErasureReasonCode.SubjectErasureRequest, 1, Now.UtcDateTime, Now.UtcDateTime));
            await privacy.SaveChangesAsync();
        }
        await Assert.That(await AssessmentWithdrawalOwner(fixture, http).WithdrawAsync(request)).IsNull();
        fixture.Context.TenantContext = fixture.Database;
        fixture.Context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        fixture.Context.ChangeTracker.Clear();
        var retained = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>()
            .AsNoTracking().SingleOrDefaultAsync();
        if (change == AssessmentWithdrawalDenial.MissingPii)
            await Assert.That(retained).IsNull();
        else
            await Assert.That(retained!.Revision).IsEqualTo(1);
    }

    public enum AssessmentWithdrawalCommitChange
    {
        Principal, ProviderLoginReplacement, Tenant, Trust, Host, Epoch, ActorDeleted,
        SubjectPiiDeleted, SubjectFenced, SourceChanged, AdoptionChanged, ClockRegression,
        Cancellation, OuterRollback, ExpiredParents, LatestInvalidSource
    }

    [Test]
    [Arguments(AssessmentWithdrawalCommitChange.Principal)]
    [Arguments(AssessmentWithdrawalCommitChange.ProviderLoginReplacement)]
    [Arguments(AssessmentWithdrawalCommitChange.Tenant)]
    [Arguments(AssessmentWithdrawalCommitChange.Trust)]
    [Arguments(AssessmentWithdrawalCommitChange.Host)]
    [Arguments(AssessmentWithdrawalCommitChange.Epoch)]
    [Arguments(AssessmentWithdrawalCommitChange.ActorDeleted)]
    [Arguments(AssessmentWithdrawalCommitChange.SubjectPiiDeleted)]
    [Arguments(AssessmentWithdrawalCommitChange.SubjectFenced)]
    [Arguments(AssessmentWithdrawalCommitChange.SourceChanged)]
    [Arguments(AssessmentWithdrawalCommitChange.AdoptionChanged)]
    [Arguments(AssessmentWithdrawalCommitChange.ClockRegression)]
    [Arguments(AssessmentWithdrawalCommitChange.Cancellation)]
    [Arguments(AssessmentWithdrawalCommitChange.OuterRollback)]
    [Arguments(AssessmentWithdrawalCommitChange.ExpiredParents)]
    [Arguments(AssessmentWithdrawalCommitChange.LatestInvalidSource)]
    public async Task ProfileAssessmentWithdrawal_ActualOuterCommitRequalifiesOriginal(
        AssessmentWithdrawalCommitChange change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var context = fixture.Context;
        var http = await AssessmentActorAsync(fixture, fixture.OwnerId,
            change == AssessmentWithdrawalCommitChange.ProviderLoginReplacement);
        using var cancellation = new CancellationTokenSource();
        var boundary = new AssessmentCommitBoundary(fixture.Privacy, async token =>
        {
            // This is the real authority's final hook after its operation and disclosure flush.
            await Assert.That(context.IdentityFenceOwnsTransaction).IsTrue();
            await Assert.That((await context.Set<ModerationProfileCollectionAssessmentAdoption>()
                .AsNoTracking().SingleAsync(token)).Revision).IsEqualTo(2);
            switch (change)
            {
                case AssessmentWithdrawalCommitChange.Principal:
                    http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId);
                    break;
                case AssessmentWithdrawalCommitChange.ProviderLoginReplacement:
                    await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId).ExecuteDeleteAsync(token);
                    var replacement = fixture.Login(Guid.CreateVersion7());
                    replacement.UserId = fixture.OwnerId;
                    context.UserExternalLogins.Add(replacement);
                    await context.SaveChangesAsync(token);
                    break;
                case AssessmentWithdrawalCommitChange.Tenant:
                    context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
                    break;
                case AssessmentWithdrawalCommitChange.Trust:
                    context.ModerationTrustDomainId = Guid.CreateVersion7();
                    break;
                case AssessmentWithdrawalCommitChange.Host:
                    await context.ModerationHostBindings.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.InstanceId, Guid.CreateVersion7()), token);
                    break;
                case AssessmentWithdrawalCommitChange.Epoch:
                    await context.ModerationQualificationEpochs.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), token);
                    break;
                case AssessmentWithdrawalCommitChange.ActorDeleted:
                    await context.Users.Where(row => row.Id == fixture.OwnerId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true), token);
                    break;
                case AssessmentWithdrawalCommitChange.SubjectPiiDeleted:
                    await context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync(token);
                    break;
                case AssessmentWithdrawalCommitChange.SubjectFenced:
                    await using (var privacy = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions))
                    {
                        await privacy.Database.UseTransactionAsync(context.Database.CurrentTransaction!.GetDbTransaction(), token);
                        privacy.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                            PrivacyErasureSubjectKind.User, fixture.SubjectId,
                            PrivacyErasureReasonCode.SubjectErasureRequest, 1, Now.UtcDateTime, Now.UtcDateTime));
                        await privacy.SaveChangesAsync(token);
                    }
                    break;
                case AssessmentWithdrawalCommitChange.SourceChanged:
                    await context.Set<ModerationProfileCollectionAssessmentSource>().Where(row => row.Id == original.Source.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Generation, 4), token);
                    break;
                case AssessmentWithdrawalCommitChange.AdoptionChanged:
                    await context.Set<ModerationProfileCollectionAssessmentAdoption>()
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.WithdrawnByHumanId, fixture.ReviewerId), token);
                    break;
                case AssessmentWithdrawalCommitChange.ClockRegression:
                    fixture.Clock.UtcNow = Now;
                    break;
                case AssessmentWithdrawalCommitChange.Cancellation:
                    cancellation.Cancel();
                    break;
                case AssessmentWithdrawalCommitChange.OuterRollback:
                    throw new AssessmentOuterRollback();
                case AssessmentWithdrawalCommitChange.ExpiredParents:
                    fixture.Clock.UtcNow = Now.AddMinutes(12);
                    await context.ModerationPolicySelections.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null), token);
                    break;
                case AssessmentWithdrawalCommitChange.LatestInvalidSource:
                    // A newer, unaccepted proposal cannot obstruct exact historical correction.
                    var age = await context.Set<ModerationChildAgePolicySource>().AsNoTracking().SingleAsync(token);
                    var parent = await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking().SingleAsync(token);
                    var procedure = await context.Set<ModerationProfileCollectionProcedureAdoption>()
                        .AsNoTracking().SingleAsync(row => row.Id == original.Source.ProcedureAdoptionId, token);
                    var procedureSource = await context.Set<ModerationProfileCollectionProcedureSource>()
                        .SingleAsync(row => row.Id == original.Source.ProcedureSourceId, token);
                    context.Add(ModerationProfileCollectionAssessmentSource.Record(Guid.CreateVersion7(),
                        procedureSource, procedure, age, parent, original.Source.Binding, [],
                        fixture.OwnerId, 4, fixture.Clock.UtcNow, original.Source.ValidUntil));
                    await context.SaveChangesAsync(token);
                    break;
            }
        });
        var owner = AssessmentWithdrawalOwner(fixture, http, boundary);
        var request = AssessmentWithdrawal(original.Adoption);
        var succeeds = change is AssessmentWithdrawalCommitChange.ExpiredParents
            or AssessmentWithdrawalCommitChange.LatestInvalidSource;
        if (change == AssessmentWithdrawalCommitChange.Cancellation)
            await Assert.That(async () => await owner.WithdrawAsync(request, cancellation.Token))
                .Throws<OperationCanceledException>();
        else if (change == AssessmentWithdrawalCommitChange.OuterRollback)
            await Assert.That(async () => await owner.WithdrawAsync(request)).Throws<AssessmentOuterRollback>();
        else
            await Assert.That(await owner.WithdrawAsync(request) is not null).IsEqualTo(succeeds);
        await Assert.That(boundary.Reached).IsTrue();
        context.TenantContext = fixture.Database;
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        context.ChangeTracker.Clear();
        var retained = await context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking().SingleAsync();
        await Assert.That(retained.Revision).IsEqualTo(succeeds ? 2 : 1);
        await Assert.That(retained.Source.Matches(original.Source)).IsTrue();
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task ProfileAssessmentWithdrawal_RejectsNestedIdentityUnitWithoutIgnoringOuterCallback()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var http = await AssessmentActorAsync(fixture, fixture.OwnerId, false);
        await fixture.Privacy.ExecuteEnrollmentSerializedAsync(async token =>
        {
            await Assert.That(async () => await AssessmentWithdrawalOwner(fixture, http)
                .WithdrawAsync(AssessmentWithdrawal(original.Adoption), token)).Throws<InvalidOperationException>();
            return true;
        }, _ => Task.CompletedTask, default);
        var retained = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().AsNoTracking().SingleAsync();
        await Assert.That(retained.Revision).IsEqualTo(1);
    }

    private static WithdrawModerationProfileCollectionAssessment AssessmentWithdrawal(
        ModerationProfileCollectionAssessmentAdoption adoption) => new(
        adoption.Source.Binding.Jurisdiction, adoption.Source.SubjectHumanId,
        adoption.Source.ProcedureSource.PolicyId, adoption.Source.ProcedureSource.PolicyRevision,
        adoption.Source.ProcedureSource.PublicationId, adoption.Source.ProcedureSource.ProfileId,
        (ModerationNativeMode)adoption.Source.ProcedureSource.ModeCode, adoption.SourceId,
        adoption.SourceGeneration, adoption.Id, adoption.Generation, 1);

    private static async Task<HttpContextAccessor> AssessmentActorAsync(
        IndividualPolicyFixture fixture, Guid actor, bool provider)
    {
        if (provider)
            await fixture.Context.UserExternalLogins.Where(row => row.Id == fixture.LoginId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UserId, actor));
        return new() { HttpContext = new DefaultHttpContext
        { User = provider ? fixture.ProviderPrincipal() : IndividualPrincipal(actor) } };
    }

    private static ModerationProfileCollectionAssessmentRepository AssessmentWithdrawalOwner(
        IndividualPolicyFixture fixture, HttpContextAccessor http, IPrivacyIdentityFenceAuthority? privacy = null)
    {
        var admin = new AdminContext(http, new PlatformUserRoleRepository(fixture.Context),
            new TenantUserRoleGrantRepository(fixture.Context), new OrganizationMemberRepository(fixture.Context),
            new GroupMemberRepository(fixture.Context), new UserExternalLoginRepository(fixture.Context),
            NullLogger<AdminContext>.Instance);
        return new(fixture.Context, admin, new MachinePrincipalAccessor(http), http,
            privacy ?? fixture.Privacy, fixture.Clock, fixture.Database.Inventory);
    }

    private sealed class AssessmentOuterRollback : Exception;

    private sealed class AssessmentCommitBoundary(IPrivacyIdentityFenceAuthority inner,
        Func<CancellationToken, Task> finalMutation) : IPrivacyIdentityFenceAuthority
    {
        public bool Reached { get; private set; }
        public Task<T> ExecuteEnrollmentSerializedAsync<T>(Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> beforePrimaryCommit, CancellationToken token) =>
            inner.ExecuteEnrollmentSerializedAsync(operation, async finalToken =>
            {
                Reached = true;
                await finalMutation(finalToken);
                await beforePrimaryCommit(finalToken);
            }, token);
        public Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token) =>
            inner.ExecuteSerializedAsync(operation, token);
        public Task ValidateKeyAsync(string keyId, string tag, CancellationToken token) =>
            inner.ValidateKeyAsync(keyId, tag, token);
        public Task<PrivacyErasureIntent?> FindAsync(PrivacyIdentityFingerprint fingerprint, CancellationToken token) =>
            inner.FindAsync(fingerprint, token);
        public Task<bool> IsSubjectFencedAsync(Guid subject, CancellationToken token) =>
            inner.IsSubjectFencedAsync(subject, token);
    }
}
