using System.Security.Claims;
using Explore.Application;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Features.Users;

public sealed class IdentityResolutionQueryTests
{
    private static readonly Guid LinkedUser = Guid.Parse("018e4e5c-7f00-7000-8000-000000000081");
    private static readonly Guid ConflictingUser = Guid.Parse("018e4e5c-7f00-7000-8000-000000000082");
    private const string Account = "oidc:27:https://accounts.google.com:Subject";

    [Test]
    public async Task ProviderBindingWinsOverConflictingInternalIdAndVerifiedEmail()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        await Assert.That(await query.ResolveCurrentUserIdAsync(Principal())).IsEqualTo(LinkedUser);
    }

    [Test]
    [Arguments("Subject", "https://other.example.test", "google")]
    [Arguments("subject", "https://accounts.google.com", "google")]
    [Arguments("Subject", "https://accounts.google.com", "keycloak")]
    [Arguments("Unlinked", "https://accounts.google.com", "google")]
    public async Task UnlinkedOrDifferentAuthorityNeverFallsBackToInternalIdOrEmail(string subject, string issuer, string authority)
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        await Assert.That(await query.ResolveCurrentUserIdAsync(Principal(subject, issuer, authority))).IsNull();
    }

    [Test]
    public async Task AnonymousPurposeBoundAndAmbiguousPrincipalsRemainUnresolved()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        await Assert.That(await query.ResolveCurrentUserIdAsync(new ClaimsPrincipal(new ClaimsIdentity(Principal().Claims)))).IsNull();
        await Assert.That(await query.ResolveCurrentUserIdAsync(new ClaimsPrincipal(new ClaimsIdentity(Principal().Claims, "ApiKey")))).IsNull();
        await Assert.That(await query.ResolveCurrentUserIdAsync(new ClaimsPrincipal(new[] { (ClaimsIdentity)Principal().Identity!, new ClaimsIdentity([], "second") }))).IsNull();
    }

    [Test]
    public async Task WithoutProviderIdentityTheExistingPrincipalChainRemainsAuthoritative()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sid", LinkedUser.ToString("D")),
            new Claim("internal_user_id", ConflictingUser.ToString("D"))], "interactive"));
        await Assert.That(await query.ResolveCurrentUserIdAsync(principal)).IsEqualTo(LinkedUser);
    }

    private static ClaimsPrincipal Principal(string subject = "Subject", string issuer = "https://accounts.google.com", string authority = "google") =>
        new(new ClaimsIdentity([
            new Claim("sub", subject), new Claim("iss", issuer), new Claim("auth_provider", authority),
            new Claim("internal_user_id", ConflictingUser.ToString("D")),
            new Claim("email", "other-account@example.invalid"), new Claim("email_verified", "true")], "interactive"));

    [Test]
    public async Task ScopedBindingsAreIndependentAndRepeatedQueriesObserveTheirOwnStore()
    {
        using var provider = Services();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstQuery = first.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        var secondQuery = second.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        var firstStore = first.ServiceProvider.GetRequiredService<IUserExternalLoginRepository>();
        var login = (await firstStore.GetByUser(LinkedUser)).Single();
        await firstStore.Delete(login);
        await Assert.That(await firstQuery.ResolveCurrentUserIdAsync(Principal())).IsNull();
        await Assert.That(await secondQuery.ResolveCurrentUserIdAsync(Principal())).IsEqualTo(LinkedUser);
    }

    [Test]
    public async Task PreCancelledTokenRetainsExistingTokenlessRepositoryBehavior()
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        // The existing repository contract has no token. The cutover must not add an early cancellation check.
        await Assert.That(await query.ResolveCurrentUserIdAsync(Principal(), cancellation.Token)).IsEqualTo(LinkedUser);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PendingProviderLookupPropagatesFailureAndCancellation(bool cancelled)
    {
        using var provider = Services();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<UserExternalLogin?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = (LoginStore)scope.ServiceProvider.GetRequiredService<IUserExternalLoginRepository>();
        store.PendingLookup = completion.Task;
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        Task<Guid?> pending = query.ResolveCurrentUserIdAsync(Principal(), cancellation.Token);
        await Assert.That(pending.IsCompleted).IsFalse();
        var failure = new InvalidOperationException("Provider storage unavailable.");
        if (cancelled)
        {
            cancellation.Cancel();
            completion.SetCanceled(cancellation.Token);
        }
        else
            completion.SetException(failure);

        Exception? observed = null;
        try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception exception) { observed = exception; }
        if (cancelled)
        {
            await Assert.That(observed).IsTypeOf<TaskCanceledException>();
            await Assert.That(((TaskCanceledException)observed!).CancellationToken).IsEqualTo(cancellation.Token);
        }
        else
            await Assert.That(observed).IsSameReferenceAs(failure);
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IUserExternalLoginRepository>(_ => new LoginStore());
        services.AddSingleton<IAuthorizationProvider, UnexpectedPolicy>();
        services.AddNativeOperations();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class UnexpectedPolicy : IAuthorizationProvider
    {
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Identity resolution must not ask the PDP for new authority.");
        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Identity resolution must not ask the PDP for new authority.");
    }

    private sealed class LoginStore : IUserExternalLoginRepository
    {
        private readonly List<UserExternalLogin> _logins = [new()
        {
            UserId = LinkedUser, User = null!, AuthenticationProvider = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Google, ProviderKey = Account
        }];
        public Task<UserExternalLogin?>? PendingLookup { get; set; }
        public Task<UserExternalLogin?> GetByProviderAndKey(ProviderAccountKey key) => PendingLookup ?? Task.FromResult(_logins.SingleOrDefault(login =>
            login.AuthenticationProviderId == (int)key.ProviderKind && string.Equals(login.ProviderKey, key.Value, StringComparison.Ordinal)));
        public Task<List<UserExternalLogin>> GetByUser(Guid userId) => Task.FromResult(_logins.Where(login => login.UserId == userId).ToList());
        public Task<UserExternalLogin?> GetById(Guid id) => Task.FromResult(_logins.SingleOrDefault(login => login.Id == id));
        public Task<IReadOnlyList<UserExternalLogin>> GetAll() => Task.FromResult<IReadOnlyList<UserExternalLogin>>(_logins.ToArray());
        public Task<(IReadOnlyList<UserExternalLogin> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) =>
            Task.FromResult<(IReadOnlyList<UserExternalLogin>, int)>((_logins.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray(), _logins.Count));
        public Task<bool> Exists(Guid id) => Task.FromResult(_logins.Any(login => login.Id == id));
        public Task<UserExternalLogin> Create(UserExternalLogin entity) { _logins.Add(entity); return Task.FromResult(entity); }
        public Task Update(UserExternalLogin entity) => throw new NotSupportedException();
        public Task Delete(UserExternalLogin entity) { _logins.Remove(entity); return Task.CompletedTask; }
    }
}
