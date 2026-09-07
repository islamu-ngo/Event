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
        return new AdmissionProtectedDeliveryMaterial(_protector.Protect(plaintext), CurrentVersion);
    }

    public AdmissionCredentialDeliveryEnvelope Unprotect(string ciphertext, int protectionVersion)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || protectionVersion != CurrentVersion)
        {
            throw new InvalidOperationException("Admission delivery envelope is unavailable.");
        }

        try
        {
            string plaintext = _protector.Unprotect(ciphertext);
            return JsonSerializer.Deserialize<AdmissionCredentialDeliveryEnvelope>(plaintext)
                ?? throw new InvalidOperationException("Admission delivery envelope is unavailable.");
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new InvalidOperationException("Admission delivery envelope is unavailable.", exception);
        }
    }
}
