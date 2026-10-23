using System.Security.Claims;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubjectObservation_OriginalNonAdminHumanSurvivesIndependentActorSession(bool provider)
    {
        // Historical ACTIVE/Draft graph is a declared read precondition, not activation proof.
        // The fixture creates actual qualified child-age/procedure sources through native writers.
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var http = ObservationPrincipal(fixture, provider);
        var original = await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture));
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.SubjectHumanId).IsEqualTo(fixture.SubjectId);
        await Assert.That(original.SubjectLoginId).IsEqualTo(provider ? fixture.LoginId : (Guid?)null);
        await Assert.That(original.Generation).IsEqualTo(1L);
        await Assert.That(original.ObservationSequence).IsEqualTo(1L);
        await Assert.That(original.EpochRevision).IsEqualTo(6L);
        await Assert.That(original.ObservedAt).IsEqualTo(Now);
        await Assert.That(original.ValidUntil).IsEqualTo(fixture.Adopt.ValidUntil);
        await Assert.That(await fixture.Context.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.SubjectId)).IsFalse();
        var binding = original.ToBinding();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        http.HttpContext!.User = IndividualPrincipal(fixture.OwnerId);
        var read = await ReadSubjectObservationAsync(fixture, http, original.Id);
        await Assert.That(read).IsNotNull();
        await Assert.That(read!.ToBinding()).IsEqualTo(binding);
        await Assert.That(read.SubjectHumanId).IsNotEqualTo(fixture.OwnerId);
        await Assert.That(read.ObservedAt).IsEqualTo(Now);
        await Assert.That(read.ValidUntil).IsEqualTo(fixture.Adopt.ValidUntil);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task SubjectObservation_NominationHasNoTargetAndNeverAliasesAnotherHuman()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var http = ObservationPrincipal(fixture, false);
        var owner = ObservationOwner(fixture, http);
        var first = await owner.ObserveAsync(ObservationIntent(fixture));
        http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId);
        var second = await owner.ObserveAsync(ObservationIntent(fixture));
        await Assert.That(first!.SubjectHumanId).IsEqualTo(fixture.SubjectId);
        await Assert.That(second!.SubjectHumanId).IsEqualTo(fixture.OtherSubjectId);
        await Assert.That(second.Generation).IsEqualTo(1L);
        await Assert.That(first.ToBinding().Target).IsNotEqualTo(second.ToBinding().Target);
    }

    [Test]
    public async Task SubjectObservation_UnrelatedActualHumanCannotReadAnotherOriginal()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var http = ObservationPrincipal(fixture, false);
        var original = (await ObservationOwner(fixture, http).ObserveAsync(ObservationIntent(fixture)))!;
        http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId);
        await Assert.That(await ReadSubjectObservationAsync(fixture, http, original.Id)).IsNull();
        http.HttpContext.User = IndividualPrincipal(fixture.SubjectId);
        await Assert.That(await ReadSubjectObservationAsync(fixture, http, original.Id)).IsNotNull();
    }

    [Test]
    [Arguments("anonymous")]
    [Arguments("missing-human")]
    [Arguments("provider-missing")]
    [Arguments("missing-tenant")]
    [Arguments("foreign-tenant")]
    [Arguments("foreign-trust")]
    [Arguments("missing-pii")]
    [Arguments("fenced-human")]
    [Arguments("wrong-profile")]
    [Arguments("draft-only")]
    [Arguments("owner-role")]
    [Arguments("reviewer-role")]
    public async Task SubjectObservation_RejectsUnresolvedIdentityScopeAndUnqualifiedParents(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var context = fixture.Context;
        var http = ObservationPrincipal(fixture, change == "provider-missing");
        if (change == "anonymous") http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (change == "missing-human") http.HttpContext!.User = IndividualPrincipal(Guid.CreateVersion7());
        if (change == "provider-missing")
            await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId).ExecuteDeleteAsync();
        if (change == "missing-tenant") context.TenantContext = null;
        if (change == "foreign-tenant") context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        if (change == "foreign-trust") context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == "missing-pii")
            await context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync();
        if (change == "fenced-human")
        {
            await using var authority = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions);
            authority.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                PrivacyErasureSubjectKind.User, fixture.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest,
                1, Now.UtcDateTime, Now.UtcDateTime));
            await authority.SaveChangesAsync();
        }
        if (change == "draft-only")
            await context.ModerationPolicySelections.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null));
        if (change is "owner-role" or "reviewer-role")
        {
            var human = change == "owner-role" ? fixture.OwnerId : fixture.ReviewerId;
            await context.PlatformUserRoles.Where(row => row.UserId == human).ExecuteDeleteAsync();
        }
        var intent = change == "wrong-profile"
            ? new NominateModerationProfileCollectionSubjectObservation(Guid.CreateVersion7(), fixture.Age.Mode)
            : ObservationIntent(fixture);
        await Assert.That(await ObservationOwner(fixture, http).ObserveAsync(intent)).IsNull();
        context.TenantContext = fixture.Database;
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationProfileCollectionSubjectObservation>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("none")]
    [Arguments("principal")]
    [Arguments("provider-replacement")]
    [Arguments("provider-rebinding")]
    [Arguments("pii")]
    [Arguments("fenced-human")]
    [Arguments("tenant")]
    [Arguments("trust")]
    [Arguments("host")]
    [Arguments("epoch")]
    [Arguments("policy")]
    [Arguments("procedure")]
    [Arguments("parent")]
    [Arguments("expiry")]
    [Arguments("clock")]
    [Arguments("cancel")]
    [Arguments("rollback")]
    public async Task SubjectObservation_ActualOuterFinalCommitRevalidatesOrRollsBackEverything(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var context = fixture.Context;
        var http = ObservationPrincipal(fixture, change.StartsWith("provider-", StringComparison.Ordinal));
        using var cancellation = new CancellationTokenSource();
        var boundary = new AssessmentCommitBoundary(fixture.Privacy, async token =>
        {
            // The row has already been saved inside the real primary transaction.
            await Assert.That(await context.Set<ModerationProfileCollectionSubjectObservation>().CountAsync(token)).IsEqualTo(1);
            switch (change)
            {
                case "principal":
                    http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId);
                    break;
                case "provider-replacement":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId).ExecuteDeleteAsync(token);
                    context.UserExternalLogins.Add(fixture.Login(Guid.CreateVersion7()));
                    await context.SaveChangesAsync(token);
                    break;
                case "provider-rebinding":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UserId, fixture.OtherSubjectId), token);
                    break;
                case "pii":
                    await context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync(token);
                    break;
                case "fenced-human":
                    await using (var authority = new EmbeddedPrivacyErasureAuthorityDbContext(fixture.PrivacyOptions))
                    {
                        await authority.Database.UseTransactionAsync(
                            context.Database.CurrentTransaction!.GetDbTransaction(), token);
                        authority.ErasureIntents.Add(PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                            PrivacyErasureSubjectKind.User, fixture.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest,
                            1, Now.UtcDateTime, Now.UtcDateTime));
                        await authority.SaveChangesAsync(token);
                    }
                    break;
                case "tenant":
                    context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
                    break;
                case "trust":
                    context.ModerationTrustDomainId = Guid.CreateVersion7();
                    break;
                case "host":
                    await context.ModerationHostBindings.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.InstanceId, Guid.CreateVersion7()), token);
                    break;
                case "epoch":
                    await context.ModerationQualificationEpochs.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.Revision, 7), token);
                    break;
                case "policy":
                    await context.ModerationPolicySelections.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.ActivePublicationId, (Guid?)null), token);
                    break;
                case "procedure":
                    await context.Set<ModerationProfileCollectionProcedureAdoption>()
                        .Where(row => row.Id == fixture.Adopt.AdoptionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ValidUntil, Now.AddMinutes(4)), token);
                    break;
                case "parent":
                    await context.Set<ModerationChildAgePolicySource>().Where(row => row.Id == fixture.Age.SourceId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DefinitionId, Guid.CreateVersion7()), token);
                    break;
                case "expiry":
                    fixture.Clock.UtcNow = fixture.Adopt.ValidUntil;
                    break;
                case "clock":
                    fixture.Clock.UtcNow = Now.AddTicks(-1);
                    break;
                case "cancel":
                    cancellation.Cancel();
                    break;
                case "rollback":
                    throw new AssessmentOuterRollback();
            }
        });
        var owner = ObservationOwner(fixture, http, boundary);
        if (change == "cancel")
            await Assert.That(async () => await owner.ObserveAsync(ObservationIntent(fixture), cancellation.Token))
                .Throws<OperationCanceledException>();
        else if (change == "rollback")
            await Assert.That(async () => await owner.ObserveAsync(ObservationIntent(fixture)))
                .Throws<AssessmentOuterRollback>();
        else
            await Assert.That(await owner.ObserveAsync(ObservationIntent(fixture)) is not null).IsEqualTo(change == "none");
        await Assert.That(boundary.Reached).IsTrue();
        context.TenantContext = fixture.Database;
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        context.ChangeTracker.Clear();
        await Assert.That(await context.Set<ModerationProfileCollectionSubjectObservation>().CountAsync())
            .IsEqualTo(change == "none" ? 1 : 0);
        await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.Id == fixture.LoginId
            && row.UserId == fixture.SubjectId)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("none")]
    [Arguments("superseded")]
    [Arguments("corrupt-latest")]
    [Arguments("expiry")]
    [Arguments("provider-replacement")]
    [Arguments("pii")]
    [Arguments("parent-withdrawn")]
    [Arguments("latest-unadopted-procedure")]
    [Arguments("original-source-changed")]
    public async Task SubjectObservation_LaterActorRereadsOriginalsWithoutRenewalOrInvalidLatestFallback(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var context = fixture.Context;
        var http = ObservationPrincipal(fixture, change == "provider-replacement");
        var owner = ObservationOwner(fixture, http);
        var first = (await owner.ObserveAsync(ObservationIntent(fixture)))!;
        var binding = first.ToBinding();
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        if (change is "superseded" or "corrupt-latest")
        {
            var second = (await owner.ObserveAsync(ObservationIntent(fixture)))!;
            await Assert.That(second.Generation).IsEqualTo(2L);
            await Assert.That(second.ObservationStamp).IsNotEqualTo(first.ObservationStamp);
            await Assert.That(second.ValidUntil).IsEqualTo(first.ValidUntil);
            if (change == "corrupt-latest")
                await context.Set<ModerationProfileCollectionSubjectObservation>().Where(row => row.Id == second.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OriginalProcedureDigest, new string('d', 64)));
        }
        if (change == "provider-replacement")
        {
            await context.UserExternalLogins.Where(row => row.Id == fixture.LoginId).ExecuteDeleteAsync();
            context.UserExternalLogins.Add(fixture.Login(Guid.CreateVersion7()));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        if (change == "pii")
            await context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync();
        if (change == "expiry") fixture.Clock.UtcNow = first.ValidUntil;
        if (change == "parent-withdrawn")
            await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(
                new WithdrawModerationChildAgePolicyAdoption
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = fixture.Database.Scope,
                    ExpectedQualificationRevision = 6,
                    AdoptionId = fixture.ParentAdoption.AdoptionId,
                    ExpectedAdoptionRevision = 1
                })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == "latest-unadopted-procedure")
            await Assert.That((await fixture.Writer(fixture.OwnerId).ExecuteAsync(fixture.Record with
            {
                OperationId = Guid.CreateVersion7(),
                SourceId = Guid.CreateVersion7(),
                ExpectedQualificationRevision = 6
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == "original-source-changed")
            await context.Set<ModerationProfileCollectionProcedureSource>().Where(row => row.Id == fixture.Record.SourceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Procedure, ProcedureVocabulary(true)));
        http.HttpContext!.User = IndividualPrincipal(fixture.ReviewerId);
        var read = await ReadSubjectObservationAsync(fixture, http, first.Id);
        await Assert.That(read is not null).IsEqualTo(change == "none");
        if (read is not null) await Assert.That(read.ToBinding()).IsEqualTo(binding);
        await Assert.That(first.ToBinding()).IsEqualTo(binding);
    }

    [Test]
    [Arguments("Generation")]
    [Arguments("ObservationStamp")]
    [Arguments("ObservedAt")]
    [Arguments("ValidUntil")]
    [Arguments("SubjectHumanId")]
    [Arguments("OriginalProcedureDigest")]
    public async Task SubjectObservation_MaterializesImmutableOriginalsAndCascadesWithActualPii(string property)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var context = fixture.Context;
        var original = (await ObservationOwner(fixture, ObservationPrincipal(fixture, true))
            .ObserveAsync(ObservationIntent(fixture)))!;
        var row = await context.Set<ModerationProfileCollectionSubjectObservation>().SingleAsync();
        await Assert.That(row).IsNotSameReferenceAs(original);
        row.ValidatePersistedState();
        await Assert.That(row.ToBinding()).IsEqualTo(original.ToBinding());
        context.Entry(row).Property(property).CurrentValue = property switch
        {
            "Generation" => 2L,
            "ObservationStamp" => Guid.CreateVersion7(),
            "SubjectHumanId" => fixture.OtherSubjectId,
            "OriginalProcedureDigest" => new string('d', 64),
            "ObservedAt" => Now.AddTicks(1),
            _ => original.ValidUntil.AddTicks(-1)
        };
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<InvalidOperationException>();
        context.ChangeTracker.Clear();
        await context.Set<UserPii>().Where(pii => pii.UserId == fixture.SubjectId).ExecuteDeleteAsync();
        await Assert.That(await context.Set<ModerationProfileCollectionSubjectObservation>().CountAsync()).IsEqualTo(0);
        await Assert.That(await context.Users.AnyAsync(human => human.Id == fixture.SubjectId)).IsTrue();
    }

    [Test]
    [Arguments("missing-trust")]
    [Arguments("foreign-trust")]
    [Arguments("missing-tenant")]
    [Arguments("foreign-tenant")]
    public async Task SubjectObservation_PrivateFilterRequiresExactResolvedScope(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await ObservationOwner(fixture, ObservationPrincipal(fixture, false)).ObserveAsync(ObservationIntent(fixture));
        if (change == "missing-trust") fixture.Context.ModerationTrustDomainId = null;
        if (change == "foreign-trust") fixture.Context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == "missing-tenant") fixture.Context.TenantContext = null;
        if (change == "foreign-tenant") fixture.Context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionSubjectObservation>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task SubjectObservation_RejectsNestedIdentityTransactionRatherThanDroppingFinalCallback()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await fixture.Privacy.ExecuteEnrollmentSerializedAsync(async token =>
        {
            await Assert.That(async () => await ObservationOwner(fixture, ObservationPrincipal(fixture, false))
                .ObserveAsync(ObservationIntent(fixture), token)).Throws<InvalidOperationException>();
            return true;
        }, _ => Task.CompletedTask, default);
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionSubjectObservation>().CountAsync()).IsEqualTo(0);
    }

    private static NominateModerationProfileCollectionSubjectObservation ObservationIntent(IndividualPolicyFixture fixture) =>
        new(fixture.Age.ProfileId, fixture.Age.Mode);

    private static HttpContextAccessor ObservationPrincipal(IndividualPolicyFixture fixture, bool provider) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            { User = provider ? fixture.ProviderPrincipal() : IndividualPrincipal(fixture.SubjectId) }
        };

    private static ModerationProfileCollectionSubjectObservationOwner ObservationOwner(
        IndividualPolicyFixture fixture, HttpContextAccessor http, IPrivacyIdentityFenceAuthority? privacy = null) =>
        new(fixture.Context, ObservationAdmin(fixture, http), new MachinePrincipalAccessor(http), http,
            privacy ?? fixture.Privacy, fixture.Database.Inventory, fixture.Clock);

    private static AdminContext ObservationAdmin(IndividualPolicyFixture fixture, HttpContextAccessor http) =>
        new(http, new PlatformUserRoleRepository(fixture.Context), new TenantUserRoleGrantRepository(fixture.Context),
            new OrganizationMemberRepository(fixture.Context), new GroupMemberRepository(fixture.Context),
            new UserExternalLoginRepository(fixture.Context), NullLogger<AdminContext>.Instance);

    private static async Task<ModerationProfileCollectionSubjectObservation?> ReadSubjectObservationAsync(
        IndividualPolicyFixture fixture, HttpContextAccessor http, Guid id)
    {
        ModerationProfileCollectionIndividualAcquisition? acquisition = null;
        var owner = ObservationOwner(fixture, http);
        ModerationProfileCollectionSubjectObservation? result = null;
        try
        {
            return await fixture.Privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                acquisition = await ModerationProfileCollectionIndividualAcquisition.AcquireAsync(fixture.Context,
                    ObservationAdmin(fixture, http), new MachinePrincipalAccessor(http), http, fixture.Privacy,
                    fixture.Database.Inventory, fixture.Clock, fixture.Age.ProfileId, fixture.Age.Mode, token);
                result = acquisition is null ? null : await owner.ReadCurrentAsync(acquisition, id, token);
                return result;
            }, async token =>
            {
                if (result is not null)
                {
                    var final = await owner.ReadCurrentAsync(acquisition!, id, token);
                    await Assert.That(final).IsNotNull();
                    await Assert.That(final!.ToBinding()).IsEqualTo(result.ToBinding());
                }
            }, default);
        }
        finally { acquisition?.Dispose(); }
    }
}
