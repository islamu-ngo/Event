using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionRecoveryDeliveryEnvelopeProtector :
    IAdmissionRecoveryDeliveryEnvelopeProtector
{
    private const int CurrentVersion = 1;
    private readonly IDataProtector protector;

    public AdmissionRecoveryDeliveryEnvelopeProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector("Explore.Admissions.RecoveryDelivery", $"v{CurrentVersion}");
    }

    public AdmissionRecoveryProtectedDeliveryMaterial Protect(
        AdmissionRecoveryDeliveryEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.RecipientAddress) ||
            envelope.RecoveryRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(envelope.Capability))
        {
            throw new ArgumentException("Complete recovery delivery material is required.", nameof(envelope));
        }

        string plaintext = JsonSerializer.Serialize(envelope);
        return new AdmissionRecoveryProtectedDeliveryMaterial(
            protector.Protect(plaintext),
            CurrentVersion);
    }

    public AdmissionRecoveryDeliveryEnvelope Unprotect(string ciphertext, int protectionVersion)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || protectionVersion != CurrentVersion)
        {
            throw new InvalidOperationException("Recovery delivery envelope is unavailable.");
        }

        try
        {
            string plaintext = protector.Unprotect(ciphertext);
            return JsonSerializer.Deserialize<AdmissionRecoveryDeliveryEnvelope>(plaintext)
                ?? throw new InvalidOperationException("Recovery delivery envelope is unavailable.");
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new InvalidOperationException("Recovery delivery envelope is unavailable.", exception);
        }
    }
}
