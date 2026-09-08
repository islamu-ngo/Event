// ABOUTME: Protects recovery recipient and capability envelopes with the persistent Data Protection key ring.
// ABOUTME: Uses a recovery-specific cryptographic purpose and exposes only redacted failures.

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
        if (envelope.DisclosureUntilUtc is not { } deadline)
            return new AdmissionRecoveryProtectedDeliveryMaterial(protector.Protect(plaintext), CurrentVersion);

        var payload = new AdmissionContactDeliveryPayload(deadline, string.Empty);
        string ciphertext = protector.CreateProtector(payload.GetProtectionPurpose()).Protect(plaintext);
        return new AdmissionRecoveryProtectedDeliveryMaterial(JsonSerializer.Serialize(payload with { Ciphertext = ciphertext }), 2);
    }

    public AdmissionRecoveryDeliveryEnvelope Unprotect(string ciphertext, int protectionVersion)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || protectionVersion is not (CurrentVersion or 2))
        {
            throw new InvalidOperationException("Recovery delivery envelope is unavailable.");
        }

        try
        {
            AdmissionContactDeliveryPayload payload = AdmissionContactDeliveryPayload.Read(ciphertext, protectionVersion);
            if (payload.AccountUserId.HasValue)
                throw new InvalidOperationException("Recovery requires order contact authority.");
            IDataProtector boundProtector = protectionVersion == 2
                ? protector.CreateProtector(payload.GetProtectionPurpose()) : protector;
            string plaintext = boundProtector.Unprotect(payload.Ciphertext);
            var envelope = JsonSerializer.Deserialize<AdmissionRecoveryDeliveryEnvelope>(plaintext)
                ?? throw new InvalidOperationException("Recovery delivery envelope is unavailable.");
            if (envelope.DisclosureUntilUtc != payload.DisclosureUntilUtc)
                throw new InvalidOperationException("Recovery delivery envelope bound is invalid.");
            return envelope;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new InvalidOperationException("Recovery delivery envelope is unavailable.", exception);
        }
    }
}
