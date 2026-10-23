using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Hybrid;
using Explore.Persistence.Repositories;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.DTOs.User;
using Explore.Domain;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileCollection_ReadinessReturnsOnlyReacquiredSelfNominations(bool requiresConsent)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false,
            requiresConsent ? [ModerationCollectionPrerequisiteKind.SubjectConsent] : []);
        var consent = requiresConsent ? await ConsentOwner(fixture, http).GrantAsync(nomination) : null;
        var owner = AdmissionOwner(fixture, http);
        var options = await owner.ReadOptionsAsync(fixture.SubjectId);
        await Assert.That(options.Count).IsEqualTo(1);
        await Assert.That(options[0].Assessment).IsEqualTo(nomination.Assessment);
        await Assert.That(options[0].ConsentId).IsEqualTo(consent?.Id);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        // A nomination captured by the editor is not a permit after original authority ends.
        fixture.Clock.UtcNow = options[0].ValidUntil;
        var consumed = false;
        await Assert.That(await owner.ExecuteAsync(options[0].Assessment, options[0].ConsentId, (_, _) =>
        { consumed = true; return Task.CompletedTask; })).IsFalse();
        await Assert.That(consumed).IsFalse();
    }

    [Test]
    public async Task ProfileCollection_ReadinessCannotNominateAnotherHuman()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, _) = await ConsentOriginalAsync(fixture, false, []);
        await Assert.That(async () => await AdmissionOwner(fixture, http).ReadOptionsAsync(fixture.OtherSubjectId))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileCollection_AuthenticatesEveryRequiredPrerequisiteBeforeInvokingTheBody(bool requiresConsent)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false,
            requiresConsent ? [ModerationCollectionPrerequisiteKind.SubjectConsent] : []);
        var consent = requiresConsent ? await ConsentOwner(fixture, http).GrantAsync(nomination) : null;
        var owner = AdmissionOwner(fixture, http);
        var invoked = false;
        var accepted = await owner.ExecuteAsync(nomination.Assessment, consent?.Id, async (admission, token) =>
        {
            invoked = true;
            await Assert.That(admission.SubjectId).IsEqualTo(fixture.SubjectId);
            await Assert.That(admission.ValidUntil <= nomination.ValidUntil).IsTrue();
            await owner.RequireCurrentAsync(fixture.SubjectId, token);
            using var services = ProfileCache();
            var user = await fixture.Context.Users.SingleAsync(row => row.Id == fixture.SubjectId, token);
            var handler = ProfileHandler(fixture, owner, services.GetRequiredService<HybridCache>());
            await Assert.That((await handler.ExecuteAsync(new()
            {
                UserId = user.Id, ExpectedConcurrencyStamp = user.ConcurrencyStamp,
                UpdateUserDto = new() { Names = new() { FirstName = "Admitted", LastName = "Profile" } }
            }, token)).IsSuccess).IsTrue();
        });
        await Assert.That(accepted).IsTrue();
        await Assert.That(invoked).IsTrue();
        await Assert.That(await fixture.Context.Users.Where(row => row.Id == fixture.SubjectId)
            .Select(row => row.FirstName).SingleAsync()).IsEqualTo("Admitted");
        await Assert.That(async () => await owner.RequireCurrentAsync(fixture.SubjectId))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("missing-consent")]
    [Arguments("withdrawn-consent")]
    [Arguments("eligibility")]
    [Arguments("guardian")]
    [Arguments("foreign-subject")]
    public async Task ProfileCollection_MissingUnsupportedOrForeignAuthorityNeverInvokesTheBody(string failure)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var required = failure switch
        {
            "eligibility" => new[] { ModerationCollectionPrerequisiteKind.AccountEligibility },
            "guardian" => [ModerationCollectionPrerequisiteKind.GuardianConsent],
            "foreign-subject" => [],
            _ => [ModerationCollectionPrerequisiteKind.SubjectConsent]
        };
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, required);
        var consent = failure == "withdrawn-consent" ? await ConsentOwner(fixture, http).GrantAsync(nomination) : null;
        if (consent is not null)
            await ConsentOwner(fixture, http).WithdrawAsync(ConsentWithdrawal(consent));
        if (failure == "foreign-subject") http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId);
        var invoked = false;
        var accepted = await AdmissionOwner(fixture, http).ExecuteAsync(nomination.Assessment, consent?.Id, (_, _) =>
        { invoked = true; return Task.CompletedTask; });
        await Assert.That(accepted).IsFalse();
        await Assert.That(invoked).IsFalse();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("consent")]
    [Arguments("principal")]
    public async Task ProfileCollection_ActualOuterCommitRejectsChangedAuthorityAndRollsBackTheWrite(string change)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false,
            [ModerationCollectionPrerequisiteKind.SubjectConsent]);
        var consent = (await ConsentOwner(fixture, http).GrantAsync(nomination))!;
        var original = await fixture.Context.Users.Where(row => row.Id == fixture.SubjectId)
            .Select(row => row.FirstName).SingleAsync();
        var observedOwner = false;
        var accepted = await AdmissionOwner(fixture, http).ExecuteAsync(nomination.Assessment, consent.Id,
            async (admission, token) =>
            {
                observedOwner = fixture.Context.IdentityFenceOwnsTransaction;
                var user = await fixture.Context.Users.SingleAsync(row => row.Id == fixture.SubjectId, token);
                user.FirstName = "Must roll back";
                await fixture.Context.SaveChangesAsync(token);
                if (change == "expiry") fixture.Clock.UtcNow = admission.ValidUntil;
                else if (change == "principal") http.HttpContext!.User = IndividualPrincipal(fixture.OtherSubjectId);
                else await fixture.Context.Set<Explore.Domain.ModerationProfileSubjectConsent>()
                    .Where(row => row.Id == consent.Id).ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.Revision, 2L).SetProperty(row => row.WithdrawnAt, Now), token);
            });
        await Assert.That(accepted).IsFalse();
        await Assert.That(observedOwner).IsTrue();
        await Assert.That(await fixture.Context.Users.Where(row => row.Id == fixture.SubjectId)
            .Select(row => row.FirstName).SingleAsync()).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileCollection_DeadlineIsUnavailableButCallerCancellationStillPropagates(bool callerCancellation)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var clock = new ImportExportOwnerClock { Current = Now };
        var owner = AdmissionOwner(fixture, http, clock);
        using var caller = new CancellationTokenSource();
        var original = await fixture.Context.Users.AsNoTracking().Where(row => row.Id == fixture.SubjectId)
            .Select(row => row.FirstName).SingleAsync();
        async Task<bool> CollectAsync() => await owner.ExecuteAsync(nomination.Assessment, null, async (admission, token) =>
        {
            var user = await fixture.Context.Users.SingleAsync(row => row.Id == fixture.SubjectId, token);
            user.FirstName = "Must roll back on cancellation";
            await fixture.Context.SaveChangesAsync(token);
            if (callerCancellation) caller.Cancel();
            else clock.Current = admission.ValidUntil;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, caller.Token);
        if (callerCancellation) await Assert.That(CollectAsync).Throws<OperationCanceledException>();
        else await Assert.That(await CollectAsync()).IsFalse();
        await Assert.That(await fixture.Context.Users.AsNoTracking().Where(row => row.Id == fixture.SubjectId)
            .Select(row => row.FirstName).SingleAsync()).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(1)]
    [Arguments(60)]
    public async Task ProfileCollection_DeadlineRetainsItsOriginalExpiryAcrossLongTimerWindows(int days)
    {
        var clock = new ImportExportOwnerClock { Current = Now };
        using var deadline = new ModerationProfileCollectionDeadline(clock, Now.AddDays(days));
        clock.Current = Now.AddDays(days / 2.0);
        await Assert.That(deadline.Token.IsCancellationRequested).IsFalse();
        clock.Current = Now.AddDays(days);
        await Assert.That(deadline.Token.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task ProfileCollection_ExpiryAtEveryInitialClockCheckpointRejectsBeforeTheConsumer()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false,
            [ModerationCollectionPrerequisiteKind.SubjectConsent]);
        var consent = (await ConsentOwner(fixture, http).GrantAsync(nomination))!;
        var probe = new AdmissionExpiryClock(Now, consent.ValidUntil, int.MaxValue);
        var readsBeforeBody = 0;
        await Assert.That(await AdmissionOwner(fixture, http, probe).ExecuteAsync(
            nomination.Assessment, consent.Id, (_, _) =>
            { readsBeforeBody = probe.Reads; return Task.CompletedTask; })).IsTrue();
        await Assert.That(readsBeforeBody > 0).IsTrue();
        for (var checkpoint = 1; checkpoint <= readsBeforeBody; checkpoint++)
        {
            var invoked = false;
            var clock = new AdmissionExpiryClock(Now, consent.ValidUntil, checkpoint);
            try
            {
                var admitted = await AdmissionOwner(fixture, http, clock).ExecuteAsync(
                    nomination.Assessment, consent.Id, (_, _) =>
                    { invoked = true; return Task.CompletedTask; });
                await Assert.That(admitted).IsFalse();
            }
            catch (OperationCanceledException) { }
            await Assert.That(invoked).IsFalse();
            await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        }
    }

    private sealed class AdmissionExpiryClock(DateTimeOffset initial, DateTimeOffset expired, int checkpoint) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => ++Reads >= checkpoint ? expired : initial;
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ProfileCollection_NameMutationCannotManufactureIdentityOrRestoreCallerSuppliedLifecycle(bool supported)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var user = await fixture.Context.Users.Include(row => row.Pii)
            .SingleAsync(row => row.Id == fixture.SubjectId);
        user.Email = "shared-contact@example.test";
        user.EmailVerified = false;
        var claim = UserIdentityEmailClaim.Create(user.Id, "authority@example.test");
        var proof = UserIdentityEmailEvidence.Create(user.Id, claim.Id, fixture.LoginId, Now.UtcDateTime);
        if (!supported) proof.Invalidate();
        claim.Evidence = [proof];
        fixture.Context.Add(claim);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var owner = AdmissionOwner(fixture, http);
        using var services = ProfileCache();
        await Assert.That(await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
        {
            var current = await fixture.Context.Users.SingleAsync(row => row.Id == fixture.SubjectId, token);
            var input = System.Text.Json.JsonSerializer.Deserialize<UpdateUserDto>("""
                {"Names":{"FirstName":"Chosen","LastName":"Family","Email":"forged@example.test","EmailVerified":true},
                 "Email":"forged@example.test","EmailVerified":true,"IsDeleted":true,"IsInstanceAdmin":true}
                """)!;
            await Assert.That((await ProfileHandler(fixture, owner, services.GetRequiredService<HybridCache>())
                .ExecuteAsync(new() { UserId = current.Id, ExpectedConcurrencyStamp = current.ConcurrencyStamp,
                    UpdateUserDto = input }, token)).IsSuccess).IsTrue();
        })).IsTrue();
        var saved = await fixture.Context.Users.Include(row => row.Pii)
            .Include(row => row.IdentityEmailClaims).ThenInclude(row => row.Evidence)
            .SingleAsync(row => row.Id == fixture.SubjectId);
        await Assert.That(saved.FirstName).IsEqualTo("Chosen");
        await Assert.That(saved.Email).IsEqualTo("shared-contact@example.test");
        await Assert.That(saved.EmailVerified).IsFalse();
        await Assert.That(saved.IsDeleted).IsFalse();
        var recipient = Explore.Application.Notifications.RecipientEmailAddressResolver.Resolve(saved, saved.Id);
        await Assert.That(recipient.HasVerifiedEmail).IsEqualTo(supported);
        await Assert.That(recipient.Email).IsEqualTo(supported ? "authority@example.test" : null);
    }

    [Test]
    [Arguments("stale")]
    [Arguments("invalid")]
    public async Task ProfileCollection_NameValidationAndConcurrencyRemainInsideAdmission(string failure)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, nomination) = await ConsentOriginalAsync(fixture, false, []);
        var original = await fixture.Context.Users.Where(row => row.Id == fixture.SubjectId)
            .Select(row => row.FirstName).SingleAsync();
        var owner = AdmissionOwner(fixture, http);
        using var services = ProfileCache();
        async Task RunAsync() => await owner.ExecuteAsync(nomination.Assessment, null, async (_, token) =>
        {
            var user = await fixture.Context.Users.SingleAsync(row => row.Id == fixture.SubjectId, token);
            var result = await ProfileHandler(fixture, owner, services.GetRequiredService<HybridCache>()).ExecuteAsync(new()
            {
                UserId = user.Id, ExpectedConcurrencyStamp = failure == "stale" ? Guid.Empty : user.ConcurrencyStamp,
                UpdateUserDto = new() { Names = new() { FirstName = failure == "invalid" ? "" : "Changed", LastName = "Family" } }
            }, token);
            await Assert.That(result.IsSuccess).IsFalse();
        });
        if (failure == "stale")
            await Assert.That(RunAsync).Throws<Explore.Application.Exceptions.ConcurrencyConflictException>();
        else await RunAsync();
        await Assert.That(await fixture.Context.Users.Where(row => row.Id == fixture.SubjectId)
            .Select(row => row.FirstName).SingleAsync()).IsEqualTo(original);
    }

    private static ServiceProvider ProfileCache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private static UpdateUserCommandHandler ProfileHandler(IndividualPolicyFixture fixture,
        ModerationProfileCollectionAdmissionOwner owner, HybridCache cache) => new(
        new UserRepository(fixture.Context), new PrivacyErasureStateRepository(fixture.Context), cache, owner);

    [Test]
    public async Task ProfileCollection_DirectNameMutationCannotReachRepositoriesWithoutNativeAdmission()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var (http, _, _, _) = await ConsentOriginalAsync(fixture, false, []);
        var handler = new Explore.Application.Features.Users.Handlers.Commands.UpdateUserCommandHandler(
            null!, null!, null!, AdmissionOwner(fixture, http));
        await Assert.That(async () => await handler.ExecuteAsync(new()
        {
            UserId = fixture.SubjectId,
            ExpectedConcurrencyStamp = Guid.CreateVersion7(),
            UpdateUserDto = new() { Names = new() { FirstName = "Unadmitted", LastName = "Caller" } }
        })).Throws<Explore.Application.Exceptions.AuthorizationException>();
    }

    private static ModerationProfileCollectionAdmissionOwner AdmissionOwner(
        IndividualPolicyFixture fixture, Microsoft.AspNetCore.Http.HttpContextAccessor http, TimeProvider? clock = null) =>
        new(fixture.Context, ObservationAdmin(fixture, http),
            new Explore.Infrastructure.Identity.MachinePrincipalAccessor(http), http,
            fixture.Privacy, fixture.Database.Inventory, clock ?? fixture.Clock);
}
