using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class SmtpEmailServiceConfigurationTests
{
    [Test]
    public async Task SendAsync_WhenSmtpConfigMissing_FailsWithoutProviderHandoff()
    {
        var service = CreateService(config: null);

        var result = await service.SendAsync(new EmailMessage
        {
            To = "attendee@example.test",
            Subject = "Configuration missing",
            PlainTextBody = "Body"
        });

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("SMTP is not configured");
    }

    [Test]
    public async Task TestConnectionAsync_WhenSmtpConfigMissing_ReturnsFailure()
    {
        var service = CreateService(config: null);

        var result = await service.TestConnectionAsync();

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("SMTP is not configured");
    }

    [Test]
    public async Task TestConnectionAsync_WhenTransportFails_UsesFixedNonPiiResultAndLogMetadata()
    {
        const string usernameCanary = "smtp-user-canary@example.test";
        const string passwordCanary = "smtp-password-canary";
        const string endpointCanary = "127.0.0.1:1";
        var logger = new TestListLogger<SmtpEmailService>();
        var service = CreateService(new SmtpConfiguration
        {
            Host = "127.0.0.1",
            Port = 1,
            Security = SmtpSecurityMode.None,
            Username = usernameCanary,
            Password = passwordCanary,
            FromAddress = "sender-canary@example.test",
            FromName = "sender-name-canary",
            TimeoutSeconds = 1
        }, logger);

        var result = await service.TestConnectionAsync();

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsEqualTo("SMTP connection test failed.");
        await Assert.That(logger.Entries).Count().IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Error);
        await Assert.That(logger.Entries[0].EventId.Id).IsEqualTo(4703);
        await Assert.That(logger.Entries[0].Exception).IsNull();
        await Assert.That(logger.Entries[0].Message).StartsWith(
            "SMTP connection test completed with status connection_failed in ");
        var observableText = string.Join('|', result.ErrorMessage, logger.Entries[0].Message);
        await Assert.That(observableText).DoesNotContain(usernameCanary);
        await Assert.That(observableText).DoesNotContain(passwordCanary);
        await Assert.That(observableText).DoesNotContain(endpointCanary);
        await Assert.That(observableText).DoesNotContain("127.0.0.1");
        await Assert.That(observableText).DoesNotContain("sender-canary@example.test");
        await Assert.That(observableText).DoesNotContain("sender-name-canary");
    }

    private static SmtpEmailService CreateService(
        SmtpConfiguration? config,
        ILogger<SmtpEmailService>? logger = null)
    {
        var resolver = Substitute.For<ISmtpConfigResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(config);
        resolver.ResolveAsync(null, Arg.Any<CancellationToken>()).Returns(config);
        return new SmtpEmailService(resolver, logger ?? NullLogger<SmtpEmailService>.Instance);
    }
}
