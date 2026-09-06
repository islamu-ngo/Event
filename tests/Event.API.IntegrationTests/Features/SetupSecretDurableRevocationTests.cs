// ABOUTME: Cross-replica regression tests for durable setup-secret revocation at API acceptance boundaries.
// ABOUTME: Proves a provider with stale local state cannot validate, authenticate, or mutate after shared completion.

using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("SetupSecretDurableRevocation")]
public sealed class SetupSecretDurableRevocationTests
{
    private static readonly string SetupSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    [Test]
    public async Task ValidateEndpoint_StaleReplicaAfterSharedCompletion_ReturnsGone()
    {
        using var scenario = await TwoReplicaScenario.CreateAsync();
        scenario.CompleteOnOtherReplica();
        await using var factory = scenario.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/instanceonboarding/validate-secret",
            new { secret = SetupSecret });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    [Test]
    public async Task SetupSecretAuthentication_StaleReplicaAfterSharedCompletion_ReturnsGone()
    {
        using var scenario = await TwoReplicaScenario.CreateAsync();
        scenario.CompleteOnOtherReplica();
        await using var factory = scenario.CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/instance/settings/auth-provider");
        request.Headers.Add("X-Setup-Secret", SetupSecret);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    [Test]
    public async Task SettingsMutation_StaleReplicaAfterSharedCompletion_ReturnsGoneWithoutMutation()
    {
        var configurationService = Substitute.For<IAuthProviderConfigurationService>();
        configurationService.ReadConfigurationAsync().Returns(new AuthProviderConfigurationDto());
        using var scenario = await TwoReplicaScenario.CreateAsync(configurationService);
        scenario.CompleteOnOtherReplica();
        await using var factory = scenario.CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/instance/settings/auth-provider")
        {
            Content = JsonContent.Create(CreateValidPatch())
        };
        request.Headers.Add("X-Setup-Secret", SetupSecret);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
        await configurationService.DidNotReceiveWithAnyArgs().ApplyConfigurationAsync(default!);
    }

    private static PatchAuthProviderConfigurationDto CreateValidPatch() => new()
    {
        Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()
        {
            GoogleSsoEnabled = true,
            GoogleClientId = "client-id",
            GoogleClientSecret = "client-secret"
        })
    };

    private sealed class TwoReplicaScenario : IDisposable
    {
        private static readonly DateTime PendingAt = new(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        private readonly IAuthProviderConfigurationService? _configurationService;
        private readonly SetupSecretProvider _completingReplica;
        private readonly SetupSecretProvider _staleReplica;
        private readonly InstanceBootstrapState _sharedState;

        private TwoReplicaScenario(
            IAuthProviderConfigurationService? configurationService,
            SetupSecretProvider completingReplica,
            SetupSecretProvider staleReplica,
            InstanceBootstrapState sharedState)
        {
            _configurationService = configurationService;
            _completingReplica = completingReplica;
            _staleReplica = staleReplica;
            _sharedState = sharedState;
        }

        public static async Task<TwoReplicaScenario> CreateAsync(
            IAuthProviderConfigurationService? configurationService = null)
        {
            InstanceBootstrapState sharedState = InstanceBootstrapState.CreateInteractivePending(
                Guid.Parse("01991e80-8c00-7000-8000-000000000001"),
                DeploymentMode.SingleTenant,
                PendingAt);
            var repository = Substitute.For<IInstanceBootstrapStateRepository>();
            repository.GetCurrent(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<InstanceBootstrapState?>(sharedState));
            IServiceScopeFactory scopeFactory = CreateScopeFactory(repository);
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SETUP_SECRET"] = SetupSecret,
                    ["Hosting:ReplicaCount"] = "2"
                })
                .Build();
            var completingReplica = new SetupSecretProvider(configuration, scopeFactory);
            var staleReplica = new SetupSecretProvider(configuration, scopeFactory);
            await completingReplica.InitializeAsync();
            await staleReplica.InitializeAsync();
            return new(configurationService, completingReplica, staleReplica, sharedState);
        }

        public void CompleteOnOtherReplica()
        {
            _sharedState.CompleteInteractive(
                Guid.Parse("01991e80-8c00-7000-8000-000000000002"),
                PendingAt.AddMinutes(1));
            _completingReplica.Lock();
        }

        public ExternalApiPhase0WebApplicationFactory CreateFactory() => new()
        {
            DeploymentMode = DeploymentMode.SingleTenant,
            SetupSecretProviderOverride = _staleReplica,
            AuthProviderConfigurationServiceOverride = _configurationService
        };

        public void Dispose() => _completingReplica.Dispose();

        private static IServiceScopeFactory CreateScopeFactory(IInstanceBootstrapStateRepository repository)
        {
            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.GetService(typeof(IInstanceBootstrapStateRepository)).Returns(repository);
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(scope);
            return scopeFactory;
        }
    }
}
