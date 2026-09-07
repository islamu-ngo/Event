using ISLAMU.Wire.Contracts.Admissions;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionRecoveryTicketDocumentService(
    IAdmissionTicketRecoveryRepository repository,
    IAdmissionCredentialDigestService credentialDigestService,
    IAdmissionQrRenderer qrRenderer,
    TimeProvider timeProvider) : IAdmissionRecoveryTicketDocumentService
{
    private const string CredentialPurpose = "AdmissionTicket";
    private const string SensitiveBearerClassification = "SENSITIVE_BEARER";

    public async Task<AdmissionRecoveryTicketDocument?> RotateAndCreateAsync(
        Guid tenantId,
        Guid admissionTicketId,
        CancellationToken cancellationToken)
    {
        AdmissionTicket? ticket = await repository.GetForUpdateAsync(
            tenantId,
            admissionTicketId,
            cancellationToken);
        if (ticket is null ||
            ticket.AdmissionTicketStatusId != (int)AdmissionTicketStatusEnum.Active)
        {
            return null;
        }

        AdmissionTicketCredential current = ticket.Credentials.Single(credential =>
            credential.AdmissionTicketCredentialStatusId ==
            (int)AdmissionTicketCredentialStatusEnum.Active);
        Guid credentialId = Guid.CreateVersion7();
        int nextVersion = current.CredentialVersion + 1;
        AdmissionCredentialMaterial material = await credentialDigestService.CreateAsync(
            new AdmissionCredentialCreateRequest(
                tenantId,
                admissionTicketId,
                credentialId,
                CredentialPurpose,
                nextVersion),
            cancellationToken);
        DateTime rotatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        ticket.RotateCredential(
            credentialId,
            material.CredentialVersion,
            material.KeyVersion,
            material.LookupDigest,
            rotatedAtUtc);
        await repository.SaveChangesAsync(cancellationToken);

        string payloadText = AdmissionQrPayloadCodec.Prefix + material.PlaintextCredential;
        if (!AdmissionQrPayloadCodec.TryDecode(payloadText, out AdmissionQrPayload? payload))
        {
            throw new InvalidOperationException("Rotated admission credential is not canonical.");
        }

        AdmissionQrSvg qr = qrRenderer.Render(payload!);
        return new AdmissionRecoveryTicketDocument(
            ticket.Id,
            ticket.EventId,
            AdmissionTicketStatusEnum.Active.ToString().ToUpperInvariant(),
            ticket.DisplayReference,
            material.PlaintextCredential,
            SensitiveBearerClassification,
            qr.Content,
            $"admission-ticket-print:v1:{ticket.Id:N}");
    }
}
