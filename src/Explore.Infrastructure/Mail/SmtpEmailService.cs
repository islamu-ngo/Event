// ABOUTME: MailKit-based email service implementation that resolves cascading per-tenant SMTP configuration.
// ABOUTME: Emits fixed non-PII transport telemetry while returning sanitized provider outcomes to callers.

using System.Diagnostics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Explore.Infrastructure.Mail;

/// <summary>
/// Sends emails via any SMTP server using MailKit.
/// Configuration is resolved per-tenant from the cascading settings engine.
/// Creates a new SmtpClient per send (MailKit is not thread-safe).
/// </summary>
public class SmtpEmailService : IEmailService, IEmailConnectionTester
{
    private static readonly EventId ConnectionTestSucceededEvent = new(4701, "SmtpConnectionTestSucceeded");
    private static readonly EventId ConnectionTestAuthenticationFailedEvent = new(4702, "SmtpConnectionTestAuthenticationFailed");
    private static readonly EventId ConnectionTestFailedEvent = new(4703, "SmtpConnectionTestFailed");
    private static readonly EventId SendAcceptedEvent = new(4710, "SmtpSendAccepted");
    private static readonly EventId SendCommandFailedEvent = new(4711, "SmtpSendCommandFailed");
    private static readonly EventId SendProtocolFailedEvent = new(4712, "SmtpSendProtocolFailed");
    private static readonly EventId SendAuthenticationFailedEvent = new(4713, "SmtpSendAuthenticationFailed");
    private static readonly EventId SendTransportFailedEvent = new(4714, "SmtpSendTransportFailed");

    private readonly ISmtpConfigResolver _configResolver;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        ISmtpConfigResolver configResolver,
        ILogger<SmtpEmailService> logger)
    {
        _configResolver = configResolver;
        _logger = logger;
    }

    public async Task<EmailResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        var config = await _configResolver.ResolveAsync(cancellationToken);
        if (config is null)
        {
            return EmailResult.Fail("SMTP is not configured. Configure email settings in the admin panel.",
                outcome: SmtpDeliveryOutcome.ConfigurationFailure);
        }

        return await SendCoreAsync(message, config, cancellationToken);
    }

    public async Task<EmailResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configResolver.ResolveAsync(null, cancellationToken);
        if (config is null)
        {
            return EmailResult.Fail("SMTP is not configured. Set email.smtp_host in admin settings.");
        }

        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new SmtpClient();
            ConfigureClient(client, config);

            await client.ConnectAsync(
                config.Host,
                config.Port,
                MapSecurityMode(config.Security),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(config.Username))
            {
                await client.AuthenticateAsync(config.Username, config.Password, cancellationToken);
            }

            await client.DisconnectAsync(quit: true, cancellationToken);

            sw.Stop();
            _logger.LogInformation(
                ConnectionTestSucceededEvent,
                "SMTP connection test completed with status {Status} in {DurationMs}ms",
                "succeeded",
                sw.ElapsedMilliseconds);

            return EmailResult.Ok("Connection successful", sw.Elapsed);
        }
        catch (AuthenticationException)
        {
            sw.Stop();
            _logger.LogError(
                ConnectionTestAuthenticationFailedEvent,
                "SMTP connection test completed with status {Status} in {DurationMs}ms",
                "authentication_failed",
                sw.ElapsedMilliseconds);
            return EmailResult.Fail("SMTP authentication failed.", sw.Elapsed);
        }
        catch (Exception)
        {
            sw.Stop();
            _logger.LogError(
                ConnectionTestFailedEvent,
                "SMTP connection test completed with status {Status} in {DurationMs}ms",
                "connection_failed",
                sw.ElapsedMilliseconds);
            return EmailResult.Fail("SMTP connection test failed.", sw.Elapsed);
        }
    }

    private async Task<EmailResult> SendCoreAsync(
        EmailMessage message,
        SmtpConfiguration config,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var sendInitiated = false;
        var accepted = false;
        var authenticating = false;

        try
        {
            using var mimeMessage = BuildMimeMessage(message, config);

            using var client = new SmtpClient();
            ConfigureClient(client, config);

            await client.ConnectAsync(
                config.Host,
                config.Port,
                MapSecurityMode(config.Security),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(config.Username))
            {
                authenticating = true;
                await client.AuthenticateAsync(config.Username, config.Password, cancellationToken);
                authenticating = false;
            }

            sendInitiated = true;
            await client.SendAsync(mimeMessage, cancellationToken);
            accepted = true;
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception) when (accepted)
        {
            // Acceptance is final even if QUIT, cancellation, or client disposal fails.
        }
        catch (SmtpCommandException ex)
        {
            sw.Stop();
            var statusCode = (int)ex.StatusCode;
            _logger.LogError(
                SendCommandFailedEvent,
                "SMTP send completed with status {Status}, SMTP status code {StatusCode}, in {DurationMs}ms",
                "command_failed",
                statusCode,
                sw.ElapsedMilliseconds);
            var outcome = authenticating ? SmtpDeliveryOutcome.ConfigurationFailure : statusCode switch
            {
                >= 400 and < 500 => SmtpDeliveryOutcome.TransientFailure,
                >= 500 and < 600 => SmtpDeliveryOutcome.ConfigurationFailure,
                _ => SmtpDeliveryOutcome.Uncertain
            };
            return EmailResult.Fail($"SMTP command failed ({statusCode}).", sw.Elapsed, outcome);
        }
        catch (SmtpProtocolException)
        {
            sw.Stop();
            _logger.LogError(
                SendProtocolFailedEvent,
                "SMTP send completed with status {Status} in {DurationMs}ms",
                "protocol_failed",
                sw.ElapsedMilliseconds);
            return EmailResult.Fail("SMTP connection protocol error.", sw.Elapsed,
                sendInitiated ? SmtpDeliveryOutcome.Uncertain : SmtpDeliveryOutcome.TransientFailure);
        }
        catch (Exception ex) when (ex is AuthenticationException
            or SslHandshakeException
            or System.Security.Authentication.AuthenticationException
            or MailKit.ServiceNotAuthenticatedException
            or ArgumentException
            or FormatException
            or NotSupportedException
            or InvalidOperationException)
        {
            sw.Stop();
            _logger.LogError(
                sendInitiated ? SendTransportFailedEvent : SendAuthenticationFailedEvent,
                "SMTP send completed with status {Status} in {DurationMs}ms",
                sendInitiated ? "uncertain" : "configuration_failed",
                sw.ElapsedMilliseconds);
            return EmailResult.Fail(
                sendInitiated ? "SMTP acceptance is uncertain." : "SMTP authentication or configuration failed.",
                sw.Elapsed,
                sendInitiated ? SmtpDeliveryOutcome.Uncertain : SmtpDeliveryOutcome.ConfigurationFailure);
        }
        catch (Exception ex) when (ex is TimeoutException
            or OperationCanceledException
            or System.IO.IOException
            or System.Net.Sockets.SocketException)
        {
            sw.Stop();
            var (status, safeFailureMessage) = ex switch
            {
                TimeoutException => ("timeout", "SMTP timeout."),
                OperationCanceledException => ("cancelled", "SMTP operation cancelled."),
                _ => ("connection_failed", "SMTP connection error.")
            };
            _logger.LogError(
                SendTransportFailedEvent,
                "SMTP send completed with status {Status} in {DurationMs}ms",
                status,
                sw.ElapsedMilliseconds);
            return EmailResult.Fail(safeFailureMessage, sw.Elapsed,
                sendInitiated ? SmtpDeliveryOutcome.Uncertain : SmtpDeliveryOutcome.TransientFailure);
        }

        sw.Stop();
        _logger.LogInformation(
            SendAcceptedEvent,
            "SMTP send completed with status {Status} in {DurationMs}ms",
            "accepted",
            sw.ElapsedMilliseconds);
        return EmailResult.Ok("SMTP accepted message.", sw.Elapsed);
    }

    private static void ConfigureClient(SmtpClient client, SmtpConfiguration config)
    {
        if (config.SkipCertificateValidation)
        {
            client.ServerCertificateValidationCallback = (_, _, _, sslPolicyErrors) =>
                sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
        }

        client.Timeout = config.TimeoutSeconds * 1000;
    }

    private static SecureSocketOptions MapSecurityMode(SmtpSecurityMode mode) => mode switch
    {
        SmtpSecurityMode.None => SecureSocketOptions.None,
        SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurityMode.Auto => SecureSocketOptions.Auto,
        _ => SecureSocketOptions.StartTls
    };

    private static MimeMessage BuildMimeMessage(EmailMessage message, SmtpConfiguration config)
    {
        var mimeMessage = new MimeMessage();

        mimeMessage.From.Add(new MailboxAddress(
            message.FromName ?? config.FromName,
            message.FromAddress ?? config.FromAddress));

        mimeMessage.To.Add(MailboxAddress.Parse(message.To));

        foreach (var cc in message.Cc)
            mimeMessage.Cc.Add(MailboxAddress.Parse(cc));

        foreach (var bcc in message.Bcc)
            mimeMessage.Bcc.Add(MailboxAddress.Parse(bcc));

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            mimeMessage.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));

        mimeMessage.Subject = message.Subject;

        foreach (var (key, value) in message.CustomHeaders)
            mimeMessage.Headers.Add(key, value);

        var builder = new BodyBuilder();

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
            builder.HtmlBody = message.HtmlBody;

        if (!string.IsNullOrWhiteSpace(message.PlainTextBody))
            builder.TextBody = message.PlainTextBody;

        foreach (var attachment in message.Attachments)
        {
            if (attachment.IsInline && !string.IsNullOrWhiteSpace(attachment.ContentId))
            {
                var inline = builder.LinkedResources.Add(
                    attachment.FileName,
                    attachment.Content,
                    ContentType.Parse(attachment.ContentType));
                inline.ContentId = attachment.ContentId;
            }
            else
            {
                builder.Attachments.Add(
                    attachment.FileName,
                    attachment.Content,
                    ContentType.Parse(attachment.ContentType));
            }
        }

        mimeMessage.Body = builder.ToMessageBody();
        return mimeMessage;
    }
}
