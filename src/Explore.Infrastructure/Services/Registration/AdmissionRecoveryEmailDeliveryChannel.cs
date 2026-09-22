using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Microsoft.Extensions.Configuration;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionRecoveryEmailDeliveryChannel(
    IEmailService emailService,
    IConfiguration configuration,
    ISystemSettingRepository systemSettings,
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
        var origin = await PublicAddressResolver.ResolveAsync(configuration, systemSettings, cancellationToken);
        if (origin is not { Scheme: "https" })
        {
            return new AdmissionRecoveryDirectDeliveryResult(
                AdmissionRecoveryDirectDeliveryOutcome.Ambiguous);
        }

        string recoveryUrl =
            new Uri(origin, "tickets/recovery").AbsoluteUri +
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
