using System.Security.Cryptography;
using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Blazor.IntegrationTests.Configuration;

[NotInParallel]
public sealed class BffSessionAuthorityConfigurationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EnvironmentSelectionReplacesMergedSessionCredentialEvenWhenAbsent(bool injected)
    {
        string selected = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string foreign = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string? previous = Environment.GetEnvironmentVariable("BFF_SESSION_REDIS_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("BFF_SESSION_REDIS_PASSWORD", injected ? selected : null);
            var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment",
                ["BFF_SESSION_REDIS_PASSWORD"] = foreign,
                ["Bff:SessionAuthority:Password"] = foreign,
                ["ConnectionStrings:cache"] = foreign,
                ["BFF_SESSION_REDIS_ENDPOINT"] = "rediss://session.example.test:6380",
                ["BFF_SESSION_NAMESPACE"] = "stable-deployment",
            });

            await using var owner = SecretAuthorityAcquisition.OpenLocal(
                new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff));
            await builder.AddPreparedBffConfigurationAsync(owner, new AuthenticationCredentialAcquisitionReceipts());
            IConfiguration configuration = builder.Build();

            await Assert.That(configuration["Bff:SessionAuthority:Password"] == (injected ? selected : null))
                .IsTrue().Because("Only the selected injected credential may configure session authority.");
            await Assert.That(configuration["Bff:SessionAuthority:Endpoint"])
                .IsEqualTo("rediss://session.example.test:6380");
            await Assert.That(configuration["Bff:SessionAuthority:Namespace"])
                .IsEqualTo("stable-deployment");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BFF_SESSION_REDIS_PASSWORD", previous);
        }
    }
}
