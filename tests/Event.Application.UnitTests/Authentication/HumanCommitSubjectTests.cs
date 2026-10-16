using System.Security.Claims;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Authentication;

public sealed class HumanCommitSubjectTests
{
    private static readonly Guid LinkedUser = Guid.Parse("018e4e5c-7f00-7000-8000-000000000081");
    private static readonly Guid ConflictingUser = Guid.Parse("018e4e5c-7f00-7000-8000-000000000082");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const string Account = "oidc:27:https://accounts.google.com:OriginalSubject";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnresolvedOrEmptyProviderBindingCannotBecomeAHumanSubject(bool empty)
    {
        var query = new IdentityQuery((_, _) => Task.FromResult<Guid?>(empty ? Guid.Empty : null));

        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            Principal(), query, CancellationToken.None);

        await Assert.That(subject).IsNull();
    }

    [Test]
    public async Task ProviderBindingWinsOverConflictingInternalIdAndVerifiedEmail()
    {
        ResolveCurrentUserIdByIdentityRequest? observed = null;
        var query = new IdentityQuery((request, _) =>
        {
            observed = request;
            return Task.FromResult<Guid?>(request.Provider == "google" && request.ProviderId == Account
                ? LinkedUser
                : null);
        });

        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            Principal(), query, CancellationToken.None);

        await Assert.That(subject).IsNotNull();
        await Assert.That(subject!.PrincipalId).IsEqualTo(LinkedUser);
        await Assert.That(subject.AccountKey).IsNotNull();
        await Assert.That(subject.AccountKey!.ProviderKind).IsEqualTo(AuthenticationProviderKind.Google);
        await Assert.That(subject.AccountKey.Value).IsEqualTo(Account);
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.Email).IsNull();
        await Assert.That(observed.EmailVerified).IsFalse();
    }

    [Test]
    public async Task PrincipalWithoutProviderAccountRetainsTheExistingGuidClaimPrecedence()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sid", LinkedUser.ToString("D")),
            new Claim("internal_user_id", ConflictingUser.ToString("D"))
        ], "interactive"));

        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            principal, UnexpectedQuery(), CancellationToken.None);

        await Assert.That(subject).IsNotNull();
        await Assert.That(subject!.PrincipalId).IsEqualTo(LinkedUser);
        await Assert.That(subject.AccountKey).IsNull();
    }

    [Test]
    public async Task EmptyGuidFromThePrincipalClaimChainCannotBecomeAHumanSubject()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sid", Guid.Empty.ToString("D"))
        ], "interactive"));

        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            principal, UnexpectedQuery(), CancellationToken.None);

        await Assert.That(subject).IsNull();
    }

    [Test]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey)]
    [Arguments(ApiAuthenticationSchemeNames.SetupSecret)]
    [Arguments(ApiAuthenticationSchemeNames.AdmissionScanner)]
    [Arguments(ApiAuthenticationSchemeNames.ManagedControlPlane)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoBootstrap)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoSession)]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt)]
    [Arguments(ApiAuthenticationSchemeNames.LocalCredentialReplacement)]
    public async Task PurposeBoundIdentityCannotDonateAProviderBindingOrInternalHumanId(string scheme)
    {
        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            Principal(scheme), UnexpectedQuery(), CancellationToken.None);

        await Assert.That(subject).IsNull();
    }

    [Test]
    public async Task AnonymousIdentityCannotDonateAProviderBindingOrInternalHumanId()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Principal().Claims));

        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            principal, UnexpectedQuery(), CancellationToken.None);

        await Assert.That(subject).IsNull();
    }

    [Test]
    public async Task MixedHumanAndMachineIdentitiesCannotSelectAnAmbientHumanSubject()
    {
        ClaimsPrincipal principal = Principal();
        principal.AddIdentity(new ClaimsIdentity([
            new Claim("sub", LinkedUser.ToString("D"))
        ], ApiAuthenticationSchemeNames.ApiKey));

        HumanCommitSubject? subject = await HumanCommitSubject.ResolveAsync(
            principal, UnexpectedQuery(), CancellationToken.None);

        await Assert.That(subject).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MutationDuringSuspendedLookupCannotChangeCapturedAccountOrQueriedIdentity(bool addMachineIdentity)
    {
        ClaimsPrincipal principal = Principal();
        var entered = new TaskCompletionSource<ResolveCurrentUserIdByIdentityRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new IdentityQuery(async (request, cancellationToken) =>
        {
            entered.SetResult(request);
            await release.Task.WaitAsync(Timeout, cancellationToken);
            return request.Provider == "google" && request.ProviderId == Account ? LinkedUser : ConflictingUser;
        });

        Task<HumanCommitSubject?> pending = HumanCommitSubject.ResolveAsync(
            principal, query, CancellationToken.None);
        ResolveCurrentUserIdByIdentityRequest observed = await entered.Task.WaitAsync(Timeout);
        try
        {
            var identity = (ClaimsIdentity)principal.Identity!;
            foreach (Claim claim in identity.Claims.ToArray())
                identity.RemoveClaim(claim);
            identity.AddClaims([
                new Claim("sub", "ReplacementSubject"),
                new Claim("iss", "https://identity.example.test"),
                new Claim("auth_provider", "keycloak"),
                new Claim("internal_user_id", ConflictingUser.ToString("D"))
            ]);
            if (addMachineIdentity)
                principal.AddIdentity(new ClaimsIdentity(authenticationType: ApiAuthenticationSchemeNames.ApiKey));
        }
        finally
        {
            release.TrySetResult();
        }
        HumanCommitSubject? subject = await pending.WaitAsync(Timeout);

        await Assert.That(observed.Provider).IsEqualTo("google");
        await Assert.That(observed.ProviderId).IsEqualTo(Account);
        await Assert.That(observed.Email).IsNull();
        await Assert.That(observed.EmailVerified).IsFalse();
        await Assert.That(subject).IsNotNull();
        await Assert.That(subject!.PrincipalId).IsEqualTo(LinkedUser);
        await Assert.That(subject.AccountKey).IsNotNull();
        await Assert.That(subject.AccountKey!.ProviderKind).IsEqualTo(AuthenticationProviderKind.Google);
        await Assert.That(subject.AccountKey.Value).IsEqualTo(Account);
    }

    private static ClaimsPrincipal Principal(string scheme = "interactive") =>
        new(new ClaimsIdentity([
            new Claim("sub", "OriginalSubject"),
            new Claim("iss", "https://accounts.google.com"),
            new Claim("auth_provider", "google"),
            new Claim("internal_user_id", ConflictingUser.ToString("D")),
            new Claim("email", "other-account@example.invalid"),
            new Claim("email_verified", "true")
        ], scheme));

    private static IdentityQuery UnexpectedQuery() =>
        new((_, _) => throw new InvalidOperationException("This principal must not perform a provider lookup."));

    private sealed class IdentityQuery(
        Func<ResolveCurrentUserIdByIdentityRequest, CancellationToken, Task<Guid?>> resolve)
        : IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>
    {
        public Task<Guid?> QueryAsync(
            ResolveCurrentUserIdByIdentityRequest query,
            CancellationToken cancellationToken = default) => resolve(query, cancellationToken);
    }
}
