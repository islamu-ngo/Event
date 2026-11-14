using Explore.Application.Authentication;
using Explore.Application.Features.Authentication.Continuation;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Authentication.Continuation;

public sealed partial class ResolveVerifiedAccountContinuationTests
{
    [Test]
    public async Task UnlinkedAccountCannotBorrowAnotherProviderNamespace()
    {
        var store = new Store();
        store.Login.AuthenticationProviderId = (int)AuthenticationProviderKind.Google;

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unlinked);
    }

    [Test]
    public async Task UnlinkedAccountCannotBorrowAnotherIssuer()
    {
        var store = new Store();
        store.Login.ProviderKey = PlatformIdentityPrincipalExtensions
            .CreateOidcAccountKey("https://other.example/realm", "same-subject").Value;

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unlinked);
    }

    [Test]
    public async Task MissingBindingReturnsUnlinkedWithoutEnrollment()
    {
        var store = new Store { HasLogin = false };

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unlinked);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeletedOrErasedUserCannotProduceDestination(bool erased)
    {
        var store = new Store();
        if (erased) store.User = null;
        else store.User!.IsDeleted = true;
        store.Rows.Add(store.Member("remaining-membership", 1));

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unavailable);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task InvalidBindingOrMismatchedUserCannotProduceDestination(int invalid)
    {
        var store = new Store();
        switch (invalid)
        {
            case 0: store.Login.Id = Guid.Empty; break;
            case 1: store.Login.UserId = Guid.Empty; break;
            case 2: store.User!.Id = Guid.CreateVersion7(); break;
            case 3:
                store.Login.ProviderKey = store.Login.ProviderKey.ToUpperInvariant();
                store.CaseInsensitiveLookup = true;
                break;
        }

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unavailable);
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Google, "retired")]
    [Arguments(AuthenticationProviderKind.Development, "development")]
    [Arguments(AuthenticationProviderKind.Local, "invalid-subject")]
    [Arguments(AuthenticationProviderKind.Local, "00000000-0000-0000-0000-000000000000")]
    [Arguments(AuthenticationProviderKind.Atproto, "did:deleted:erased")]
    [Arguments(AuthenticationProviderKind.Atproto, "invalid-did")]
    public async Task UnsupportedOrInvalidSubjectNeverBecomesUnlinkedEnrollment(
        AuthenticationProviderKind provider, string subject)
    {
        var store = new Store { HasLogin = false };

        var result = await store.Handler.QueryAsync(
            new(new ProviderAccountKey(provider, subject)));

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unavailable);
    }

    [Test]
    public async Task LocalBindingMustMatchCanonicalGlobalSubject()
    {
        var store = new Store();
        var key = new ProviderAccountKey(AuthenticationProviderKind.Local, Guid.CreateVersion7().ToString("D"));
        store.Key = key;
        store.Login.AuthenticationProviderId = (int)AuthenticationProviderKind.Local;
        store.Login.ProviderKey = key.Value;

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unavailable);
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Local)]
    [Arguments(AuthenticationProviderKind.Keycloak)]
    [Arguments(AuthenticationProviderKind.Atproto)]
    public async Task ExistingLinkedAccountContinuesWithoutRegistrationAdmission(AuthenticationProviderKind provider)
    {
        var store = new Store();
        store.Key = provider switch
        {
            AuthenticationProviderKind.Local => new(provider, store.User!.Id.ToString("D")),
            AuthenticationProviderKind.Atproto => new(provider, "did:plc:existing-account"),
            _ => store.Key
        };
        store.Login.AuthenticationProviderId = (int)provider;
        store.Login.ProviderKey = store.Key.Value;
        var member = store.Member("existing-community", 1);
        store.Rows.Add(member);

        var result = await store.Resolve();

        await Assert.That(result.Outcome).IsEqualTo(VerifiedAccountContinuationOutcome.Destination);
        await Assert.That(result.TenantId).IsEqualTo(member.TenantId);
        await Assert.That(result.TenantSlug).IsEqualTo("existing-community");
        await Assert.That(result.HasMultipleTenants).IsFalse();
    }

    [Test]
    public async Task NoActiveMembershipDoesNotInventDestination()
    {
        var store = new Store();
        store.User!.LastActiveTenantId = Guid.CreateVersion7();

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.NoMembership);
    }

    [Test]
    public async Task EmptyExplicitTargetIsUnavailableInsteadOfUntargeted()
    {
        var store = new Store();
        store.Rows.Add(store.Member("valid", 1));

        var result = await store.Resolve(Guid.Empty);

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unavailable);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LiveAffinityWinsOtherwiseLatestNativeMembershipWins(bool liveAffinity)
    {
        var store = new Store();
        var older = store.Member("older", 1);
        older.CreatedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var latest = store.Member("latest", 3);
        latest.JoinedAt = null; // Native fallback uses CreatedAt when JoinedAt is absent.
        store.Rows.AddRange([older, latest, store.Member("middle", 2)]);
        store.User!.LastActiveTenantId = liveAffinity ? older.TenantId : Guid.CreateVersion7();

        var result = await store.Resolve();

        await Assert.That(result.TenantId).IsEqualTo(liveAffinity ? older.TenantId : latest.TenantId);
        await Assert.That(result.TenantSlug).IsEqualTo(liveAffinity ? "older" : "latest");
        await Assert.That(result.HasMultipleTenants).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactTargetWinsAndMissingTargetNeverFallsBackToAffinity(bool targetMember)
    {
        var store = new Store();
        var preferred = store.Member("preferred", 5);
        var target = store.Member("target", 1);
        store.User!.LastActiveTenantId = preferred.TenantId;
        store.Rows.Add(preferred);
        if (targetMember) store.Rows.Add(target);

        var result = await store.Resolve(target.TenantId);

        if (targetMember)
        {
            await Assert.That(result.TenantId).IsEqualTo(target.TenantId);
            await Assert.That(result.TenantSlug).IsEqualTo("target");
            await Assert.That(result.HasMultipleTenants).IsTrue();
        }
        else await AssertBounded(result, VerifiedAccountContinuationOutcome.NoMembership);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task IneligibleRowsCannotWinAffinityOrLatestFallback(int invalid)
    {
        var store = new Store();
        var valid = store.Member("valid", 1);
        var rejected = store.Member("rejected", 5);
        switch (invalid)
        {
            case 0: rejected.IsDeleted = true; break;
            case 1: rejected.StatusId = 0; break;
            case 2: rejected.Tenant.TenantStatusId = 0; break;
            case 3: rejected.UserId = Guid.CreateVersion7(); break;
            case 4: rejected.Tenant.Id = Guid.CreateVersion7(); break;
            case 5: rejected.Tenant.Slug = ""; break;
        }
        store.Rows.AddRange([rejected, valid]);
        store.User!.LastActiveTenantId = rejected.TenantId;

        var result = await store.Resolve();

        await Assert.That(result.TenantId).IsEqualTo(valid.TenantId);
        await Assert.That(result.HasMultipleTenants).IsFalse();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task LookupFailureReturnsUnavailableInsteadOfEmptyMembership(int stage)
    {
        var store = new Store { FailureStage = stage };

        var result = await store.Resolve();

        await AssertBounded(result, VerifiedAccountContinuationOutcome.Unavailable);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task CallerCancellationIsNeverConvertedToContinuation(int stage)
    {
        using var cancellation = new CancellationTokenSource();
        var store = new Store { CancelStage = stage, Cancellation = cancellation };
        store.Rows.Add(store.Member("valid", 1));
        if (stage == 0) cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.Resolve(token: cancellation.Token));
    }

    private static async Task AssertBounded(
        VerifiedAccountContinuationResult result, VerifiedAccountContinuationOutcome expected)
    {
        await Assert.That(result.Outcome).IsEqualTo(expected);
        await Assert.That(result.TenantId).IsNull();
        await Assert.That(result.TenantSlug).IsNull();
        await Assert.That(result.HasMultipleTenants).IsFalse();
    }

}
