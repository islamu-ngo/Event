using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Management;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Management;

public sealed class ManagedControlPlaneRegistrationFailureTests
{
    [Test]
    [Arguments(null, false)]
    [Arguments("", false)]
    [Arguments(" ", false)]
    [Arguments(null, true)]
    [Arguments("", true)]
    [Arguments(" ", true)]
    public async Task FirstRegistrationWithoutRequiredCredentialReturnsPendingWithoutCreatingRegistration(
        string? unavailableCredential, bool missingOutbound)
    {
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var registrations = new ManagedControlPlaneRegistrationRepository(context);
        var secrets = Substitute.For<ISecretResolver>();
        string availableCredential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        secrets.ResolveAsync(ManagedControlPlaneContract.CredentialSecretSettingKey, null,
                Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                ManagedControlPlaneContract.CredentialSecretSettingKey,
                JsonSerializer.Serialize(new
                {
                    ControlPlaneToEventSecret = missingOutbound ? availableCredential : unavailableCredential,
                    EventToControlPlaneSecret = missingOutbound ? unavailableCredential : availableCredential
                }),
                SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow)));

        var services = new ServiceCollection();
        services.AddOptions<DeploymentSettings>();
        services.AddDistributedMemoryCache();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<DeploymentModeProvider>();
        await using var provider = services.BuildIsolatedServiceProvider();
        var handler = new TriggerManagedControlPlaneRegistrationCommandHandler(
            Options.Create(new ManagedControlPlaneOptions { Enabled = true }),
            registrations,
            new InstanceBootstrapStateRepository(context),
            new SecretBindingRepository(context),
            secrets,
            provider.GetRequiredService<DeploymentModeProvider>(),
            Substitute.For<IManagedControlPlaneRegistrationClient>(),
            NullLogger<TriggerManagedControlPlaneRegistrationCommandHandler>.Instance);

        var result = await handler.ExecuteAsync(new TriggerManagedControlPlaneRegistrationCommand());

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.State).IsEqualTo("Pending");
        await Assert.That(result.FailureCode).IsEqualTo("registration_secret_unavailable");
        await Assert.That(result.RegistrationAttemptId).IsNull();
        await Assert.That(await registrations.GetCurrentAsync()).IsNull();
    }
}
