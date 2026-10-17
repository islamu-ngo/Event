using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.Settings;
using Explore.Application.Settings;
using Explore.Application.Services;
using Explore.Application.Utilities;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

public sealed partial class CerbosPolicyPackageService
{
    private OriginalPublicationSelection? _issuedPublication;

    public async Task<IEventResourceProviderPublicationSelection> SelectOriginalPublicationAsync(
        SyncAuthorizationPolicyPackageCommand command, EventResourceProviderPublicationInput input, CancellationToken token)
    {
        if (_issuedPublication is not null || command.Request.InspectOnly) throw PublicationCustodyDenied();
        string declarations = PublicationDeclarations();
        var target = await ResolveOriginalTargetAsync(command, input, token);
        if (declarations != PublicationDeclarations()) throw PublicationCustodyDenied();
        return _issuedPublication = new(this, command, input, target, declarations);
    }

    public async Task RequireOriginalSelectionAsync(SyncAuthorizationPolicyPackageCommand command,
        IEventResourceProviderPublicationSelection selection, CancellationToken token)
    {
        var selected = RequireSelection(command, selection);
        var current = await ResolveOriginalTargetAsync(command, selected.Input, token);
        if (selected.Declarations != PublicationDeclarations() || !SameTarget(selected.Target, current))
            throw PublicationCustodyDenied();
        selected.Require();
    }

    private OriginalPublicationSelection RequireSelection(SyncAuthorizationPolicyPackageCommand command,
        IEventResourceProviderPublicationSelection selection)
    {
        if (selection is not OriginalPublicationSelection selected || !ReferenceEquals(selected, _issuedPublication)
            || !ReferenceEquals(selected.Owner, this) || !ReferenceEquals(selected.Command, command))
            throw PublicationCustodyDenied();
        selected.Require();
        return selected;
    }

    private AdminApiTarget ClaimCommittedSelection(SyncAuthorizationPolicyPackageCommand command,
        IEventResourceProviderPublicationSelection selection)
    {
        var selected = RequireSelection(command, selection);
        selected.Claim();
        return selected.Target;
    }

    private string PublicationDeclarations() => JsonSerializer.Serialize(new
    {
        Provider = _authorizationDeployment.GetProvider(),
        _instanceCerbosSettings.GrpcEndpoint,
        Endpoints = _adminApiSettings.Endpoints.ToArray()
    });

    private async Task<AdminApiTarget> ResolveOriginalTargetAsync(SyncAuthorizationPolicyPackageCommand command,
        EventResourceProviderPublicationInput input, CancellationToken token)
    {
        if (input.TenantId == Guid.Empty || input.Settings.IsDefault
            || !input.Settings.Select(row => row.Key).SequenceEqual(HumanAuthorizationRouteFacts.Keys))
            throw PublicationCustodyDenied();
        var credentials = new PolicyPackageAdminCredentials(
            command.Request.AdminUsername ?? throw PublicationCustodyDenied(),
            command.Request.AdminPassword ?? throw PublicationCustodyDenied());
        if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            throw PublicationCustodyDenied();
        var rows = input.Settings.ToDictionary(row => row.Key, StringComparer.Ordinal);
        string? Value(string key) => HierarchicalSettingMerge.Resolve(key,
            new SettingMergeFacts(rows[key].System, rows[key].Tenant, null, null, null, true, null))?.Value;
        if (_authorizationDeployment.GetProvider() is not null
            || !SettingValueSerializer.Deserialize(Value(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled), false)
            || SettingValueSerializer.Deserialize<string?>(Value(GovernanceSettingKeys.Cerbos.Mode), null)
                ?.Trim().ToLowerInvariant() != "custom_endpoint")
        {
            var instance = await ResolveInstanceAdminApiTargetAsync(credentials, token);
            return instance.Target ?? throw PublicationCustodyDenied();
        }
        string? endpoint = SettingValueSerializer.Deserialize<string?>(Value(GovernanceSettingKeys.Cerbos.CustomEndpoint), null);
        string? admin = SettingValueSerializer.Deserialize<string?>(Value(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint), null);
        if (string.IsNullOrWhiteSpace(admin)
            || !_adminEndpointValidator.TryNormalize(admin, isByo: true, out var validated, out _))
            throw PublicationCustodyDenied();
        return new([validated], credentials.Username, credentials.Password, AdminApiTargetSource.Byo,
            string.IsNullOrWhiteSpace(endpoint) ? string.Empty : GrpcEndpointNormalizer.Normalize(endpoint));
    }

    private static bool SameTarget(AdminApiTarget left, AdminApiTarget right) =>
        left.Source == right.Source && left.GrpcEndpoint == right.GrpcEndpoint && left.Endpoints.SequenceEqual(right.Endpoints);

    private async Task<IEventResourceProviderPublicationSelection> SelectPublicationForWriteAsync(
        SyncAuthorizationPolicyPackageCommand command, CancellationToken token)
    {
        var request = _services.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw PublicationCustodyDenied();
        var authentication = await request.AuthenticateAsync();
        if (!authentication.Succeeded || authentication.Ticket is null || authentication.Principal is null
            || !authentication.Principal.Identities.Any(identity => identity.IsAuthenticated && request.User.Identities.Contains(identity)))
            throw PublicationCustodyDenied();
        if (authentication.Ticket.AuthenticationScheme != ApiAuthenticationSchemeNames.SetupSecret)
        {
            if (!ReferenceEquals(_resourcePublicationFence, _services.GetRequiredService<EventResourceProviderControlPlane>()))
                throw PublicationCustodyDenied();
            return await _resourcePublicationFence.BeginPublicationAsync(command, token);
        }

        // Setup has no human deployment-activation authority. Its original ticket and
        // pending-generation lease permit only a validated, unbound foreground publication.
        var authority = await _services.GetRequiredService<IProviderConfigurationSetupAuthority>()
            .CaptureAsync(new TrustedSetupSettingsSource(null, null, null), token);
        return await _services.GetRequiredService<ISettingMutationLock>().ExecuteOrderedGroupsAsync(
            [[EventResourceProviderBindingDocument.SettingKey]], async cancellation =>
            {
                var settings = _services.GetRequiredService<ISystemSettingRepository>();
                var tenants = _services.GetRequiredService<ITenantSettingRepository>();
                Guid tenant = _services.GetRequiredService<ITenantContext>().TenantId;
                async Task<(EventResourceProviderPublicationInput Input, string Image)> ReadAsync()
                {
                    var facts = ImmutableArray.CreateBuilder<HumanAuthorizationRouteSettingFact>();
                    var image = new List<object?>();
                    foreach (string key in HumanAuthorizationRouteFacts.Keys)
                    {
                        var system = await settings.GetByKey(key, cancellation);
                        var currentTenant = await tenants.GetByTenantAndKey(tenant, key, cancellation);
                        image.Add(system);
                        image.Add(currentTenant);
                        facts.Add(new(key, system is null ? null : new(system.Value, system.ValueType, system.IsLocked,
                            system.Description, system.Category, system.AllowedValues),
                            currentTenant is null ? null : new(currentTenant.Value, currentTenant.IsLocked)));
                    }
                    image.Add(await settings.GetByKey(EventResourceProviderBindingDocument.SettingKey, cancellation));
                    return (new(tenant, facts.ToImmutable()), JsonSerializer.Serialize(image));
                }
                await authority.RequireCurrentAsync(cancellation);
                var observed = await ReadAsync();
                var selection = await SelectOriginalPublicationAsync(command, observed.Input, cancellation);
                var document = EventResourceProviderBindingDocument.Parse((await settings.GetByKey(
                    EventResourceProviderBindingDocument.SettingKey, cancellation))?.Value);
                if (GrpcEndpointNormalizer.IsValid(selection.GrpcEndpoint)
                    && document.Deployments.Any(binding => binding.Endpoints.Contains(
                        EventResourceProviderBindingDocument.NormalizeEndpoint(selection.GrpcEndpoint), StringComparer.Ordinal)))
                    throw new UnauthorizedAccessException("Setup cannot publish to a bound resource deployment.");
                if ((await ReadAsync()).Image != observed.Image) throw PublicationCustodyDenied();
                await RequireOriginalSelectionAsync(command, selection, cancellation);
                await authority.RequireCurrentAsync(cancellation);
                authority.RequireCurrentTime();
                return selection;
            }, token);
    }

    private sealed class OriginalPublicationSelection(
        CerbosPolicyPackageService owner, SyncAuthorizationPolicyPackageCommand command,
        EventResourceProviderPublicationInput input, AdminApiTarget target, string declarations)
        : IEventResourceProviderPublicationSelection
    {
        private int ended;
        internal CerbosPolicyPackageService Owner => owner;
        internal SyncAuthorizationPolicyPackageCommand Command => command;
        internal EventResourceProviderPublicationInput Input => input;
        internal AdminApiTarget Target => target;
        internal string Declarations => declarations;
        public string GrpcEndpoint => GrpcEndpointNormalizer.IsValid(target.GrpcEndpoint)
            ? EventResourceProviderBindingDocument.NormalizeEndpoint(target.GrpcEndpoint) : target.GrpcEndpoint;
        internal void Require()
        {
            if (Volatile.Read(ref ended) != 0) throw PublicationCustodyDenied();
        }
        internal void Claim()
        {
            if (Interlocked.Exchange(ref ended, 1) != 0) throw PublicationCustodyDenied();
        }
        internal void Close() => Interlocked.Exchange(ref ended, 1);
        public override string ToString() => nameof(OriginalPublicationSelection);
    }

    private static UnauthorizedAccessException PublicationCustodyDenied() =>
        new("The original authenticated publication command and validated target selection are required.");
}
