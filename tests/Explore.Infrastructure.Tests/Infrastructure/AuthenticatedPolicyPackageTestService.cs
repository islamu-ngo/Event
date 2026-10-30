using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Application.Utilities;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Explore.Tests.Shared.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

/// <summary>
/// Package/transport scenarios retain their external ports, but every write now enters
/// the real Local-authenticated selected Global owner and the production custody store.
/// </summary>
internal sealed class AuthenticatedPolicyPackageTestService(
    IOptions<CerbosPolicyPackageOptions> options, IOptions<CerbosAdminApiSettings> admin,
    ICerbosConfigResolver resolver, CerbosAdminEndpointValidator validator, IHttpClientFactory clients,
    ILogger<CerbosPolicyPackageService> logger, IOptions<CerbosSettings> cerbos)
{
    private CerbosPolicyPackageService Observation => new(options, admin, resolver, validator, clients, logger,
        Substitute.For<IEventResourcePolicyPublicationFence>(), cerbos,
        new ServiceCollection().BuildServiceProvider(), Options.Create(new AuthorizationProviderDeploymentOptions()));

    public Task<PolicyPackageManifest> BuildManifestAsync(CancellationToken token = default) => Observation.BuildManifestAsync(token);
    public Task<PolicyPackageArchive> ExportArchiveAsync(CancellationToken token = default) => Observation.ExportArchiveAsync(token);
    public Task<bool> IsPublicationReadyAsync(CancellationToken token = default) => Observation.IsPublicationReadyAsync(token);
    public Task<PolicyPackageStatusResult> GetStatusAsync(CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null, bool instanceTargetOnly = false) =>
        Observation.GetStatusAsync(cancellationToken, oneTimeCredentials, instanceTargetOnly);

    public Task<PolicyPackagePublishResult> PublishAsync(CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null) =>
        PublishAsync(false, oneTimeCredentials, cancellationToken);

    public Task<PolicyPackagePublishResult> PublishInstanceAsync(CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null) =>
        PublishAsync(true, oneTimeCredentials, cancellationToken);

    private async Task<PolicyPackagePublishResult> PublishAsync(bool managed,
        PolicyPackageAdminCredentials? credentials, CancellationToken token)
    {
        var command = Command(credentials);
        // Credential/package failures are independent observations and occur before the
        // production service requests an authenticated mutation owner.
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            return await Observation.PublishAsync(command, token);
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        ExploreDbContext CreateContext() => CreateContextFor(database);
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(CreateContext);
        if (!managed)
        {
            var configuration = await resolver.ResolveAsync(token);
            if (configuration is { IsInstanceDefault: false })
            {
                await SetValueAsync(authenticated, null, GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, true);
                await SetValueAsync(authenticated, null, GovernanceSettingKeys.Cerbos.Mode, "custom_endpoint");
                await SetValueAsync(authenticated, null, GovernanceSettingKeys.Cerbos.CustomEndpoint, configuration.Endpoint);
                await SetValueAsync(authenticated, null, GovernanceSettingKeys.Cerbos.CustomAdminEndpoint, configuration.AdminEndpoint ?? "");
            }
        }
        return await authenticated.RunAsync(null, session =>
        {
            var selected = new ResourceProviderTestServices(session.Services);
            var publisher = new CerbosPolicyPackageService(options, admin, resolver, validator, clients, logger,
                selected.Control, cerbos, selected,
                Options.Create(new AuthorizationProviderDeploymentOptions { Provider = managed ? "cerbos" : null }));
            selected.Publisher = publisher;
            return publisher.PublishAsync(command, token);
        }, cancellationToken: token);
    }

    internal static SyncAuthorizationPolicyPackageCommand Command(PolicyPackageAdminCredentials? credentials) => new()
    {
        Request = new() { AdminUsername = credentials?.Username, AdminPassword = credentials?.Password }
    };

    internal static async Task SetValueAsync<T>(AuthenticatedSettingsFixture authentication, Guid? tenant, string key, T value)
    {
        string stored = SettingValueSerializer.Serialize(value);
        string input = value is string text ? text : stored;
        if (key is GovernanceSettingKeys.Cerbos.CustomEndpoint or GovernanceSettingKeys.Cerbos.CustomAdminEndpoint)
            stored = SettingValueSerializer.Serialize(GrpcEndpointNormalizer.Normalize(input));
        await authentication.RunAsync(tenant, session => session.ValueAsync(key, input, stored, async token =>
        {
            if (tenant is { } target)
                await session.Services.GetRequiredService<ITenantSettingRepository>()
                    .SetValueAsync(target, key, stored, token, actorId: session.ActorId);
            else
            {
                var definition = SettingRegistry.Get(key)!;
                var repository = session.Services.GetRequiredService<ISystemSettingRepository>();
                var setting = await repository.GetByKey(key, token) ?? new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = stored,
                    ValueType = definition.ValueType,
                    Description = definition.Description,
                    Category = definition.Category,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = session.ActorId
                };
                setting.Value = stored;
                setting.UpdatedBy = session.ActorId;
                await repository.UpsertAsync(setting, token);
            }
            return true;
        }));
    }

    internal static ExploreDbContext CreateContextFor(SmtpSettingsDatabase database)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = database.Context.Database.GetDbConnection().DataSource
        });
        return new(options.UseSnakeCaseNamingConvention().Options);
    }
}

/// <summary>
/// Reconstructs only this production family against the fixture's actual selected scope,
/// so transport tests can inject external HTTP ports without replacing human admission.
/// </summary>
internal sealed class ResourceProviderTestServices : IServiceProvider
{
    private readonly IServiceProvider selected;
    internal IPolicyPackageService? Publisher { get; set; }
    internal HumanSettingsMutationPreparation Preparation { get; }
    internal EventResourceProviderOriginalStore Store { get; }
    internal EventResourceProviderControlPlane Control { get; }

    internal ResourceProviderTestServices(IServiceProvider selected, TimeProvider? clock = null)
    {
        this.selected = selected;
        Preparation = new(this, selected.GetRequiredService<IHumanCommitScopePreparation>(),
            selected.GetRequiredService<IHttpContextAccessor>(), selected.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            selected.GetRequiredService<ITenantContext>(), clock ?? selected.GetRequiredService<TimeProvider>(),
            selected.GetRequiredService<CerbosAuthorizationService>(), selected.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            selected.GetRequiredService<IOptions<CerbosSettings>>());
        Store = new(selected.GetRequiredService<ExploreDbContext>(), this);
        Control = new(selected.GetRequiredService<ISystemSettingRepository>(), selected.GetRequiredService<ISettingMutationLock>(),
            selected.GetRequiredService<IAdminContext>(), Preparation, Store);
    }

    public object? GetService(Type serviceType) => serviceType == typeof(IHumanSettingsMutationPreparation) ? Preparation
        : serviceType == typeof(IEventResourceProviderOriginalStore) ? Store
        : serviceType == typeof(EventResourceProviderControlPlane) ? Control
        : serviceType == typeof(IPolicyPackageService) ? Publisher ?? selected.GetRequiredService<IPolicyPackageService>()
        : selected.GetService(serviceType);
}
