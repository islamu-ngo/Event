using System.Net;
using System.Net.Sockets;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Domain.Constants;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeEmailTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RealSmtpRechecksOriginalAuthorityAfterConnectionBeforeAnyMessage(bool expireAtGreeting)
    {
        await using var fixture = await EmailFixture.CreateAsync();
        await using var peer = new OriginalNoticeSmtpPeer(() =>
        {
            if (expireAtGreeting) fixture.Notice.Clock.UtcNow = fixture.Notice.Approval.RetainUntil;
            return Task.CompletedTask;
        });
        await using var context = fixture.Notice.Database.Context();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var settings = SmtpSettingsReader(context, cache);
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpHost, "\"127.0.0.1\"");
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpPort,
            peer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpSecurity, "\"None\"");
        var request = await fixture.AdmitAsync();
        // Only the external secret authority is substituted. Settings, eligibility, original
        // source qualification, recipient resolution, SMTP and graph settlement are native.
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
        var capability = new EmailDeliveryCapabilityResolver(settings, secrets, new SecretBindingRepository(context));
        var resolver = new SmtpConfigResolver(capability, fixture.Notice.Database, settings);
        var smtp = new SmtpEmailService(resolver, NullLogger<SmtpEmailService>.Instance,
            fixture.Notice.Clock, fixture.CreateAuthority(context));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await smtp.SendAsync(new EmailMessage
        {
            ModerationNotice = request,
            To = "stale-copy@example.invalid",
            Subject = "UNTRUSTED_QUEUE_COPY",
            PlainTextBody = fixture.Notice.PrivateJustification
        }, timeout.Token);
        await peer.Completed.WaitAsync(timeout.Token);
        await Assert.That(result.Outcome).IsEqualTo(expireAtGreeting
            ? SmtpDeliveryOutcome.AuthorityRejected : SmtpDeliveryOutcome.Accepted);
        await Assert.That(peer.Messages).IsEqualTo(expireAtGreeting ? 0 : 1);
        if (!expireAtGreeting)
        {
            using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(peer.Message));
            using var message = await MimeMessage.LoadAsync(bytes, timeout.Token);
            await Assert.That(message.To.Mailboxes.Single().Address).IsEqualTo("original-notice@example.invalid");
            await Assert.That(message.Subject).IsNotEqualTo("UNTRUSTED_QUEUE_COPY");
            await Assert.That(message.TextBody).DoesNotContain(fixture.Notice.PrivateJustification);
            await Assert.That(message.TextBody).DoesNotContain(fixture.Notice.Approval.RuleMeaning);
            await Assert.That(message.TextBody).Contains(fixture.Notice.SubjectNoticeId.ToString("D"));
        }
    }

    [Test]
    [Arguments("recipient")]
    [Arguments("email")]
    [Arguments("preference")]
    [Arguments("approval")]
    [Arguments("source")]
    [Arguments("address")]
    public async Task RealSmtpGreetingChangesRequalifyRecipientBeforeAnyEnvelope(string change)
    {
        await using var fixture = await EmailFixture.CreateAsync();
        await using var peer = new OriginalNoticeSmtpPeer(async () =>
        {
            if (change == "address")
                await fixture.ChangeAddressAsync();
            else
                await fixture.InvalidateAsync(change);
        });
        await using var context = fixture.Notice.Database.Context();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var settings = SmtpSettingsReader(context, cache);
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpHost, "\"127.0.0.1\"");
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpPort,
            peer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpSecurity, "\"None\"");
        var request = await fixture.AdmitAsync();
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
        var capability = new EmailDeliveryCapabilityResolver(settings, secrets, new SecretBindingRepository(context));
        var resolver = new SmtpConfigResolver(capability, fixture.Notice.Database, settings);
        var smtp = new SmtpEmailService(resolver, NullLogger<SmtpEmailService>.Instance,
            fixture.Notice.Clock, fixture.CreateAuthority(context));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await smtp.SendAsync(new EmailMessage
        {
            ModerationNotice = request,
            To = "stale-copy@example.invalid",
            Subject = "UNTRUSTED_QUEUE_COPY",
            PlainTextBody = fixture.Notice.PrivateJustification
        }, timeout.Token);
        await peer.Completed.WaitAsync(timeout.Token);
        var delivered = change == "address";
        await Assert.That(result.Outcome).IsEqualTo(delivered
            ? SmtpDeliveryOutcome.Accepted : SmtpDeliveryOutcome.AuthorityRejected);
        await Assert.That(peer.EnvelopeCommands).IsEqualTo(delivered ? 2 : 0);
        await Assert.That(peer.Messages).IsEqualTo(delivered ? 1 : 0);
        if (delivered)
        {
            using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(peer.Message));
            using var message = await MimeMessage.LoadAsync(bytes, timeout.Token);
            await Assert.That(message.To.Mailboxes.Single().Address).IsEqualTo(fixture.CurrentAddress);
            await Assert.That(message.To.Mailboxes.Single().Address).IsNotEqualTo("stale-copy@example.invalid");
            await Assert.That(message.TextBody).DoesNotContain(fixture.Notice.PrivateJustification);
            await Assert.That(message.TextBody).DoesNotContain(fixture.Notice.Approval.RuleMeaning);
            await Assert.That(message.TextBody).Contains(fixture.Notice.SubjectNoticeId.ToString("D"));
        }
        context.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        await Assert.That(await context.Set<Explore.Domain.ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    private static HierarchicalSettingsReader SmtpSettingsReader(ExploreDbContext context, IMemoryCache cache)
    {
        var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        return new(SystemSettingRepository.CreateReadOnly(context, mutationLock),
            TenantSettingRepository.CreateReadOnly(context, mutationLock),
            new OrganizationSettingRepository(context), new GroupSettingRepository(context),
            UserPreferenceRepository.CreateReadOnly(context), cache,
            new AtprotoFederationSettingRepository(context, null));
    }

    private sealed class OriginalNoticeSmtpPeer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        public Task Completed { get; }
        public int Port { get; }
        public int Messages { get; private set; }
        public int EnvelopeCommands { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public OriginalNoticeSmtpPeer(Func<Task> beforeReady)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Completed = RunAsync(beforeReady);
        }

        private async Task RunAsync(Func<Task> beforeReady)
        {
            using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
            using var reader = new StreamReader(connection.GetStream());
            await using var writer = new StreamWriter(connection.GetStream()) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 original notice witness");
            var receiving = false;
            var content = new StringBuilder();
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (receiving)
                {
                    if (line == ".")
                    {
                        Messages++;
                        Message = content.ToString();
                        receiving = false;
                        await writer.WriteLineAsync("250 accepted");
                    }
                    else content.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
                }
                else if (line.StartsWith("EHLO ", StringComparison.Ordinal) || line.StartsWith("HELO ", StringComparison.Ordinal))
                {
                    await beforeReady();
                    await writer.WriteLineAsync("250 ready");
                }
                else if (line == "DATA")
                {
                    receiving = true;
                    await writer.WriteLineAsync("354 message");
                }
                else if (line == "QUIT")
                {
                    await writer.WriteLineAsync("221 goodbye");
                    break;
                }
                else
                {
                    if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                        EnvelopeCommands++;
                    await writer.WriteLineAsync("250 ok");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await Completed; }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally { _stop.Dispose(); }
        }
    }
}
