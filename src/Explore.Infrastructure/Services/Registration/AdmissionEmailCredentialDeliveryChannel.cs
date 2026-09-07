using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionEmailCredentialDeliveryChannel(IEmailService emailService)
    : IAdmissionCredentialDirectDeliveryChannel
{
    public async Task<AdmissionCredentialDirectDeliveryResult> DeliverAsync(
        AdmissionCredentialDirectDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.DeliveryIntentId == Guid.Empty ||
            request.AdmissionTicketId == Guid.Empty || string.IsNullOrWhiteSpace(request.RecipientAddress) ||
            string.IsNullOrWhiteSpace(request.PlaintextCredential))
        {
            return new AdmissionCredentialDirectDeliveryResult(AdmissionCredentialDirectDeliveryOutcome.Ambiguous);
        }

        string idempotencyKey = request.DeliveryIntentId.ToString("N");
        EmailResult result = await emailService.SendAsync(new EmailMessage
        {
            To = request.RecipientAddress,
            Subject = "Your admission credential",
            PlainTextBody = $"Admission ticket: {request.AdmissionTicketId:N}\nCredential: {request.PlaintextCredential}",
            CustomHeaders =
            {
                ["X-Admission-Delivery-Idempotency-Key"] = idempotencyKey
            }
        }, cancellationToken);

        return result.Success
            ? new AdmissionCredentialDirectDeliveryResult(
                AdmissionCredentialDirectDeliveryOutcome.Accepted,
                $"smtp:{idempotencyKey}")
            : new AdmissionCredentialDirectDeliveryResult(AdmissionCredentialDirectDeliveryOutcome.Ambiguous);
    }
}
