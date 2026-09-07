using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ConsoleOutput")]
public class SetupSecretStartupOutputTests
{
    [Test]
    public async Task ApiStartup_UnclaimedGeneratedSecret_DoesNotWriteRawValue()
    {
        var canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var provider = new SyntheticSetupSecretProvider(canary);
        var originalOut = Console.Out;
        using var capturedOutput = new StringWriter();

        Console.SetOut(capturedOutput);
        try
        {
            await using var factory = new SetupSecretStartupOutputFactory(provider);
            using var client = factory.CreateClient();

            await Assert.That(provider.ValidateSecret(canary)).IsEqualTo(true);
            await Assert.That(capturedOutput.ToString()).DoesNotContain(canary);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private sealed class SetupSecretStartupOutputFactory(ISetupSecretProvider provider)
        : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISetupSecretProvider>();
                services.AddSingleton(provider);
            });
        }
    }

    private sealed class SyntheticSetupSecretProvider(string secret) : ISetupSecretProvider
    {
        public bool IsSetupModeActive => true;
        public bool IsSetupSecretRequired => true;
        public bool IsFromEnvironmentVariable => false;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool ValidateSecret(string? candidate) => candidate == secret;
        public void Lock() { }
    }
}
