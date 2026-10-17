using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments("clone")]
    [Arguments("replacement")]
    [Arguments("second-authenticated")]
    [Arguments("owner")]
    [Arguments("scope")]
    [Arguments("canonical-user")]
    [Arguments("key-user")]
    [Arguments("duplicate-key-user")]
    [Arguments("provider")]
    public async Task AuthenticationProofRejectsChangedPrincipalIdentity(string mutation)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var key = await AuthenticateNativeMachine(scope, db, data.OwnerId, ExternalApiKeyOwnerType.User);
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        var proof = scope.ServiceProvider.GetRequiredService<IPersistedApiKeyProofAccessor>();
        await Assert.That(proof.CurrentProof?.RowId).IsEqualTo(key.Id);
        var identity = (ClaimsIdentity)http.User.Identity!;
        switch (mutation)
        {
            case "clone":
                http.User = new ClaimsPrincipal(identity.Clone());
                break;
            case "replacement":
                http.User = new ClaimsPrincipal(new ClaimsIdentity(identity.Claims, ApiAuthenticationSchemeNames.ApiKey));
                break;
            case "second-authenticated":
                http.User.AddIdentity(new ClaimsIdentity([], "SecondAuthentication"));
                break;
            case "owner":
                identity.RemoveClaim(identity.FindFirst(ApiAuthenticationClaimTypes.OwnerId)!);
                identity.AddClaim(new Claim(ApiAuthenticationClaimTypes.OwnerId, data.OutsiderId.ToString("D")));
                break;
            case "scope":
                foreach (var claim in identity.FindAll(ApiAuthenticationClaimTypes.Scope).ToArray())
                    identity.RemoveClaim(claim);
                identity.AddClaim(new Claim(ApiAuthenticationClaimTypes.Scope, ExternalApiKeyScopes.EventsRead));
                break;
            case "canonical-user":
                identity.AddClaim(new Claim("sub", data.OutsiderId.ToString("D")));
                break;
            case "key-user":
                identity.RemoveClaim(identity.FindFirst(PlatformIdentityClaimTypes.InternalUserId)!);
                identity.AddClaim(new Claim(PlatformIdentityClaimTypes.InternalUserId, data.OutsiderId.ToString("D")));
                break;
            case "duplicate-key-user":
                identity.AddClaim(new Claim(PlatformIdentityClaimTypes.InternalUserId, data.OwnerId.ToString("D")));
                break;
            case "provider":
                identity.AddClaim(new Claim("auth_provider", "local"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        await Assert.That(proof.CurrentProof).IsNull();
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
            token => scope.ServiceProvider.GetRequiredService<IMachineMutationCommitAuthority>()
                .GetAuthorizedSessionParentAsync(db.TenantContext!.TenantId, data.PublicId, AuthorizationActions.Update, token), default);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AuthenticationProofAllowsUnauthenticatedEnrichmentWithoutChangingItsSnapshot()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var key = await AuthenticateNativeMachine(scope, db, data.OwnerId, ExternalApiKeyOwnerType.User);
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        http.User.AddIdentity(new ClaimsIdentity([new Claim("locale", "en")]));
        key.Scopes = ExternalApiKeyScopes.EventsRead;
        key.OwnerId = data.OutsiderId;

        var proof = scope.ServiceProvider.GetRequiredService<IPersistedApiKeyProofAccessor>().CurrentProof;
        await Assert.That(proof).IsNotNull();
        await Assert.That(proof!.OwnerId).IsEqualTo(data.OwnerId);
        await Assert.That(proof.Scopes.Contains(ExternalApiKeyScopes.EventsWrite)).IsTrue();
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
            token => scope.ServiceProvider.GetRequiredService<IMachineMutationCommitAuthority>()
                .GetAuthorizedSessionParentAsync(db.TenantContext!.TenantId, data.PublicId, AuthorizationActions.Update, token), default);
        await Assert.That(result?.Id).IsEqualTo(data.PublicId);
    }

    [Test]
    public async Task ConfiguredCredentialCannotSubstituteACollidingPersistedKey()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var key = await AuthenticateNativeMachine(scope, db, data.OwnerId, ExternalApiKeyOwnerType.User, configured: true);

        await Assert.That(scope.ServiceProvider.GetRequiredService<IMachinePrincipalAccessor>().Current?.KeyId).IsEqualTo(key.KeyId);
        await Assert.That(scope.ServiceProvider.GetRequiredService<IPersistedApiKeyProofAccessor>().CurrentProof).IsNull();
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
            token => scope.ServiceProvider.GetRequiredService<IMachineMutationCommitAuthority>()
                .GetAuthorizedSessionParentAsync(db.TenantContext!.TenantId, data.PublicId, AuthorizationActions.Update, token), default);
        await Assert.That(result).IsNull();
        await Assert.That(await db.ExternalApiKeys.AsNoTracking().AnyAsync(row => row.Id == key.Id)).IsTrue();
    }

    [Test]
    public async Task InternalExecutionCannotBorrowHttpProofAndClearRestoresOnlyOriginalAuthentication()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var key = await AuthenticateNativeMachine(scope, db, data.OwnerId, ExternalApiKeyOwnerType.User);
        var machine = scope.ServiceProvider.GetRequiredService<IMachinePrincipalAccessor>();
        var execution = scope.ServiceProvider.GetRequiredService<IMachinePrincipalExecutionAccessor>();
        var proofs = scope.ServiceProvider.GetRequiredService<IPersistedApiKeyProofAccessor>();
        execution.SetPrincipal(machine.Current!);
        await Assert.That(proofs.CurrentProof).IsNull();
        var rejected = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
            token => scope.ServiceProvider.GetRequiredService<IMachineMutationCommitAuthority>()
                .GetAuthorizedSessionParentAsync(db.TenantContext!.TenantId, data.PublicId, AuthorizationActions.Update, token), default);
        await Assert.That(rejected).IsNull();

        execution.Clear();

        await Assert.That(proofs.CurrentProof?.RowId).IsEqualTo(key.Id);
        var restored = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
            token => scope.ServiceProvider.GetRequiredService<IMachineMutationCommitAuthority>()
                .GetAuthorizedSessionParentAsync(db.TenantContext!.TenantId, data.PublicId, AuthorizationActions.Update, token), default);
        await Assert.That(restored?.Id).IsEqualTo(data.PublicId);
    }

    private static async Task<ExternalApiKey> AuthenticateNativeMachine(
        IServiceScope scope, ExploreDbContext db, Guid ownerId, ExternalApiKeyOwnerType ownerType,
        bool configured = false, string scopes = ExternalApiKeyScopes.EventsWrite, DateTime? expires = null)
    {
        var key = ManagedKey(ownerType == ExternalApiKeyOwnerType.InstanceAdmin ? null : db.TenantContext!.TenantId,
            ownerId, ownerType);
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        key.SecretHash = ApiKeyHashing.ComputeHash(secret);
        key.Scopes = scopes;
        key.ExpiresAt = expires;
        db.ExternalApiKeys.Add(key);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
            .Get(ApiAuthenticationSchemeNames.ApiKey);
        if (configured)
        {
            options.Clients =
            [
                new ApiKeyClientDescriptor
                {
                    KeyId = key.KeyId, TenantId = db.TenantContext!.TenantId,
                    OwnerType = ownerType.ToString(), OwnerId = ownerId.ToString("D"),
                    Scopes = scopes.Split(' ').ToList(), SecretHash = ApiKeyHashing.ComputeHash(secret)
                }
            ];
        }
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        http.RequestServices = scope.ServiceProvider;
        http.Request.Headers[options.HeaderName] = configured ? secret : ApiKeyHashing.FormatPersistedApiKey(key.KeyId, secret);
        var handler = ActivatorUtilities.CreateInstance<ApiKeyAuthenticationHandler>(scope.ServiceProvider, UrlEncoder.Default);
        await handler.InitializeAsync(new AuthenticationScheme(ApiAuthenticationSchemeNames.ApiKey,
            ApiAuthenticationSchemeNames.ApiKey, typeof(ApiKeyAuthenticationHandler)), http);
        var result = await handler.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        http.User = result.Principal!;
        return key;
    }
}
