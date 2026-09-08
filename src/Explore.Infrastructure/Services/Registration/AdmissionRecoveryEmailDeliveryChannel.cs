using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionRecoveryEmailDeliveryChannel(
    IEmailService emailService,
    IConfiguration configuration,
    TimeProvider? timeProvider = null) :
    IAdmissionRecoveryDirectDeliveryChannel
{
    public async Task<AdmissionRecoveryDirectDeliveryResult> DeliverAsync(
        AdmissionRecoveryDirectDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.DeliveryIntentId == Guid.Empty ||
            request.AdmissionTicketId == Guid.Empty || request.RecoveryRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RecipientAddress) ||
            string.IsNullOrWhiteSpace(request.Capability))
        {
            return new AdmissionRecoveryDirectDeliveryResult(
                AdmissionRecoveryDirectDeliveryOutcome.Ambiguous);
        }

        string idempotencyKey = request.DeliveryIntentId.ToString("N");
        string baseUrl = (
            configuration["PublicBaseUrl"] ??
            configuration["App:PublicBaseUrl"] ??
            configuration["Application:PublicBaseUrl"] ??
            string.Empty).TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? origin) ||
            origin.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(origin.UserInfo))
        {
            return new AdmissionRecoveryDirectDeliveryResult(
                AdmissionRecoveryDirectDeliveryOutcome.Ambiguous);
        }

        string recoveryUrl =
            $"{origin.GetLeftPart(UriPartial.Authority)}/tickets/recovery" +
            $"#capability={Uri.EscapeDataString(request.Capability)}";
        if (request.DisclosureUntilUtc is { } deadline && (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime >= deadline)
            return new AdmissionRecoveryDirectDeliveryResult(AdmissionRecoveryDirectDeliveryOutcome.RetentionExpired);
        EmailResult result = await emailService.SendAsync(new EmailMessage
        {
            DisclosureUntilUtc = request.DisclosureUntilUtc,
            To = request.RecipientAddress,
            Subject = "Recover your admission ticket",
            PlainTextBody = $"Open this same-origin one-time recovery link:\n{recoveryUrl}",
            CustomHeaders =
            {
                ["X-Admission-Recovery-Idempotency-Key"] = idempotencyKey
            }
        }, cancellationToken);
        if (result.Outcome == SmtpDeliveryOutcome.RetentionExpired)
            return new AdmissionRecoveryDirectDeliveryResult(AdmissionRecoveryDirectDeliveryOutcome.RetentionExpired);
        return result.Success
            ? new AdmissionRecoveryDirectDeliveryResult(
                AdmissionRecoveryDirectDeliveryOutcome.Accepted,
                $"smtp:{idempotencyKey}")
            : new AdmissionRecoveryDirectDeliveryResult(
                AdmissionRecoveryDirectDeliveryOutcome.Ambiguous);
    }
}
