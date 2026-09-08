// ABOUTME: Hands transient Local Identity link material to the existing MailKit SMTP implementation.
// ABOUTME: Uses an admitted instance transport snapshot and stable delivery identifiers without tenant resolution.

using Explore.Application.Contracts.Identity;
using Explore.Application.Models;
using Explore.Infrastructure.Mail;
using Microsoft.AspNetCore.WebUtilities;

namespace Explore.Infrastructure.Authentication;

public interface ILocalIdentityLifecycleSmtpTransport
{
    Task<EmailResult> SendAsync(LocalIdentityLifecycleTransport handoff, Guid attemptId,
        Uri callbackUri, SmtpConfiguration configuration, CancellationToken cancellationToken);
}

public sealed class LocalIdentityLifecycleSmtpTransport(SmtpEmailService smtp) : ILocalIdentityLifecycleSmtpTransport
{
    public Task<EmailResult> SendAsync(LocalIdentityLifecycleTransport handoff, Guid attemptId,
        Uri callbackUri, SmtpConfiguration configuration, CancellationToken cancellationToken)
    {
        var pointer = handoff.Operation;
        string encoded = QueryHelpers.AddQueryString(string.Empty, new Dictionary<string, string?>
        {
            ["operationId"] = pointer.OperationId.ToString("D"),
            ["localSubjectId"] = pointer.LocalSubjectId.ToString("D"),
            ["personalActorId"] = pointer.PersonalActorId.ToString("D"),
            ["externalLoginId"] = pointer.ExternalLoginId.ToString("D"),
            ["purpose"] = ((int)pointer.Purpose).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["generation"] = pointer.Generation.ToString("D"),
            ["token"] = handoff.Token
        });
        // Browser fragments never enter HTTP request targets, proxy logs, or referrer query strings.
        string link = callbackUri.AbsoluteUri + "#" + encoded[1..];
        var message = new EmailMessage
        {
            To = handoff.Address,
            Subject = pointer.Purpose == LocalIdentityLifecyclePurpose.PasswordRecovery
                ? "Reset your password" : "Verify your email address",
            PlainTextBody = $"Use this link to complete your account request:\n{link}\n\nIf you did not request this, ignore this message.",
            CustomHeaders = { ["Message-ID"] = $"<local-{pointer.OperationId:N}-{attemptId:N}@lifecycle.invalid>" }
        };
        return smtp.SendAdmittedAsync(message, configuration, cancellationToken);
    }
}
