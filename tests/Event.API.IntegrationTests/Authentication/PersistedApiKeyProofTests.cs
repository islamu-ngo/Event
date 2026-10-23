using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.API.Authentication;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

public sealed class PersistedApiKeyProofTests
{
    private static readonly Guid RowId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000741");
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000742");
    private static readonly Guid OwnerId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000743");
    private static readonly DateTime ExpiresAt = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
    private const string KeyId = "persisted-proof";

    [Test]
    [Arguments(ExternalApiKeyStatusEnum.Active, ExternalApiKeyOwnerType.User, true)]
    [Arguments(ExternalApiKeyStatusEnum.PendingRotation, ExternalApiKeyOwnerType.User, true)]
    [Arguments(ExternalApiKeyStatusEnum.Active, ExternalApiKeyOwnerType.Organization, true)]
    [Arguments(ExternalApiKeyStatusEnum.Active, ExternalApiKeyOwnerType.InstanceAdmin, false)]
    public async Task SuccessfulPersistedAuthenticationPublishesBoundServerProof(
        ExternalApiKeyStatusEnum status,
        ExternalApiKeyOwnerType ownerType,
        bool tenantScoped)
    {
        using Scenario scenario = await CreateScenario(status: status, ownerType: ownerType, tenantScoped: tenantScoped);

        AuthenticateResult result = await scenario.Handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsTrue();
        PersistedApiKeyAuthenticationProof? proof = scenario.Context.Features.Get<PersistedApiKeyAuthenticationProof>();
        await Assert.That(proof).IsNotNull();
        await Assert.That(proof!.IsBoundTo(result.Principal!)).IsTrue();
        await Assert.That(proof.Origin).IsEqualTo("persisted");
        await Assert.That(proof.RowId).IsEqualTo(RowId);
        await Assert.That(proof.KeyId).IsEqualTo(KeyId);
        await Assert.That(proof.TenantId).IsEqualTo(tenantScoped ? (Guid?)TenantId : null);
        await Assert.That(proof.OwnerType).IsEqualTo(ownerType);
        await Assert.That(proof.OwnerId).IsEqualTo(OwnerId);
        await Assert.That(proof.StatusId).IsEqualTo((int)status);
        await Assert.That(proof.ExpiresAt).IsEqualTo((DateTime?)ExpiresAt);
        await Assert.That(proof.Scopes.ToArray()).IsEquivalentTo(new[] { "events:read", "events:write" });

        // Authentication middleware installs the handler's ticket principal on the request.
        scenario.Context.User = result.Principal!;
        await Assert.That(ReferenceEquals(scenario.Accessor.CurrentProof, proof)).IsTrue();
        await Assert.That(scenario.Accessor.IsMachineCaller).IsTrue();
    }

    [Test]
    public async Task ConfiguredKeyAuthenticatesWithoutPersistedProof()
    {
        using Scenario scenario = await CreateScenario(configured: true);

        AuthenticateResult result = await scenario.Handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsTrue();
        scenario.Context.User = result.Principal!;
        await Assert.That(scenario.Accessor.IsMachineCaller).IsTrue();
        await Assert.That(scenario.Accessor.Current!.KeyId).IsEqualTo(KeyId);
        await Assert.That(scenario.Context.Features.Get<PersistedApiKeyAuthenticationProof>()).IsNull();
        await Assert.That(scenario.Accessor.CurrentProof).IsNull();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("malformed")]
    [Arguments("unknown")]
    [Arguments("wrong_secret")]
    [Arguments("revoked")]
    [Arguments("expired")]
    [Arguments("configured_expired")]
    public async Task UnsuccessfulAuthenticationNeverPublishesProof(string failure)
    {
        using Scenario scenario = await CreateScenario(failure: failure, configured: failure == "configured_expired");

        AuthenticateResult result = await scenario.Handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.None).IsEqualTo(failure == "missing");
        if (failure != "missing")
        {
            await Assert.That(result.Failure).IsNotNull();
        }
        await Assert.That(result.Principal).IsNull();
        await Assert.That(scenario.Context.Features.Get<PersistedApiKeyAuthenticationProof>()).IsNull();
        await Assert.That(scenario.Accessor.CurrentProof).IsNull();
    }

    [Test]
    [Arguments("cloned_principal")]
    [Arguments("replacement_identity")]
    [Arguments("additional_authenticated_identity")]
    [Arguments("anonymous")]
    public async Task CopiedOrReplacedIdentityCannotInheritServerProof(string replacement)
    {
        using Scenario scenario = await CreateScenario();
        AuthenticateResult result = await scenario.Handler.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        ClaimsPrincipal original = result.Principal!;
        scenario.Context.User = original;
        PersistedApiKeyAuthenticationProof? proof = scenario.Accessor.CurrentProof;
        await Assert.That(proof).IsNotNull();

        scenario.Context.User = replacement switch
        {
            "cloned_principal" => new ClaimsPrincipal(original.Identities.Select(identity => identity.Clone())),
            "replacement_identity" => new ClaimsPrincipal(new ClaimsIdentity(
                original.Claims, ApiAuthenticationSchemeNames.ApiKey, ClaimTypes.Name, ClaimTypes.Role)),
            "additional_authenticated_identity" => new ClaimsPrincipal(
                new[] { (ClaimsIdentity)original.Identity!, new ClaimsIdentity([], "Bearer") }),
            "anonymous" => new ClaimsPrincipal(new ClaimsIdentity(original.Claims)),
            _ => throw new ArgumentOutOfRangeException(nameof(replacement))
        };

        await Assert.That(ReferenceEquals(scenario.Context.Features.Get<PersistedApiKeyAuthenticationProof>(), proof)).IsTrue();
        await Assert.That(proof!.IsBoundTo(scenario.Context.User)).IsFalse();
        await Assert.That(scenario.Accessor.CurrentProof).IsNull();
        if (replacement is "cloned_principal" or "replacement_identity")
        {
            // Identical machine claims are insufficient without the handler-owned identity.
            await Assert.That(scenario.Accessor.Current!.KeyId).IsEqualTo(KeyId);
            await Assert.That(scenario.Accessor.Current.OwnerId).IsEqualTo(OwnerId);
        }
    }

    [Test]
    [Arguments("owner_type")]
    [Arguments("owner_id")]
    [Arguments("tenant")]
    [Arguments("tenant_removed")]
    [Arguments("key")]
    [Arguments("scope_added")]
    [Arguments("scope_removed")]
    [Arguments("internal_user")]
    [Arguments("foreign_subject")]
    public async Task ChangedClaimsOnOriginalIdentityCannotInheritServerProof(string change)
    {
        using Scenario scenario = await CreateScenario();
        AuthenticateResult result = await scenario.Handler.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        scenario.Context.User = result.Principal!;
        PersistedApiKeyAuthenticationProof? proof = scenario.Accessor.CurrentProof;
        await Assert.That(proof).IsNotNull();
        var identity = (ClaimsIdentity)scenario.Context.User.Identity!;

        switch (change)
        {
            case "owner_type":
                ReplaceClaim(identity, ApiAuthenticationClaimTypes.OwnerType, nameof(ExternalApiKeyOwnerType.Organization));
                break;
            case "owner_id":
                ReplaceClaim(identity, ApiAuthenticationClaimTypes.OwnerId, Guid.CreateVersion7().ToString("D"));
                break;
            case "tenant":
                ReplaceClaim(identity, ApiAuthenticationClaimTypes.TenantId, Guid.CreateVersion7().ToString("D"));
                break;
            case "tenant_removed":
                identity.RemoveClaim(identity.FindFirst(ApiAuthenticationClaimTypes.TenantId)!);
                break;
            case "key":
                ReplaceClaim(identity, ApiAuthenticationClaimTypes.ApiKeyId, "replacement-key");
                break;
            case "scope_added":
                identity.AddClaim(new Claim(ApiAuthenticationClaimTypes.Scope, "events:delete"));
                break;
            case "scope_removed":
                identity.RemoveClaim(identity.FindFirst(ApiAuthenticationClaimTypes.Scope)!);
                break;
            case "internal_user":
                ReplaceClaim(identity, PlatformIdentityClaimTypes.InternalUserId, Guid.CreateVersion7().ToString("D"));
                break;
            case "foreign_subject":
                identity.AddClaim(new Claim("sub", OwnerId.ToString("D")));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        await Assert.That(ReferenceEquals(scenario.Context.Features.Get<PersistedApiKeyAuthenticationProof>(), proof)).IsTrue();
        await Assert.That(scenario.Accessor.CurrentProof).IsNull();
    }

    [Test]
    public async Task InternalExecutionWithIdenticalMachineContextCannotInheritRequestProof()
    {
        using Scenario scenario = await CreateScenario();
        AuthenticateResult result = await scenario.Handler.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        scenario.Context.User = result.Principal!;
        PersistedApiKeyAuthenticationProof? proof = scenario.Accessor.CurrentProof;
        await Assert.That(proof).IsNotNull();
        ApiKeyPrincipalContext machine = scenario.Accessor.Current!;

        scenario.Accessor.SetPrincipal(machine);

        await Assert.That(ReferenceEquals(scenario.Accessor.Current, machine)).IsTrue();
        await Assert.That(scenario.Accessor.CurrentProof).IsNull();
        await Assert.That(ReferenceEquals(scenario.Context.Features.Get<PersistedApiKeyAuthenticationProof>(), proof)).IsTrue();

        scenario.Accessor.Clear();
        await Assert.That(ReferenceEquals(scenario.Accessor.CurrentProof, proof)).IsTrue();
    }

    [Test]
    public async Task ProofSnapshotRemainsStableAfterSourceEntityMutation()
    {
        using Scenario scenario = await CreateScenario();
        AuthenticateResult result = await scenario.Handler.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        scenario.Context.User = result.Principal!;
        PersistedApiKeyAuthenticationProof? proof = scenario.Accessor.CurrentProof;
        await Assert.That(proof).IsNotNull();

        scenario.Entity.Id = Guid.CreateVersion7();
        scenario.Entity.KeyId = "mutated-key";
        scenario.Entity.TenantId = null;
        scenario.Entity.OwnerType = ExternalApiKeyOwnerType.InstanceAdmin;
        scenario.Entity.OwnerId = Guid.CreateVersion7();
        scenario.Entity.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
        scenario.Entity.ExpiresAt = DateTime.UnixEpoch;
        scenario.Entity.Scopes = "events:delete";

        await Assert.That(ReferenceEquals(scenario.Accessor.CurrentProof, proof)).IsTrue();
        await Assert.That(proof!.Origin).IsEqualTo("persisted");
        await Assert.That(proof.RowId).IsEqualTo(RowId);
        await Assert.That(proof.KeyId).IsEqualTo(KeyId);
        await Assert.That(proof.TenantId).IsEqualTo((Guid?)TenantId);
        await Assert.That(proof.OwnerType).IsEqualTo(ExternalApiKeyOwnerType.User);
        await Assert.That(proof.OwnerId).IsEqualTo(OwnerId);
        await Assert.That(proof.StatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
        await Assert.That(proof.ExpiresAt).IsEqualTo((DateTime?)ExpiresAt);
        await Assert.That(proof.Scopes.ToArray()).IsEquivalentTo(new[] { "events:read", "events:write" });
    }

    private static void ReplaceClaim(ClaimsIdentity identity, string type, string value)
    {
        identity.RemoveClaim(identity.FindFirst(type)!);
        identity.AddClaim(new Claim(type, value));
    }

    private static async Task<Scenario> CreateScenario(
        ExternalApiKeyStatusEnum status = ExternalApiKeyStatusEnum.Active,
        ExternalApiKeyOwnerType ownerType = ExternalApiKeyOwnerType.User,
        bool tenantScoped = true,
        bool configured = false,
        string? failure = null)
    {
        string secret = ApiKeyHashing.CreateSecret();
        var entity = new ExternalApiKey
        {
            Id = RowId,
            Name = "Proof authentication probe",
            KeyId = KeyId,
            SecretHash = ApiKeyHashing.ComputeHash(secret),
            TenantId = tenantScoped ? TenantId : null,
            OwnerType = ownerType,
            OwnerId = OwnerId,
            ExternalApiKeyStatusId = failure == "revoked" ? (int)ExternalApiKeyStatusEnum.Revoked : (int)status,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
            ExternalApiKeyCreditPeriod = null!,
            ExpiresAt = failure == "expired" ? DateTime.UnixEpoch : ExpiresAt,
            Scopes = "events:read, events:write"
        };
        var repository = Substitute.For<IExternalApiKeyAuthenticationRepository>();
        if (!configured && failure != "unknown")
        {
            repository.GetByKeyIdForAuthentication(KeyId, Arg.Any<CancellationToken>()).Returns(entity);
        }
        var optionsValue = new ApiKeyAuthenticationOptions();
        if (configured)
        {
            optionsValue.Clients =
            [
                new ApiKeyClientDescriptor
                {
                    KeyId = KeyId,
                    TenantId = TenantId,
                    OwnerType = nameof(ExternalApiKeyOwnerType.User),
                    OwnerId = OwnerId.ToString("D"),
                    Scopes = ["events:read", "events:write"],
                    SecretHash = ApiKeyHashing.ComputeHash(secret),
                    ExpiresAtUtc = failure == "configured_expired" ? DateTimeOffset.UnixEpoch : null
                }
            ];
        }
        string credential = configured ? secret : ApiKeyHashing.FormatPersistedApiKey(
            KeyId, failure == "wrong_secret" ? secret + "x" : secret);
        if (failure == "malformed")
        {
            credential = secret;
        }
        var options = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        options.Get(ApiAuthenticationSchemeNames.ApiKey).Returns(optionsValue);
        var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var loggerFactory = LoggerFactory.Create(_ => { });
        var metrics = new BusinessMetrics(services.GetRequiredService<IMeterFactory>());
        var context = new DefaultHttpContext { RequestServices = services };
        if (failure != "missing")
        {
            context.Request.Headers[optionsValue.HeaderName] = credential;
        }
        var handler = new ApiKeyAuthenticationHandler(options, loggerFactory, UrlEncoder.Default, repository, metrics);
        await handler.InitializeAsync(
            new AuthenticationScheme(ApiAuthenticationSchemeNames.ApiKey,
                ApiAuthenticationSchemeNames.ApiKey, typeof(ApiKeyAuthenticationHandler)),
            context);
        var contextAccessor = Substitute.For<IHttpContextAccessor>();
        contextAccessor.HttpContext.Returns(context);
        var accessor = new MachinePrincipalAccessor(contextAccessor);
        return new Scenario(handler, context, accessor, entity, services, metrics, loggerFactory);
    }

    private sealed record Scenario(
        ApiKeyAuthenticationHandler Handler,
        DefaultHttpContext Context,
        MachinePrincipalAccessor Accessor,
        ExternalApiKey Entity,
        ServiceProvider Services,
        BusinessMetrics Metrics,
        ILoggerFactory LoggerFactory) : IDisposable
    {
        public void Dispose()
        {
            Metrics.Dispose();
            LoggerFactory.Dispose();
            Services.Dispose();
        }
    }
}
