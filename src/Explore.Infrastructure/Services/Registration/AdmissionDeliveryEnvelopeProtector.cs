// ABOUTME: Protects recoverable admission delivery envelopes with the shared persistent Data Protection key ring.
// ABOUTME: Keeps recipient and bearer encrypted at rest and maps cryptographic failures to a redacted boundary.

using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionDeliveryEnvelopeProtector : IAdmissionDeliveryEnvelopeProtector
{
    private const int CurrentVersion = 1;
    private readonly IDataProtector _protector;

    public AdmissionDeliveryEnvelopeProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("Explore.Admissions.CredentialDelivery", $"v{CurrentVersion}");
    }

    public AdmissionProtectedDeliveryMaterial Protect(AdmissionCredentialDeliveryEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.RecipientAddress) ||
            string.IsNullOrWhiteSpace(envelope.PlaintextCredential))
        {
            throw new ArgumentException("Complete admission delivery material is required.", nameof(envelope));
        }

        string plaintext = JsonSerializer.Serialize(envelope);
        if (envelope.DisclosureUntilUtc is null && envelope.AccountUserId is null)
            return new AdmissionProtectedDeliveryMaterial(_protector.Protect(plaintext), CurrentVersion);

        var payload = new AdmissionContactDeliveryPayload(envelope.DisclosureUntilUtc, string.Empty) { AccountUserId = envelope.AccountUserId };
        string ciphertext = _protector.CreateProtector(payload.GetProtectionPurpose()).Protect(plaintext);
        return new AdmissionProtectedDeliveryMaterial(JsonSerializer.Serialize(payload with { Ciphertext = ciphertext }), 2);
    }

    public AdmissionCredentialDeliveryEnvelope Unprotect(string ciphertext, int protectionVersion)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || protectionVersion is not (CurrentVersion or 2))
        {
            throw new InvalidOperationException("Admission delivery envelope is unavailable.");
        }

        try
        {
            AdmissionContactDeliveryPayload payload = AdmissionContactDeliveryPayload.Read(ciphertext, protectionVersion);
            IDataProtector boundProtector = protectionVersion == 2
                ? _protector.CreateProtector(payload.GetProtectionPurpose()) : _protector;
            string plaintext = boundProtector.Unprotect(payload.Ciphertext);
            var envelope = JsonSerializer.Deserialize<AdmissionCredentialDeliveryEnvelope>(plaintext)
                ?? throw new InvalidOperationException("Admission delivery envelope is unavailable.");
            if (envelope.DisclosureUntilUtc != payload.DisclosureUntilUtc || envelope.AccountUserId != payload.AccountUserId)
                throw new InvalidOperationException("Admission delivery envelope bound is invalid.");
            return envelope;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new InvalidOperationException("Admission delivery envelope is unavailable.", exception);
        }
    }
}
