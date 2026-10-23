using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>Issues distinct original human and setup sources before configuration validation.</summary>
public sealed class ProviderConfigurationMutationAuthority(IServiceProvider services,
    IHttpContextAccessor contexts, IHumanCommitScopePreparation human,
    IHumanAuthenticationLifetimeObserver lifetimes, IConfiguration configuration, ITenantContext tenants, TimeProvider clock)
    : IProviderConfigurationMutationAuthority
{
    private readonly IServiceProvider _services = services;
    private readonly IHttpContextAccessor _contexts = contexts;
    private readonly IHumanCommitScopePreparation _human = human;
    private readonly IHumanAuthenticationLifetimeObserver _lifetimes = lifetimes;
    private readonly IConfiguration _configuration = configuration;
    private readonly ITenantContext _tenants = tenants;
    private readonly TimeProvider _clock = clock;
    private Source? issued;

    public async Task<IProviderConfigurationMutationSource> CaptureAsync(object command, CancellationToken token)
    {
        if (issued is not null) throw Denied();
        Guid? actor = command switch
        {
            UpdateAuthProviderConfigurationCommand value => value.UserId,
            UpdateAuthorizationProviderConfigurationCommand value => value.UserId,
            UpdateAuthProviderConfigurationDuringSetupCommand => null,
            UpdateAuthorizationProviderConfigurationDuringSetupCommand => null,
            _ => throw Denied()
        };
        var request = _contexts.HttpContext ?? throw Denied();
        var principal = request.User;
        var authenticated = await request.AuthenticateAsync();
        if (!authenticated.Succeeded || authenticated.Ticket is null || authenticated.Principal is null
            || !authenticated.Principal.Identities.Any(identity => identity.IsAuthenticated && principal.Identities.Contains(identity)))
            throw Denied();
        if (command is UpdateAuthProviderConfigurationDuringSetupCommand or UpdateAuthorizationProviderConfigurationDuringSetupCommand)
        {
            if (authenticated.Ticket.AuthenticationScheme != ApiAuthenticationSchemeNames.SetupSecret || _human.Subject is not null)
                throw Denied();
            var setup = await _services.GetRequiredService<IProviderConfigurationSetupAuthority>()
                .CaptureAsync(new TrustedSetupSettingsSource(null, null, null), token);
            issued = new SetupSource(this, command, request, principal, authenticated.Ticket, setup);
        }
        else
        {
            if (actor is null || actor == Guid.Empty || authenticated.Ticket.AuthenticationScheme == ApiAuthenticationSchemeNames.SetupSecret)
                throw Denied();
            await _human.PrepareAsync(token);
            var subject = _human.Subject ?? throw Denied();
            var lifetime = await _lifetimes.ObserveAsync(principal, token) ?? throw Denied();
            if (subject.PrincipalId != actor || lifetime.AccountKey != subject.AccountKey) throw Denied();
            var principalFence = _services.GetRequiredService<IHumanPrincipalSourceFence>();
            var supportFence = await _services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync(token);
            var principalFacts = await principalFence.ObserveAsync(token);
            var supportFacts = await supportFence.ObserveAsync(token);
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principalFacts);
            if (principalFacts.SelectedTenantId is not null) throw Denied();
            issued = new HumanSource(this, command, request, principal, authenticated.Ticket,
                subject, lifetime, principalFence, supportFence, principalFacts, supportFacts);
        }
        await issued.RequireSourcesAsync(token);
        issued.RequireTime();
        return issued;
    }

    public void RequireIssued(IProviderConfigurationMutationSource source)
    {
        if (!ReferenceEquals(issued, source)) throw Denied();
    }

    private static UnauthorizedAccessException Denied() => new("The original provider configuration source changed.");

    private abstract class Source : IProviderConfigurationMutationSource
    {
        protected readonly ProviderConfigurationMutationAuthority Owner;
        protected readonly HttpContext Request;
        protected readonly System.Security.Claims.ClaimsPrincipal Principal;
        private readonly ClaimsIdentity[] ticketIdentities;
        private readonly Claim[] ticketClaims;
        private readonly string scheme;
        private readonly DateTimeOffset? issuedAt;
        private readonly DateTimeOffset? expiresAt;
        private readonly IServiceProvider requestServices;
        private readonly Guid ambientTenant;
        private readonly string requestShape;
        private readonly IConfigurationProvider[] providers;
        private readonly string declarations;
        private readonly AuthorizationProviderDeploymentOptions deploymentOptions;
        private readonly string providerOption;
        private readonly AuthorizationProviderBootstrapState bootstrap;
        private readonly AuthorizationProviderBootstrapSnapshot bootstrapSnapshot;
        private readonly List<RetainedSecretReference> references = [];
        private string? materials;
        private bool observed;
        private IPolicyPackageService? packages;
        private CerbosPolicyPackageService.ProviderConfigurationPublication? originalPackage;
        private CerbosPolicyPackageOptions? publicationOptions;
        private CerbosSettings? publicationEndpoint;
        private string? publicationDeclarations;
        private CerbosPublicationOptions? originalPublication;
        private string? originalEndpoint;
        private PolicyPackageManifest? publicationManifest;
        private string? manifestImage;
        private bool? publicationReady;

        protected Source(ProviderConfigurationMutationAuthority owner, object command, HttpContext request,
            System.Security.Claims.ClaimsPrincipal principal, AuthenticationTicket ticket)
        {
            Owner = owner; OriginalCommand = command; Request = request; Principal = principal;
            ticketIdentities = ticket.Principal.Identities.ToArray();
            ticketClaims = ticket.Principal.Claims.ToArray();
            scheme = ticket.AuthenticationScheme;
            issuedAt = ticket.Properties.IssuedUtc;
            expiresAt = ticket.Properties.ExpiresUtc;
            requestServices = request.RequestServices;
            ambientTenant = owner._tenants.TenantId;
            requestShape = RequestShape(request);
            if (owner._configuration is not IConfigurationRoot root) throw Denied();
            providers = root.Providers.ToArray();
            declarations = Declarations();
            Deployment = new ConfigurationBuilder().AddInMemoryCollection(owner._configuration.AsEnumerable()
                .Where(pair => IsDeclaration(pair.Key))).Build();
            deploymentOptions = owner._services.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>().Value;
            providerOption = JsonSerializer.Serialize(deploymentOptions);
            bootstrap = owner._services.GetRequiredService<AuthorizationProviderBootstrapState>();
            bootstrapSnapshot = bootstrap.Read();
        }

        public object OriginalCommand { get; }
        public IConfiguration Deployment { get; }

        private static bool IsDeclaration(string key) => key is "AUTHENTICATION_PROVIDER" or "AUTHORIZATION_PROVIDER"
            or "PublicBaseUrl" or "App:PublicBaseUrl" or "Application:PublicBaseUrl"
            || key.StartsWith("Authentication:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Keycloak:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Cerbos:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Secrets:Ownership:", StringComparison.OrdinalIgnoreCase);

        private string Declarations()
        {
            var keys = Owner._configuration.AsEnumerable().Where(pair => IsDeclaration(pair.Key))
                .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
            return JsonSerializer.Serialize(keys.Select(key => new
            {
                Key = key,
                Sources = providers.Select(provider => provider.TryGet(key, out string? value)
                    ? new { Present = true, Value = value } : new { Present = false, Value = (string?)null }).ToArray()
            }));
        }

        private static string RequestShape(HttpContext request) => JsonSerializer.Serialize(new
        {
            request.Request.Method,
            request.Request.Scheme,
            Host = request.Request.Host.Value,
            Path = request.Request.Path.Value,
            Base = request.Request.PathBase.Value,
            Query = request.Request.QueryString.Value,
            Authorization = request.Request.Headers.Authorization.ToString(),
            TenantId = request.Request.Headers["X-Tenant-Id"].ToString(),
            TenantSlug = request.Request.Headers["X-Tenant-Slug"].ToString(),
            Cookie = request.Request.Headers.Cookie.ToString(),
            Setup = request.Request.Headers["X-Setup-Secret"].ToString(),
            Support = request.Request.Headers.Where(header => SupportAccessHeaderNames.IsSupportAccessHeader(header.Key))
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .Select(header => new { header.Key, Value = header.Value.ToString() }).ToArray()
        });

        protected void RequireCustody()
        {
            Request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Owner._contexts.HttpContext, Request) || !ReferenceEquals(Request.User, Principal)
                || !ReferenceEquals(Request.RequestServices, requestServices) || RequestShape(Request) != requestShape
                || Owner._tenants.TenantId != ambientTenant
                || Owner._configuration is not IConfigurationRoot root || !root.Providers.SequenceEqual(providers)
                || Declarations() != declarations || JsonSerializer.Serialize(deploymentOptions) != providerOption
                || !ReferenceEquals(bootstrap.Read(), bootstrapSnapshot)
                || publicationOptions is not null && PublicationDeclarations() != publicationDeclarations) throw Denied();
        }

        public async Task RequireSourcesAsync(CancellationToken token)
        {
            await RequireAuthenticationAsync();
            await RequireOriginalAuthorityAsync(token);
            RequireCustody();
        }

        protected async Task RequireAuthenticationAsync()
        {
            RequireCustody();
            var current = await Request.AuthenticateAsync();
            if (!current.Succeeded || current.Ticket?.AuthenticationScheme != scheme || current.Principal is null
                || !ticketIdentities.SequenceEqual(current.Principal.Identities)
                || !ticketClaims.SequenceEqual(current.Principal.Claims)
                || current.Properties?.IssuedUtc != issuedAt || current.Properties?.ExpiresUtc != expiresAt) throw Denied();
            RequireCustody();
        }

        protected abstract Task RequireOriginalAuthorityAsync(CancellationToken token);
        public abstract void RequireTime();

        public async Task<ProviderConfigurationMaterials> ObserveMaterialsAsync(ProviderConfigurationState state, CancellationToken token)
        {
            Owner._services.GetRequiredService<IProviderConfigurationSettingsWriter>().RequireIdle();
            if (observed) throw Denied();
            observed = true;
            var results = new Dictionary<string, SecretResolutionResult>(StringComparer.Ordinal);
            if (OriginalCommand is UpdateAuthProviderConfigurationCommand or UpdateAuthProviderConfigurationDuringSetupCommand)
            {
                var resolver = Owner._services.GetRequiredService<IRetainedSecretResolver>();
                using var bindings = JsonDocument.Parse(state.Bindings);
                foreach (string key in ProviderConfigurationState.SecretKeys)
                {
                    // Guid.Empty is the existing retained resolver's explicit instance selection.
                    // It does not create a tenant or select an ambient request tenant.
                    var reference = await resolver.CaptureAsync(key, Guid.Empty, token);
                    var row = bindings.RootElement.EnumerateArray().SingleOrDefault(item =>
                        item.GetProperty("SettingKey").GetString() == key
                        && item.GetProperty("SettingScopeId").GetInt32() == 1
                        && item.GetProperty("ScopeId").ValueKind == JsonValueKind.Null
                        && item.GetProperty("Qualifier").GetString() == "");
                    if (row.ValueKind == JsonValueKind.Undefined)
                    {
                        if (reference.BindingId is not null) throw Denied();
                    }
                    else if (reference.BindingId != row.GetProperty("Id").GetGuid()
                        || (int)reference.SourceType != row.GetProperty("SecretSourceTypeId").GetInt32()
                        || reference.EnvironmentVariableName != row.GetProperty("EnvironmentVariableName").GetString()
                        || reference.InfisicalEnvironment != row.GetProperty("InfisicalEnvironment").GetString()
                        || reference.InfisicalPath != row.GetProperty("InfisicalPath").GetString()
                        || reference.InfisicalKey != row.GetProperty("InfisicalKey").GetString()) throw Denied();
                    if (reference.Scope != SecretScope.Instance || reference.ScopeId is not null || reference.Qualifier != "")
                        throw Denied();
                    references.Add(reference);
                    results.Add(key, await resolver.ResolveAsync(reference, token));
                }
            }
            else if (deploymentOptions.GetProvider() == AuthorizationProviderDeploymentOptions.CerbosProvider
                && bootstrapSnapshot.Provider == AuthorizationProviderDeploymentOptions.CerbosProvider
                && bootstrapSnapshot.Status == AuthorizationProviderBootstrapState.Ready)
            {
                publicationOptions = Owner._services.GetRequiredService<IOptions<CerbosPolicyPackageOptions>>().Value;
                publicationEndpoint = Owner._services.GetRequiredService<IOptions<CerbosSettings>>().Value;
                publicationDeclarations = PublicationDeclarations();
                originalPublication = JsonSerializer.Deserialize<CerbosPublicationOptions>(JsonSerializer.Serialize(publicationOptions.Publication))!;
                originalEndpoint = publicationEndpoint.GrpcEndpoint;
                packages = Owner._services.GetRequiredService<IPolicyPackageService>();
                publicationManifest = await ReadPublicationManifestAsync(token);
                manifestImage = ManifestImage(publicationManifest);
                publicationReady = VerifyPublication();
            }
            materials = MaterialSnapshot(results);
            var keycloak = results.Count == 0 ? KeycloakConnectionResolution.Failed(KeycloakConnectionStatus.Unconfigured)
                : await new KeycloakConnectionResolver(new OriginalSecrets(results), Deployment,
                    Owner._services.GetRequiredService<KeycloakCredentialBindingRevision>()).ResolveRuntimeAsync(token);
            await RequireSourcesAsync(token);
            RequireTime();
            return new(keycloak, results.GetValueOrDefault(SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret)
                ?? SecretResolutionResult.Unconfigured, publicationReady);
        }

        public async Task RequireExternalSourcesAsync(CancellationToken token)
        {
            Owner._services.GetRequiredService<IProviderConfigurationSettingsWriter>().RequireIdle();
            if (!observed) throw Denied();
            var resolver = Owner._services.GetRequiredService<IRetainedSecretResolver>();
            var results = new Dictionary<string, SecretResolutionResult>(StringComparer.Ordinal);
            foreach (var reference in references)
                results.Add(reference.SettingKey, await resolver.ResolveAsync(reference, token));
            if (MaterialSnapshot(results) != materials) throw Denied();
            if (packages is not null && ManifestImage(await ReadPublicationManifestAsync(token)) != manifestImage) throw Denied();
            await RequireSourcesAsync(token);
            RequireTime();
        }

        private string PublicationDeclarations() => JsonSerializer.Serialize(new
        { Options = publicationOptions, Endpoint = publicationEndpoint?.GrpcEndpoint });

        private async Task<PolicyPackageManifest?> ReadPublicationManifestAsync(CancellationToken token)
        {
            if (originalPublication!.ReceiptBase64.Length is 0 or > 12000
                || originalPublication.SignatureBase64.Length is 0 or > 1500) return null;
            if (originalPackage is null)
            {
                var compiler = packages as CerbosPolicyPackageService ?? throw Denied();
                originalPackage = await compiler.ObserveProviderConfigurationPublicationAsync(token);
            }
            else await originalPackage.RequireOriginalAsync(token);
            return originalPackage.Manifest;
        }

        private static string ManifestImage(PolicyPackageManifest? manifest) =>
            JsonSerializer.Serialize(manifest is null ? null : new
            { manifest.PackageId, manifest.Version, manifest.ContentHash, manifest.Artifacts });

        private bool VerifyPublication()
        {
            if (publicationManifest is null) return false;
            try
            {
                return new CerbosPublicationReceiptVerifier().Verify(
                    Convert.FromBase64String(originalPublication!.ReceiptBase64),
                    Convert.FromBase64String(originalPublication.SignatureBase64),
                    originalPublication.PublicKeyPem, originalPublication.Revision, publicationManifest.ContentHash,
                    originalPublication.Environment, originalEndpoint!, Owner._clock.GetUtcNow());
            }
            catch (FormatException) { return false; }
        }

        protected void RequirePublicationTime()
        {
            if (publicationReady.HasValue && VerifyPublication() != publicationReady.Value) throw Denied();
        }

        private static string MaterialSnapshot(Dictionary<string, SecretResolutionResult> results) =>
            JsonSerializer.Serialize(results.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
            {
                pair.Key,
                pair.Value.Status,
                pair.Value.Value,
                pair.Value.Source,
                pair.Value.Scope,
                pair.Value.ScopeId,
                Revision = pair.Value.Secret?.BindingRevision
            }));
    }

    private sealed class HumanSource(ProviderConfigurationMutationAuthority owner, object command,
        HttpContext request, System.Security.Claims.ClaimsPrincipal principal, AuthenticationTicket ticket,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, IHumanPrincipalSourceFence principalFence,
        IHumanSupportAccessSourceFence supportFence, HumanPrincipalSourceFacts principalFacts, HumanSupportAccessSourceFacts supportFacts)
        : Source(owner, command, request, principal, ticket), IProviderConfigurationHumanSource
    {
        private readonly string originalPrincipal = JsonSerializer.Serialize(principalFacts);
        private readonly string originalSupport = JsonSerializer.Serialize(supportFacts);
        public HumanCommitSubject Subject => subject;
        public HumanAuthenticationLifetime Lifetime => lifetime;

        protected override async Task RequireOriginalAuthorityAsync(CancellationToken token)
        {
            await RequireCredentialAsync(token);
            RequireAcquiredFacts(await principalFence.ObserveAsync(token), await supportFence.ObserveAsync(token));
        }

        private async Task RequireCredentialAsync(CancellationToken token)
        {
            if (!ReferenceEquals(Owner._human.Subject, subject)
                || !ReferenceEquals(await Owner._lifetimes.ObserveAsync(Principal, token), lifetime)) throw Denied();
        }

        public async Task RequireRequestAsync(CancellationToken token)
        {
            await RequireAuthenticationAsync();
            await RequireCredentialAsync(token);
            RequireCustody();
        }

        public void RequireAcquiredFacts(HumanPrincipalSourceFacts currentPrincipal, HumanSupportAccessSourceFacts currentSupport)
        {
            RequireCustody();
            if (JsonSerializer.Serialize(currentPrincipal) != originalPrincipal
                || JsonSerializer.Serialize(currentSupport) != originalSupport) throw Denied();
        }

        public override void RequireTime()
        {
            RequireCustody();
            RequirePublicationTime();
            var now = Owner._clock.GetUtcNow();
            var support = HumanSupportAccessProjection.Context(supportFacts, now);
            if (!lifetime.IsValidAt(now) || !ReferenceEquals(Owner._human.Subject, subject)
                || !ReferenceEquals(supportFacts.Forwarding.Subject, subject)
                || support.WasForwarded && (!support.IsActive || !support.AllowsWrites)) throw Denied();
        }
    }

    private sealed class SetupSource(ProviderConfigurationMutationAuthority owner, object command,
        HttpContext request, System.Security.Claims.ClaimsPrincipal principal, AuthenticationTicket ticket,
        ITrustedSetupSettingsAuthorityLease setup)
        : Source(owner, command, request, principal, ticket), IProviderConfigurationSetupSource
    {
        public ITrustedSetupSettingsAuthorityLease Setup => setup;
        protected override Task RequireOriginalAuthorityAsync(CancellationToken token)
        {
            if (Owner._human.Subject is not null) throw Denied();
            return setup.RequireCurrentAsync(token);
        }
        public override void RequireTime()
        {
            RequireCustody();
            RequirePublicationTime();
            if (Owner._human.Subject is not null) throw Denied();
            setup.RequireCurrentTime();
        }
    }

    private sealed class OriginalSecrets(IReadOnlyDictionary<string, SecretResolutionResult> results) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string key, Guid? tenant, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(tenant is null && results.TryGetValue(key, out var result) ? result : throw Denied());
        }
        public Task<SecretResolutionResult> ResolveQualifiedAsync(string key, SecretScope scope, Guid? id, string qualifier,
            CancellationToken token = default) => throw Denied();
        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenant, Guid binding, CancellationToken token = default) => throw Denied();
        public Task InvalidateAsync(string key, SecretScope scope, Guid? id, CancellationToken token = default) => throw Denied();
    }
}

public static class ProviderConfigurationMutationRegistration
{
    public static IServiceCollection AddProviderConfigurationMutationAuthority(this IServiceCollection services)
    {
        services.TryAddScoped<IProviderConfigurationMutationAuthority, ProviderConfigurationMutationAuthority>();
        return services;
    }
}
