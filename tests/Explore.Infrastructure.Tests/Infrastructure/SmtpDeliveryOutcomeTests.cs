
using System.Net;
using SmtpSettingsDatabase = Explore.Tests.Shared.Settings.SmtpSettingsDatabase;
using System.Net.Sockets;
using Explore.Application.Models;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Tests.Fixtures;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class SmtpDeliveryOutcomeTests
{
    public enum SmtpPeerBehavior
    {
        Accepted, LostAcceptance, FailedQuit, TemporaryRejection, PermanentRejection, RefusedGreeting,
        LostGreeting, AuthenticationRejected, MissingStartTls, CancelledQuit, CancelledAcceptance, UnexpectedReply
    }

    [Test]
    [Arguments(SmtpPeerBehavior.LostAcceptance, SmtpDeliveryOutcome.Uncertain)]
    [Arguments(SmtpPeerBehavior.FailedQuit, SmtpDeliveryOutcome.Accepted)]
    [Arguments(SmtpPeerBehavior.TemporaryRejection, SmtpDeliveryOutcome.TransientFailure)]
    [Arguments(SmtpPeerBehavior.PermanentRejection, SmtpDeliveryOutcome.ConfigurationFailure)]
    [Arguments(SmtpPeerBehavior.RefusedGreeting, SmtpDeliveryOutcome.TransientFailure)]
    [Arguments(SmtpPeerBehavior.LostGreeting, SmtpDeliveryOutcome.TransientFailure)]
    [Arguments(SmtpPeerBehavior.AuthenticationRejected, SmtpDeliveryOutcome.ConfigurationFailure)]
    [Arguments(SmtpPeerBehavior.MissingStartTls, SmtpDeliveryOutcome.ConfigurationFailure)]
    [Arguments(SmtpPeerBehavior.CancelledQuit, SmtpDeliveryOutcome.Accepted)]
    [Arguments(SmtpPeerBehavior.CancelledAcceptance, SmtpDeliveryOutcome.Uncertain)]
    [Arguments(SmtpPeerBehavior.UnexpectedReply, SmtpDeliveryOutcome.Uncertain)]
    public async Task SendAsync_UsesProtocolEvidenceWithoutResending(SmtpPeerBehavior behavior, SmtpDeliveryOutcome expected)
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await using var peer = new SmtpPeer(behavior);
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpHost, "127.0.0.1");
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpPort, peer.Port);
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpSecurity, "None");
        if (behavior == SmtpPeerBehavior.MissingStartTls)
            await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpSecurity, "StartTls");
        if (behavior == SmtpPeerBehavior.AuthenticationRejected)
        {
            await fixture.AddCredentialsAsync(SecretScope.Instance);
            fixture.ResolveCredentials(SecretScope.Instance, null);
        }
        var logger = new TestListLogger<SmtpEmailService>();
        var service = new SmtpEmailService(fixture.Smtp, logger);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        peer.CancelSend = timeout.Cancel;

        var result = await service.SendAsync(new EmailMessage
        {
            To = "recipient@example.test", Subject = "Controlled SMTP outcome", PlainTextBody = "Body"
        }, timeout.Token);

        await Assert.That(result.Outcome).IsEqualTo(expected);
        await Assert.That(result.Success).IsEqualTo(expected == SmtpDeliveryOutcome.Accepted);
        await Assert.That(peer.Connections).IsEqualTo(1);
        await Assert.That(peer.Messages).IsEqualTo(behavior is SmtpPeerBehavior.RefusedGreeting or SmtpPeerBehavior.LostGreeting
            or SmtpPeerBehavior.AuthenticationRejected or SmtpPeerBehavior.MissingStartTls ? 0 : 1);
        await Assert.That(string.Join('|', logger.Entries.Select(e => e.Message))).DoesNotContain(SmtpPeer.ProviderCanary);
        await Assert.That(logger.Entries.All(e => e.Exception is null)).IsTrue();
        await Assert.That(result.ErrorMessage ?? string.Empty).DoesNotContain(SmtpPeer.ProviderCanary);
    }

    [Test]
    public async Task SendAsync_DisabledConfiguration_IsConfigurationFailure()
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        var service = new SmtpEmailService(fixture.Smtp, new TestListLogger<SmtpEmailService>());
        var result = await service.SendAsync(new EmailMessage { To = "recipient@example.test", Subject = "Disabled" });
        await Assert.That(result.Outcome).IsEqualTo(SmtpDeliveryOutcome.ConfigurationFailure);
    }

    [Test]
    public async Task Fail_WithoutTransportEvidence_DoesNotInferRetryFromText()
    {
        await Assert.That(EmailResult.Fail("connection timeout 421 451").Outcome).IsEqualTo(SmtpDeliveryOutcome.Uncertain);
        Assert.Throws<ArgumentException>(() => EmailResult.Fail("failure", outcome: SmtpDeliveryOutcome.Accepted));
    }

    [Test]
    public async Task TestConnectionAsync_ProbesInstanceEvenWhenAmbientTenantOwnsDifferentTransport()
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await using var instancePeer = new SmtpPeer(SmtpPeerBehavior.Accepted);
        await using var tenantPeer = new SmtpPeer(SmtpPeerBehavior.RefusedGreeting);
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpHost, "127.0.0.1");
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpPort, instancePeer.Port);
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpSecurity, "None");
        await fixture.ConfigureTenantAsync();
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.SmtpHost, "127.0.0.1");
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.SmtpPort, tenantPeer.Port);
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.SmtpSecurity, "None");
        var service = new SmtpEmailService(fixture.Smtp, new TestListLogger<SmtpEmailService>());

        var result = await service.TestConnectionAsync();

        await Assert.That(result.Success).IsTrue();
        await Assert.That(instancePeer.Connections).IsEqualTo(1);
        await Assert.That(tenantPeer.Connections).IsEqualTo(0);
    }

    private sealed class SmtpPeer : IAsyncDisposable
    {
        public const string ProviderCanary = "provider-response-recipient@example.test";
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private int _connections;
        private int _messages;

        public SmtpPeer(SmtpPeerBehavior behavior)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _run = RunAsync(behavior);
        }

        public int Port { get; }
        public int Connections => Volatile.Read(ref _connections);
        public int Messages => Volatile.Read(ref _messages);
        public Action? CancelSend { get; set; }

        private async Task RunAsync(SmtpPeerBehavior behavior)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _connections);
                    using var reader = new StreamReader(connection.GetStream());
                    await using var writer = new StreamWriter(connection.GetStream()) { NewLine = "\r\n", AutoFlush = true };
                    if (behavior == SmtpPeerBehavior.LostGreeting) continue;
                    if (behavior == SmtpPeerBehavior.RefusedGreeting)
                    {
                        await writer.WriteLineAsync($"421 {ProviderCanary}");
                        continue;
                    }
                    await writer.WriteLineAsync("220 test SMTP");
                    var data = false;
                    while (await reader.ReadLineAsync(_stop.Token) is { } line)
                    {
                        if (data)
                        {
                            if (line != ".") continue;
                            Interlocked.Increment(ref _messages);
                            data = false;
                            if (behavior == SmtpPeerBehavior.LostAcceptance) break;
                            if (behavior == SmtpPeerBehavior.CancelledAcceptance)
                            {
                                CancelSend!();
                                break;
                            }
                            await writer.WriteLineAsync(behavior switch
                            {
                                SmtpPeerBehavior.TemporaryRejection => $"451 {ProviderCanary}",
                                SmtpPeerBehavior.PermanentRejection => $"550 {ProviderCanary}",
                                SmtpPeerBehavior.UnexpectedReply => $"354 {ProviderCanary}",
                                _ => "250 accepted"
                            });
                        }
                        else if (line == "DATA")
                        {
                            await writer.WriteLineAsync("354 send body");
                            data = true;
                        }
                        else if (line == "QUIT")
                        {
                            if (behavior == SmtpPeerBehavior.CancelledQuit)
                            {
                                CancelSend!();
                                break;
                            }
                            if (behavior != SmtpPeerBehavior.FailedQuit) await writer.WriteLineAsync("221 goodbye");
                            break;
                        }
                        else
                        {
                            await writer.WriteLineAsync(behavior == SmtpPeerBehavior.AuthenticationRejected
                                ? line.StartsWith("EHLO", StringComparison.Ordinal)
                                    ? "250-test SMTP\r\n250 AUTH PLAIN"
                                    : $"535 {ProviderCanary}"
                                : "250 ok");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _run;
            _stop.Dispose();
        }
    }
}
