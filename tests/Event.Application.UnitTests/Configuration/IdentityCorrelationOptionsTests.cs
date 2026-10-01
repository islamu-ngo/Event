using ISLAMU.Wire.Contracts.Identity;
using Explore.Application;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Application.UnitTests.Configuration;

public sealed class IdentityCorrelationOptionsTests
{
    [Test]
    public async Task DefaultDeploymentHasNoCorrelationAuthorities()
    {
        using ServiceProvider services = new ServiceCollection()
            .ConfigureApplicationServices(new ConfigurationBuilder().Build()).BuildServiceProvider();
        await Assert.That(services.GetRequiredService<IOptions<IdentityCorrelationOptions>>().Value.TrustedIssuers)
            .IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("not-an-issuer")]
    [Arguments("https://identity.example.test/realm?tenant=other")]
    [Arguments("https://identity.example.test/realm#other")]
    [Arguments("https://user@identity.example.test/realm")]
    [Arguments("https://identity.example.test/*")]
    public async Task CompositionRejectsMalformedOrWildcardAuthority(string issuer)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["IdentityCorrelation:TrustedIssuers:0"] = issuer }).Build();
        using ServiceProvider services = new ServiceCollection()
            .ConfigureApplicationServices(configuration).BuildServiceProvider();

        await Assert.That(() => services.GetRequiredService<IOptions<IdentityCorrelationOptions>>().Value)
            .Throws<OptionsValidationException>();
    }

    [Test]
    public async Task DuplicateNormalizedAuthoritiesFailRatherThanWidenTrust()
    {
        var options = new IdentityCorrelationOptions
        {
            TrustedIssuers = ["https://IDENTITY.example.test:443/realm/", "https://identity.example.test/realm"]
        };
        await Assert.That(IdentityCorrelationOptions.IsValid(options)).IsFalse();
    }

    [Test]
    public async Task PolicyAndAccountKeysUseOneIssuerNormalization()
    {
        const string issuer = "https://IDENTITY.example.test:443/realm/";
        const string normalized = "https://identity.example.test/realm";
        await Assert.That(OidcIssuerAuthority.Normalize(issuer)).IsEqualTo(normalized);
        await Assert.That(PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(issuer, "CaseSensitiveSubject"))
            .IsEqualTo(PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(normalized, "CaseSensitiveSubject"));
        await Assert.That(IdentityCorrelationOptions.IsValid(new IdentityCorrelationOptions
        {
            TrustedIssuers = [normalized, "https://identity.example.test/Realm"]
        })).IsTrue();
    }
}
