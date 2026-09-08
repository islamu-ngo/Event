using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Providers;
using Explore.Secrets.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Sources;

[NotInParallel]
public sealed class UserSecretsAuthoritySourceTests
{
    [Test]
    public async Task GetSecretAsync_WhenUserSecretsIsSelected_IgnoresEnvironmentCanary()
    {
        string userSecret = SecretsTestValues.CreateSecret();
        string environmentCanary = SecretsTestValues.CreateSecret();
        Environment.SetEnvironmentVariable("MAIL_SMTP_PASSWORD", environmentCanary);
        try
        {
            var authority = new UserSecretsAuthority(
                new TestHostEnvironment("Testing"),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MAIL_SMTP_PASSWORD"] = userSecret,
                }).Build());
            var source = new EnvironmentSecretSource(
                Options.Create(new SecretProviderOptions { Provider = SecretProviderType.UserSecrets }),
                authority);
            SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
                SecretDefinitionRegistry.Keys.Smtp.Password,
                SecretScope.Instance,
                scopeId: null,
                "MAIL_SMTP_PASSWORD");

            var result = await source.GetSecretAsync(binding);

            await Assert.That(result.IsResolved).IsTrue();
            await Assert.That(result.Value).IsEqualTo(userSecret);
            await Assert.That(result.Value).IsNotEqualTo(environmentCanary);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAIL_SMTP_PASSWORD", null);
        }
    }

    [Test]
    public async Task Provider_WhenUserSecretIsMissing_DoesNotFallBackToEnvironment()
    {
        const string environmentKey = "MAIL__SMTP__PASSWORD";
        Environment.SetEnvironmentVariable(environmentKey, SecretsTestValues.CreateSecret());
        try
        {
            var authority = new UserSecretsAuthority(
                new TestHostEnvironment("Testing"),
                new ConfigurationBuilder().Build());
            var provider = new EnvironmentSecretProvider(
                Substitute.For<ILogger<EnvironmentSecretProvider>>(),
                authority);
            await provider.InitializeAsync();

            string? result = await provider.GetSecretAsync("Mail:Smtp:Password");

            await Assert.That(result).IsNull();
            await Assert.That(provider.ProviderType).IsEqualTo(SecretProviderType.UserSecrets);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Explore.Secrets.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
