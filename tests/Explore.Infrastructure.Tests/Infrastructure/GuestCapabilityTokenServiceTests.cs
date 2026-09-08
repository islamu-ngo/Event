using System.Reflection;
using Explore.Application.Contracts.Services;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class GuestCapabilityTokenServiceTests
{
    private readonly GuestCapabilityTokenService _service = new();

    [Test]
    public async Task Issue_CreatesUniqueUrlSafeTokenAndSeparateHash()
    {
        GuestCapabilityTokenIssue first = _service.Issue();
        GuestCapabilityTokenIssue second = _service.Issue();

        byte[] tokenBytes = Convert.FromBase64String(ToPaddedBase64(first.RawToken));

        await Assert.That(tokenBytes.Length).IsEqualTo(32);
        await Assert.That(first.RawToken.Contains('+')).IsFalse();
        await Assert.That(first.RawToken.Contains('/')).IsFalse();
        await Assert.That(first.RawToken.Contains('=')).IsFalse();
        await Assert.That(first.RawToken).IsNotEqualTo(second.RawToken);
        await Assert.That(first.Hash.Value).IsNotEqualTo(first.RawToken);
    }

    [Test]
    public async Task Matches_UsesIssuedTokenAndFailsClosedForTamperedOrMissingTokens()
    {
        GuestCapabilityTokenIssue issue = _service.Issue();
        string tamperedToken = issue.RawToken[..^1] + (issue.RawToken[^1] == 'A' ? "B" : "A");

        await Assert.That(_service.Matches(issue.RawToken, issue.Hash)).IsTrue();
        await Assert.That(_service.Matches(tamperedToken, issue.Hash)).IsFalse();
        await Assert.That(_service.Matches(null, issue.Hash)).IsFalse();
        await Assert.That(_service.Matches(string.Empty, issue.Hash)).IsFalse();
        await Assert.That(_service.Matches("not-a-token", issue.Hash)).IsFalse();
    }

    [Test]
    public async Task PublicContract_ExposesOnlyIssuanceAndMatching()
    {
        MethodInfo[] methods = typeof(IGuestCapabilityTokenService).GetMethods();
        string[] issueProperties = typeof(GuestCapabilityTokenIssue)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order()
            .ToArray();

        await Assert.That(methods).Count().IsEqualTo(2);
        await Assert.That(methods.Single(method => method.Name == "Issue").ReturnType)
            .IsEqualTo(typeof(GuestCapabilityTokenIssue));
        await Assert.That(methods.Single(method => method.Name == "Matches").ReturnType)
            .IsEqualTo(typeof(bool));
        await Assert.That(issueProperties).IsEquivalentTo(["Hash", "RawToken"]);
    }

    [Test]
    public async Task Issue_ToStringDoesNotExposeCapabilityMaterial()
    {
        GuestCapabilityTokenIssue issue = _service.Issue();
        string representation = issue.ToString();

        await Assert.That(representation.Contains(issue.RawToken)).IsFalse();
        await Assert.That(representation.Contains(issue.Hash.Value)).IsFalse();
    }

    [Test]
    public async Task ConfigureInfrastructureServices_ResolvesStatelessCapabilityTokenService()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.ConfigureInfrastructureServices(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        IGuestCapabilityTokenService? service = provider.GetService<IGuestCapabilityTokenService>();

        await Assert.That(service).IsTypeOf<GuestCapabilityTokenService>();
    }

    private static string ToPaddedBase64(string token)
    {
        string base64 = token.Replace('-', '+').Replace('_', '/');
        return base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
    }
}
