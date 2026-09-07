// ABOUTME: Mailpit-backed integration tests for SmtpEmailService.
// ABOUTME: Proves MailKit SMTP sends and connection checks work against local test infrastructure.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<MailpitContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("MailpitSmtp")]
public sealed class SmtpEmailServiceMailpitTests(MailpitContainerFixture mailpit)
{
    [Test]
    [Timeout(180_000)]
    public async Task SendAsync_WithMailpitSmtpConfig_DeliversMessageToMailpit()
    {
        await mailpit.ClearMessagesAsync();
        var subject = $"Registration confirmation {Guid.CreateVersion7():N}";
        var body = $"Your registration has been received. sentinel-{Guid.CreateVersion7():N}";
        var smtpPassword = $"smtp-secret-{Guid.CreateVersion7():N}";
        var logger = new TestListLogger<SmtpEmailService>();
        var service = CreateService(CreateConfig(password: smtpPassword), logger);

        var result = await service.SendAsync(new EmailMessage
        {
            To = "attendee@example.test",
            Subject = subject,
            PlainTextBody = body,
            HtmlBody = $"<p>{body}</p>"
        });

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("SMTP accepted message.");
        await Assert.That(result.Message ?? string.Empty).DoesNotContain(body);
        await Assert.That(result.Message ?? string.Empty).DoesNotContain(smtpPassword);
        await Assert.That(result.ErrorMessage ?? string.Empty).DoesNotContain(body);
        await Assert.That(result.ErrorMessage ?? string.Empty).DoesNotContain(smtpPassword);
        await Assert.That(logger.Entries).Count().IsEqualTo(1);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(logger.Entries[0].EventId.Id).IsEqualTo(4710);
        await Assert.That(logger.Entries[0].Exception).IsNull();
        await Assert.That(logger.Entries[0].Message).StartsWith(
            "SMTP send completed with status accepted in ");
        var observableText = string.Join('|', result.Message, logger.Entries[0].Message);
        await Assert.That(observableText).DoesNotContain("attendee@example.test");
        await Assert.That(observableText).DoesNotContain(subject);
        await Assert.That(observableText).DoesNotContain(body);
        await Assert.That(observableText).DoesNotContain(smtpPassword);
        await Assert.That(observableText).DoesNotContain(mailpit.SmtpHost);
        await Assert.That(observableText).DoesNotContain("noreply@example.test");
        await Assert.That(observableText).DoesNotContain("ISLAMU Event Tests");

        var summary = await mailpit.WaitForMessageAsync(
            message => message.Subject == subject
                && message.To.Any(address => address.Address == "attendee@example.test"),
            TimeSpan.FromSeconds(10));
        var detail = await mailpit.GetMessageAsync(summary.Id);

        await Assert.That(summary.From.Address).IsEqualTo("noreply@example.test");
        await Assert.That(detail.Text).Contains(body);
        await Assert.That(detail.Html).Contains(body);
    }

    [Test]
    [Timeout(180_000)]
    public async Task TestConnectionAsync_WithMailpitSmtpConfig_ReturnsSuccess()
    {
        var service = CreateService(CreateConfig());

        var result = await service.TestConnectionAsync();

        await Assert.That(result.Success).IsTrue();
    }

    private SmtpConfiguration CreateConfig(string? password = null) => new()
    {
        Host = mailpit.SmtpHost,
        Port = mailpit.SmtpPort,
        Security = SmtpSecurityMode.None,
        FromAddress = "noreply@example.test",
        FromName = "ISLAMU Event Tests",
        Password = password,
        TimeoutSeconds = 10
    };

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
