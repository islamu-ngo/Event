// ABOUTME: Verifies the BFF onboarding probe consumes the generated status resource contract.
// ABOUTME: Locks fail-closed classification to canonical state, mode, provider, and generation fields.

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffOnboardingStatusProviderTests
{
    [Test]
    public async Task GetStatusAsync_ClassifiesConfiguredPending_FromGeneratedTypedProperties()
    {
        var resource = new HalResourceOfInstanceOnboardingStatusDto
        {
            IsCompleted = false,
            State = "ConfiguredAdministratorPending",
            Mode = "ConfiguredAdministrator",
            Provider = "Keycloak",
            Generation = 8
        };

        using var context = CreateContext(resource);
        var status = await context.Provider.GetStatusAsync();

        await Assert.That(status.Disposition)
            .IsEqualTo(BffOnboardingDisposition.ConfiguredAdministratorPending);
        await Assert.That(status.State).IsEqualTo("ConfiguredAdministratorPending");
        await Assert.That(status.Mode).IsEqualTo("ConfiguredAdministrator");
        await Assert.That(status.Provider).IsEqualTo("Keycloak");
        await Assert.That(status.Generation).IsEqualTo(8L);
    }

    [Test]
    [Arguments("InteractivePending", "Interactive", null, BffOnboardingDisposition.InteractivePending)]
    [Arguments("InteractivePending", "Interactive", "Local", BffOnboardingDisposition.InteractivePending)]
    [Arguments("InteractivePending", "Interactive", "Keycloak", BffOnboardingDisposition.InteractivePending)]
    [Arguments("InteractivePending", "Interactive", "Atproto", BffOnboardingDisposition.InteractivePending)]
    [Arguments("ConfiguredAdministratorPending", "ConfiguredAdministrator", "Atproto", BffOnboardingDisposition.ConfiguredAdministratorPending)]
    public async Task GetStatusAsync_ClassifiesCanonicalPendingStates(
        string state,
        string mode,
        string? provider,
        BffOnboardingDisposition expected)
    {
        using var context = CreateContext(new HalResourceOfInstanceOnboardingStatusDto
        {
            IsCompleted = false,
            State = state,
            Mode = mode,
            Provider = provider,
            Generation = 3
        });

        var status = await context.Provider.GetStatusAsync();

        await Assert.That(status.Disposition).IsEqualTo(expected);
    }

    [Test]
    public async Task GetStatusAsync_ClassifiesCompletedConfiguredStatus_WhenProviderIsNull()
    {
        using var context = CreateContext(new HalResourceOfInstanceOnboardingStatusDto
        {
            IsCompleted = true,
            State = "Completed",
            Mode = "ConfiguredAdministrator",
            Provider = null,
            Generation = 13
        });

        var status = await context.Provider.GetStatusAsync();

        await Assert.That(status.Disposition).IsEqualTo(BffOnboardingDisposition.Completed);
        await Assert.That(status.Provider).IsNull();
    }

    [Test]
    [Arguments(false, "ConfiguredAdministratorPending", "ConfiguredAdministrator", null, 2L)]
    [Arguments(false, "ConfiguredAdministratorPending", "ConfiguredAdministrator", "Google", 2L)]
    [Arguments(false, "InteractivePending", "Interactive", "Unknown", 2L)]
    [Arguments(false, "Pending", "Interactive", null, 2L)]
    [Arguments(true, "Completed", "Headless", null, 2L)]
    [Arguments(true, "Completed", "ConfiguredAdministrator", "Google", 2L)]
    [Arguments(false, "Completed", "Interactive", null, 2L)]
    [Arguments(false, "InteractivePending", "Interactive", null, 0L)]
    public async Task GetStatusAsync_FailsClosed_ForInconsistentGeneratedValues(
        bool isCompleted,
        string state,
        string mode,
        string? provider,
        long generation)
    {
        using var context = CreateContext(new HalResourceOfInstanceOnboardingStatusDto
        {
            IsCompleted = isCompleted,
            State = state,
            Mode = mode,
            Provider = provider,
            Generation = generation
        });

        var status = await context.Provider.GetStatusAsync();

        await Assert.That(status.Disposition).IsEqualTo(BffOnboardingDisposition.Closed);
    }

    private static TestContext CreateContext(
        HalResourceOfInstanceOnboardingStatusDto resource)
    {
        IInstanceOnboardingClient apiClient = Substitute.For<IInstanceOnboardingClient>();
        apiClient.GetInstanceOnboardingStatusAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(resource);

        return new TestContext(apiClient);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public TestContext(IInstanceOnboardingClient apiClient)
        {
            var services = new ServiceCollection();
            services.AddSingleton(apiClient);
            _serviceProvider = services.BuildServiceProvider();
            Provider = new BffOnboardingStatusProvider(
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _cache,
                NullLogger<BffOnboardingStatusProvider>.Instance);
        }

        public BffOnboardingStatusProvider Provider { get; }

        public void Dispose()
        {
            _cache.Dispose();
            _serviceProvider.Dispose();
        }
    }
}
