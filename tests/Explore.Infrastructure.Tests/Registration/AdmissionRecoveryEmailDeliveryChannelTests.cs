using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionRecoveryEmailDeliveryChannelTests
{
    [Test]
    public async Task DeliveryUsesConfiguredSameOriginLinkAndIntentIdempotency()
    {
        EmailMessage? observed = null;
        IEmailService email = Substitute.For<IEmailService>();
        email.SendAsync(
                Arg.Do<EmailMessage>(message => observed = message),
                Arg.Any<CancellationToken>())
            .Returns(EmailResult.Ok("accepted"));
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "https://events.example.test"
            })
            .Build();
        var channel = new AdmissionRecoveryEmailDeliveryChannel(email, configuration);
        Guid intentId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000471");
        Guid requestId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000472");
        const string capability = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        AdmissionRecoveryDirectDeliveryResult result = await channel.DeliverAsync(
            new AdmissionRecoveryDirectDeliveryRequest(
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000473"),
                intentId,
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000474"),
                requestId,
                "holder@example.test",
                capability),
            CancellationToken.None);

        string link = observed!.PlainTextBody.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last();
        var uri = new Uri(link);
        await Assert.That(result.Outcome).IsEqualTo(AdmissionRecoveryDirectDeliveryOutcome.Accepted);
        await Assert.That(uri.GetLeftPart(UriPartial.Authority))
            .IsEqualTo("https://events.example.test");
        await Assert.That(uri.AbsolutePath).IsEqualTo("/tickets/recovery");
        await Assert.That(uri.Query).IsEmpty();
        await Assert.That(uri.Fragment).IsEqualTo($"#capability={capability}");
        await Assert.That(link[..link.IndexOf('#')]).DoesNotContain(capability);
        await Assert.That(observed.CustomHeaders["X-Admission-Recovery-Idempotency-Key"])
            .IsEqualTo(intentId.ToString("N"));
    }

    [Test]
    public async Task DeliveryRejectsOriginWithoutTransportConfidentiality()
    {
        IEmailService email = Substitute.For<IEmailService>();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "http://events.example.test"
            })
            .Build();
        var channel = new AdmissionRecoveryEmailDeliveryChannel(email, configuration);

        AdmissionRecoveryDirectDeliveryResult result = await channel.DeliverAsync(
            new AdmissionRecoveryDirectDeliveryRequest(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "holder@example.test",
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            CancellationToken.None);

        await Assert.That(result.Outcome)
            .IsEqualTo(AdmissionRecoveryDirectDeliveryOutcome.Ambiguous);
        await email.DidNotReceiveWithAnyArgs()
            .SendAsync(default!, default);
    }
}
