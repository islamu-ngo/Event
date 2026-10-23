using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Infrastructure.Services;

public sealed class StorageConfigurationAuthority(IServiceProvider services,
    IHttpContextAccessor contexts, IHumanCommitScopePreparation human,
    IHumanAuthenticationLifetimeObserver lifetimes, ITenantContext tenants, TimeProvider clock)
    : IStorageConfigurationAuthority
{
    private readonly IServiceProvider _services = services;
    private readonly IHttpContextAccessor _contexts = contexts;
    private readonly IHumanCommitScopePreparation _human = human;
    private readonly IHumanAuthenticationLifetimeObserver _lifetimes = lifetimes;
    private readonly ITenantContext _tenants = tenants;
    private readonly TimeProvider _clock = clock;
    private Source? issued;

    public async Task<IStorageConfigurationSource> CaptureAsync(object command, Guid? tenantId, CancellationToken token)
    {
        Guid actor = command switch
        {
            UpdateInstanceStorageSettingsCommand instance when tenantId is null => instance.UserId,
            PatchTenantStorageSettingsCommand tenant when tenantId == _tenants.TenantId => tenant.UserId,
            _ => throw Denied()
        };
        if (issued is not null || actor == Guid.Empty || tenantId == Guid.Empty) throw Denied();
        var request = _contexts.HttpContext ?? throw Denied();
        var principal = request.User;
        var authentication = await request.AuthenticateAsync();
        if (!authentication.Succeeded || authentication.Ticket is null || authentication.Principal is null
            || authentication.Ticket.AuthenticationScheme == ApiAuthenticationSchemeNames.SetupSecret
            || !authentication.Principal.Identities.Any(identity => identity.IsAuthenticated && principal.Identities.Contains(identity)))
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
        if (principalFacts.SelectedTenantId != tenantId) throw Denied();
        issued = new(this, command, tenantId, _tenants.TenantId, request, principal,
            authentication.Ticket, subject, lifetime, principalFence, supportFence, principalFacts, supportFacts);
        await issued.RequireSourcesAsync(token);
        issued.RequireTime();
        return issued;
    }

    public void RequireIssued(IStorageConfigurationSource source)
    {
        if (!ReferenceEquals(issued, source)) throw Denied();
    }

    private static UnauthorizedAccessException Denied() => new("The original storage configuration custody is no longer valid.");

    private sealed class Source(StorageConfigurationAuthority owner, object command, Guid? tenant,
        Guid ambient, HttpContext request, System.Security.Claims.ClaimsPrincipal principal,
        AuthenticationTicket ticket, HumanCommitSubject subject, HumanAuthenticationLifetime lifetime,
        IHumanPrincipalSourceFence principalFence, IHumanSupportAccessSourceFence supportFence,
        HumanPrincipalSourceFacts principalFacts, HumanSupportAccessSourceFacts supportFacts)
        : IStorageConfigurationSource
    {
        private readonly IServiceProvider requestServices = request.RequestServices;
        private readonly string method = request.Request.Method;
        private readonly PathString path = request.Request.Path;
        private readonly PathString pathBase = request.Request.PathBase;
        private readonly QueryString query = request.Request.QueryString;
        private readonly string authorization = request.Request.Headers.Authorization.ToString();
        private readonly string cookie = request.Request.Headers.Cookie.ToString();
        private readonly bool requestTenantPresent = request.Items.ContainsKey(TenantContextAccessor.TenantIdItemKey);
        private readonly Guid? requestTenant = request.Items.TryGetValue(TenantContextAccessor.TenantIdItemKey, out var marker)
            && marker is Guid selected && selected != Guid.Empty ? selected : null;
        private readonly Dictionary<string, string> support = request.Request.Headers
            .Where(header => SupportAccessHeaderNames.IsSupportAccessHeader(header.Key))
            .ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        private RetainedSecretReference[]? references;
        private string? originalExternal;
        private bool providerObserved;
        private readonly string originalPrincipal = JsonSerializer.Serialize(principalFacts);
        private readonly string originalSupport = JsonSerializer.Serialize(supportFacts);
        private readonly ClaimsIdentity[] ticketIdentities = ticket.Principal.Identities.ToArray();
        private readonly Claim[] ticketClaims = ticket.Principal.Claims.ToArray();
        private readonly string scheme = ticket.AuthenticationScheme;
        private readonly DateTimeOffset? issuedAt = ticket.Properties.IssuedUtc;
        private readonly DateTimeOffset? expiresAt = ticket.Properties.ExpiresUtc;

        public object OriginalCommand => command;
        public Guid Actor => subject.PrincipalId;
        public Guid? TenantId => tenant;
        public Guid AmbientTenant => ambient;
        public HumanCommitSubject Subject => subject;
        public HumanAuthenticationLifetime Lifetime => lifetime;

        private void RequireCustody()
        {
            request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(owner._contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(owner._human.Subject, subject) || owner._tenants.TenantId != ambient
                || request.Items.TryGetValue(TenantContextAccessor.TenantIdItemKey, out var currentRequestTenant) != requestTenantPresent
                || requestTenantPresent && (currentRequestTenant is not Guid currentTenant || currentTenant != requestTenant)
                || !ReferenceEquals(request.RequestServices, requestServices)
                || request.Request.Method != method || request.Request.Path != path
                || request.Request.PathBase != pathBase || request.Request.QueryString != query
                || request.Request.Headers.Authorization.ToString() != authorization
                || request.Request.Headers.Cookie.ToString() != cookie
                || request.Request.Headers.Count(header => SupportAccessHeaderNames.IsSupportAccessHeader(header.Key)) != support.Count
                || support.Any(header => request.Request.Headers[header.Key].ToString() != header.Value))
                throw Denied();
        }

        public async Task RequireSourcesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RequireCustody();
            var current = await request.AuthenticateAsync();
            // Reauthentication may wrap the same native credential in a new ticket.
            if (!current.Succeeded || current.Ticket?.AuthenticationScheme != scheme || current.Principal is null
                || !ticketIdentities.SequenceEqual(current.Principal.Identities)
                || !ticketClaims.SequenceEqual(current.Principal.Claims)
                || current.Properties?.IssuedUtc != issuedAt || current.Properties?.ExpiresUtc != expiresAt
                || !ReferenceEquals(await owner._lifetimes.ObserveAsync(principal, token), lifetime)) throw Denied();
            if (JsonSerializer.Serialize(await principalFence.ObserveAsync(token)) != originalPrincipal
                || JsonSerializer.Serialize(await supportFence.ObserveAsync(token)) != originalSupport)
                throw Denied();
            RequireCustody();
        }

        public void RequireTime()
        {
            RequireCustody();
            if (!lifetime.IsValidAt(owner._clock.GetUtcNow())) throw Denied();
            var supportContext = HumanSupportAccessProjection.Context(supportFacts, owner._clock.GetUtcNow());
            if (!ReferenceEquals(supportFacts.Forwarding.Subject, subject)
                || supportContext.WasForwarded && (!supportContext.IsActive || !supportContext.AllowsWrites
                    || tenant.HasValue && supportContext.TargetTenantId != tenant)) throw Denied();
        }

        public async Task<bool> ObserveProviderAsync(StorageConfigurationState state, CancellationToken token)
        {
            owner._services.GetRequiredService<IStorageConfigurationWriter>().RequireIdle();
            if (providerObserved) throw Denied();
            providerObserved = true;
            var selection = state.Resolve(GovernanceSettingKeys.Storage.Provider, tenant.HasValue && state.TenantOverridesAllowed);
            string provider = SettingValueSerializer.DeserializeString(selection?.Value, StorageProviders.Local).Trim().ToLowerInvariant();
            bool infer = provider != StorageProviders.S3Compatible
                && (selection is null || selection.Source is SettingSource.SystemDefault or SettingSource.SystemLocked);
            string Text(string key) => SettingValueSerializer.DeserializeString(state.Resolve(key, true)?.Value, "");
            if (!infer || string.IsNullOrWhiteSpace(Text(GovernanceSettingKeys.Storage.Endpoint))
                || string.IsNullOrWhiteSpace(Text(GovernanceSettingKeys.Storage.BucketName)))
            {
                references = [];
                return false;
            }
            var resolver = owner._services.GetRequiredService<IRetainedSecretResolver>();
            var retained = new List<RetainedSecretReference>();
            var results = new List<SecretResolutionResult>();
            using var bindings = JsonDocument.Parse(state.Bindings);
            foreach (string secretKey in new[] { SecretDefinitionRegistry.Keys.Storage.AccessKeyId, SecretDefinitionRegistry.Keys.Storage.SecretAccessKey })
            {
                var reference = await resolver.CaptureAsync(secretKey, ambient, token);
                var rows = bindings.RootElement.EnumerateArray().Where(row =>
                    row.GetProperty("SettingKey").GetString() == reference.SettingKey
                    && row.GetProperty("Qualifier").GetString() == "").ToArray();
                var winner = rows.FirstOrDefault(row => row.GetProperty("SettingScopeId").GetInt32() == 2
                    && row.GetProperty("ScopeId").GetGuid() == ambient);
                if (winner.ValueKind == JsonValueKind.Undefined)
                    winner = rows.FirstOrDefault(row => row.GetProperty("SettingScopeId").GetInt32() == 1);
                if (winner.ValueKind == JsonValueKind.Undefined)
                {
                    if (reference.BindingId is not null) throw Denied();
                }
                else if (reference.BindingId != winner.GetProperty("Id").GetGuid()
                    || (int)reference.SourceType != winner.GetProperty("SecretSourceTypeId").GetInt32()
                    || reference.EnvironmentVariableName != winner.GetProperty("EnvironmentVariableName").GetString()
                    || reference.InfisicalEnvironment != winner.GetProperty("InfisicalEnvironment").GetString()
                    || reference.InfisicalPath != winner.GetProperty("InfisicalPath").GetString()
                    || reference.InfisicalKey != winner.GetProperty("InfisicalKey").GetString())
                    throw Denied();
                retained.Add(reference);
                var result = await ResolveExternalAsync(reference, token);
                results.Add(result);
                // Preserve the actual S3 resolver's absence short circuit. Do not contact
                // a second provider source that the original projection did not need.
                if (!result.IsResolved || string.IsNullOrWhiteSpace(result.Value)) break;
            }
            references = retained.ToArray();
            originalExternal = ExternalSnapshot(results);
            return results.Count == 2 && results.All(result => result.IsResolved && !string.IsNullOrWhiteSpace(result.Value));
        }

        private async Task<SecretResolutionResult> ResolveExternalAsync(RetainedSecretReference reference, CancellationToken token)
        {
            owner._services.GetRequiredService<IStorageConfigurationWriter>().RequireIdle();
            await RequireSourcesAsync(token);
            var result = await owner._services.GetRequiredService<IRetainedSecretResolver>().ResolveAsync(reference, token);
            if (result.Status is not (SecretResolutionStatus.Resolved or SecretResolutionStatus.Unconfigured)) throw Denied();
            RequireTime();
            return result;
        }

        private async Task<string> ReadExternalAsync(CancellationToken token)
        {
            var results = new List<SecretResolutionResult>();
            foreach (var reference in references ?? throw Denied())
            {
                var result = await ResolveExternalAsync(reference, token);
                results.Add(result);
                if (!result.IsResolved || string.IsNullOrWhiteSpace(result.Value)) break;
            }
            return ExternalSnapshot(results);
        }

        private static string ExternalSnapshot(IEnumerable<SecretResolutionResult> results) =>
            JsonSerializer.Serialize(results.Select(result => new
            {
                result.Status,
                result.Value,
                result.Source,
                result.Scope,
                result.ScopeId,
                Revision = result.Secret?.BindingRevision
            }));

        public async Task RequireExternalSourcesAsync(CancellationToken token)
        {
            if (!providerObserved || references is null) throw Denied();
            owner._services.GetRequiredService<IStorageConfigurationWriter>().RequireIdle();
            if (references.Length != 0 && await ReadExternalAsync(token) != originalExternal) throw Denied();
            await RequireSourcesAsync(token);
            RequireTime();
        }
    }
}

public static class StorageConfigurationAuthorityRegistration
{
    public static IServiceCollection AddStorageConfigurationAuthority(this IServiceCollection services)
    {
        services.TryAddScoped<IStorageConfigurationAuthority, StorageConfigurationAuthority>();
        return services;
    }
}
