using System.Net;
using Explore.Infrastructure.Http;

namespace Explore.Infrastructure.Tests.Infrastructure.Http;

public sealed partial class OutboundTransportSecurityTests
{
    [Test]
    [Arguments("169.254.169.254")]
    [Arguments("::ffff:169.254.169.254")]
    [Arguments("fd00:ec2::254")]
    [Arguments("100.100.100.200")]
    public async Task MetadataDenialSurvivesBroadPrivateOptInAndCidrs(string address)
    {
        var policy = new OutboundEndpointPolicy(false, ["0.0.0.0/0", "::/0"]);
        var exception = await Assert.ThrowsAsync<OutboundEndpointDeniedException>(() =>
        {
            policy.RequireAllowed(new Uri("https://webhook.example.test/hook"), [IPAddress.Parse(address)]);
            return Task.CompletedTask;
        });
        await Assert.That(exception?.FailureCategory).IsEqualTo("cloud_metadata_blocked");
    }

    [Test]
    [Arguments("10.1.2.3")]
    [Arguments("::ffff:10.1.2.3")]
    [Arguments("::1")]
    public async Task OneForbiddenAnswerRejectsTheEntireResolution(string address)
    {
        var policy = new OutboundEndpointPolicy();
        var exception = await Assert.ThrowsAsync<OutboundEndpointDeniedException>(() =>
        {
            policy.RequireAllowed(new Uri("https://webhook.example.test/hook"),
                [IPAddress.Parse("1.1.1.1"), IPAddress.Parse(address)]);
            return Task.CompletedTask;
        });
        await Assert.That(exception?.FailureCategory).IsEqualTo("private_network_blocked");
    }

    [Test]
    public async Task ExplicitCidrAllowsOnlyMatchingPrivateAddresses()
    {
        var policy = new OutboundEndpointPolicy(true, ["10.1.0.0/16"]);
        policy.RequireAllowed(new Uri("https://webhook.example.test/hook"),
            [IPAddress.Parse("10.1.2.3"), IPAddress.Parse("::ffff:10.1.3.4")]);
        var exception = await Assert.ThrowsAsync<OutboundEndpointDeniedException>(() =>
        {
            policy.RequireAllowed(new Uri("https://webhook.example.test/hook"), [IPAddress.Parse("10.2.2.3")]);
            return Task.CompletedTask;
        });
        await Assert.That(exception?.FailureCategory).IsEqualTo("private_network_blocked");
    }

    [Test]
    public async Task EmptyResolutionIsNotAnApprovedDestination()
    {
        var exception = await Assert.ThrowsAsync<OutboundEndpointDeniedException>(() =>
        {
            new OutboundEndpointPolicy().RequireAllowed(new Uri("https://webhook.example.test/hook"), []);
            return Task.CompletedTask;
        });
        await Assert.That(exception?.FailureCategory).IsEqualTo("dns_resolution_empty");
    }
}
