using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Application.Utilities;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("non-administrator")]
    [Arguments("setup-inactive")]
    [Arguments("invalid-configuration")]
    [Arguments("local")]
    [Arguments("deployment-owned")]
    [Arguments("cerbos-serving")]
    [Arguments("admin-endpoint-rejected")]
    [Arguments("cerbos-not-serving")]
    public async Task AuthorizationHandlerRetainsOriginalBusinessScenariosWithPrivateObservation(string scenario)
    {
        await using var transport = await AuthorizationHandlerHealthTransportFixture.CreateAsync(
            serving: scenario != "cerbos-not-serving");
        var observer = new NativeProviderObserver();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => RegisterNativeProvider(services, observer));
        if (scenario != "non-administrator") await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = scenario == "deployment-owned" ? "cerbos" : ""
            })));
        await using var request = await ProviderRequestAsync(host.Services, await fixture.SignInAsync(), null);
        var service = new AuthorizationHandlerObservationRecorder(
            request.Services.GetRequiredService<IAuthorizationProviderConfigurationService>());
        var handler = ActivatorUtilities.CreateInstance<UpdateAuthorizationProviderConfigurationCommandHandler>(request.Services, service);
        bool cerbos = scenario is "invalid-configuration" or "cerbos-serving" or "admin-endpoint-rejected" or "cerbos-not-serving";
        var patch = new AuthorizationProviderConfigurationWriteDto
        {
            Provider = cerbos ? "cerbos" : "local",
            CerbosGrpcEndpoint = scenario == "invalid-configuration" ? "" : cerbos ? transport.BareEndpoint : "",
            CerbosAdminEndpoint = scenario == "admin-endpoint-rejected" ? "https://localhost:3592" : ""
        };
        var command = new UpdateAuthorizationProviderConfigurationCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(patch) }
        };
        string originalCommand = JsonSerializer.Serialize(command);
        await using var initial = fixture.Native.CreateDatabase();
        var before = await initial.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey).ToArrayAsync(transport.Token);
        var result = scenario == "setup-inactive"
            ? await handler.ExecuteAsync(new UpdateAuthorizationProviderConfigurationDuringSetupCommand { Patch = command.Patch }, transport.Token)
            : await handler.ExecuteAsync(command, transport.Token);
        bool success = scenario is "local" or "cerbos-serving";
        await Assert.That(result.IsSuccess).IsEqualTo(success);
        await Assert.That(JsonSerializer.Serialize(command)).IsEqualTo(originalCommand);
        if (scenario is "non-administrator" or "setup-inactive")
            await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        if (scenario is "deployment-owned" or "invalid-configuration" or "admin-endpoint-rejected" or "cerbos-not-serving")
        {
            await Assert.That(result.FailureCode).IsNull();
            await Assert.That(result.Errors!.Count > 0).IsTrue();
        }
        if (scenario is "non-administrator" or "setup-inactive")
            await Assert.That(service.Observed).IsNull();
        else
        {
            var observed = service.Observed!;
            await Assert.That(ReferenceEquals(observed.OriginalCommand, command)).IsTrue();
            await Assert.That(ReferenceEquals(observed.Source.OriginalCommand, command)).IsTrue();
            await Assert.That(observed.Source is IProviderConfigurationHumanSource).IsTrue();
            await Assert.That(observed.Actor).IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(observed.AuthorizationProposed.CerbosGrpcEndpoint).IsEqualTo(patch.CerbosGrpcEndpoint);
            if (success)
                await Assert.That(ReferenceEquals(observer.Mutation, observed)).IsTrue();
        }
        await using var final = fixture.Native.CreateDatabase();
        if (!success)
        {
            var after = await final.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey).ToArrayAsync(transport.Token);
            await Assert.That(JsonSerializer.Serialize(after)).IsEqualTo(JsonSerializer.Serialize(before));
            await Assert.That(observer.Mutation).IsNull();
        }
        else
        {
            foreach (var effect in service.Observed!.Effects)
                await Assert.That(ProviderConfigurationRow.From(await final.SystemSettings.AsNoTracking()
                    .SingleAsync(row => row.SettingKey == effect.Key, transport.Token))).IsEqualTo(effect);
            await Assert.That(await final.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                .Select(row => row.Value).SingleAsync(transport.Token)).IsEqualTo(cerbos ? "\"cerbos\"" : "\"local\"");
            string endpoint = await final.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint)
                .Select(row => row.Value).SingleAsync(transport.Token);
            await Assert.That(endpoint).IsEqualTo(JsonSerializer.Serialize(cerbos ? transport.Endpoint : ""));
        }
        if (scenario is "cerbos-serving" or "admin-endpoint-rejected" or "cerbos-not-serving")
            await Assert.That(transport.Requests.All(path => path == "/grpc.health.v1.Health/Check") && !transport.Requests.IsEmpty).IsTrue();
        else
            await Assert.That(transport.Requests.IsEmpty).IsTrue();
        if (scenario == "cerbos-serving")
        {
            await Assert.That(GrpcEndpointNormalizer.Normalize("cerbosgrpc.example.com:443"))
                .IsEqualTo("https://cerbosgrpc.example.com:443");
            await Assert.That(patch.CerbosGrpcEndpoint).IsEqualTo(transport.BareEndpoint);
            await Assert.That(service.Observed!.Effects.Single(row => row.Key == GovernanceSettingKeys.Cerbos.GrpcEndpoint).Value)
                .IsEqualTo(JsonSerializer.Serialize(transport.Endpoint));
        }
    }

    [Test]
    public async Task AuthorizationHandlerSetupUsesAcceptedBootstrapWithoutAdministratorIdentity()
    {
        var observer = new NativeProviderObserver();
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = "" }));
            builder.ConfigureTestServices(services => RegisterNativeProvider(services, observer));
        });
        await using var request = await ProviderRequestAsync(host.Services, null, native.SetupSecret);
        var service = new AuthorizationHandlerObservationRecorder(
            request.Services.GetRequiredService<IAuthorizationProviderConfigurationService>());
        var handler = ActivatorUtilities.CreateInstance<UpdateAuthorizationProviderConfigurationCommandHandler>(request.Services, service);
        var command = new UpdateAuthorizationProviderConfigurationDuringSetupCommand
        {
            Patch = new() { Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(new() { Provider = "local" }) }
        };
        var result = await handler.ExecuteAsync(command, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var observed = service.Observed!;
        await Assert.That(ReferenceEquals(observed.OriginalCommand, command)).IsTrue();
        await Assert.That(ReferenceEquals(observed.Source.OriginalCommand, command)).IsTrue();
        await Assert.That(ReferenceEquals(observer.Mutation, observed)).IsTrue();
        await Assert.That(observed.Source is IProviderConfigurationSetupSource).IsTrue();
        await Assert.That(observed.Actor).IsNull();
        await using var final = native.CreateDatabase();
        foreach (var effect in observed.Effects)
        {
            var row = await final.SystemSettings.AsNoTracking().SingleAsync(item => item.SettingKey == effect.Key);
            await Assert.That(ProviderConfigurationRow.From(row)).IsEqualTo(effect);
            await Assert.That(row.UpdatedBy).IsNull();
        }
        await Assert.That(await final.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"local\"");
    }

    /// <summary>Records, but never constructs or substitutes, the real service's private observation.</summary>
    private sealed class AuthorizationHandlerObservationRecorder(IAuthorizationProviderConfigurationService inner)
        : IAuthorizationProviderConfigurationService
    {
        internal ProviderConfigurationMutation? Observed { get; private set; }
        public async Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthorizationProviderConfigurationCommand command, CancellationToken token) =>
            Observed = await inner.ObserveConfigurationAsync(command, token);
        public async Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthorizationProviderConfigurationDuringSetupCommand command, CancellationToken token) =>
            Observed = await inner.ObserveConfigurationAsync(command, token);
        public Task ApplyConfigurationAsync(ProviderConfigurationMutation observation, CancellationToken token) => inner.ApplyConfigurationAsync(observation, token);
        public Task<AuthorizationProviderConfigurationDto> ReadConfigurationAsync() => inner.ReadConfigurationAsync();
        public Task ApplyConfigurationAsync(UpdateAuthorizationProviderConfigurationCommand command, CancellationToken token = default) => inner.ApplyConfigurationAsync(command, token);
        public Task ApplySetupConfigurationAsync(UpdateAuthorizationProviderConfigurationDuringSetupCommand command, CancellationToken token = default) => inner.ApplySetupConfigurationAsync(command, token);
        public Task<bool> IsConfiguredAsync() => inner.IsConfiguredAsync();
        public Task<bool> VerifyCerbosEndpointAsync(string endpoint, CancellationToken token = default) => inner.VerifyCerbosEndpointAsync(endpoint, token);
        public Task<bool> VerifyCerbosAdminEndpointAsync(string endpoint, CancellationToken token = default) => inner.VerifyCerbosAdminEndpointAsync(endpoint, token);
        public Task<AuthorizationProviderReconciliationResult> ReconcileDeploymentProviderAsync(CancellationToken token = default) => inner.ReconcileDeploymentProviderAsync(token);
    }
}
