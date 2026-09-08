using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionRecoveryRequestEnvelopeProtector :
    IAdmissionRecoveryRequestEnvelopeProtector
{
    private const int CurrentVersion = 1;
    private readonly IDataProtector protector;

    public AdmissionRecoveryRequestEnvelopeProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(
            "Explore.Admissions.RecoveryRequest",
            $"v{CurrentVersion}");
    }

    public AdmissionRecoveryProtectedDeliveryMaterial Protect(
        AdmissionRecoveryRequestEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.NormalizedIdentity) ||
            envelope.Purpose != AdmissionRecoveryPurpose.TicketRecovery)
        {
            throw new ArgumentException("Complete recovery request material is required.", nameof(envelope));
        }

        return new AdmissionRecoveryProtectedDeliveryMaterial(
            protector.Protect(JsonSerializer.Serialize(envelope)),
            CurrentVersion);
    }

    public AdmissionRecoveryRequestEnvelope Unprotect(string ciphertext, int protectionVersion)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || protectionVersion != CurrentVersion)
        {
            throw new InvalidOperationException("Recovery request envelope is unavailable.");
        }

        try
        {
            return JsonSerializer.Deserialize<AdmissionRecoveryRequestEnvelope>(
                    protector.Unprotect(ciphertext))
                ?? throw new InvalidOperationException("Recovery request envelope is unavailable.");
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new InvalidOperationException("Recovery request envelope is unavailable.", exception);
        }
    }
}
