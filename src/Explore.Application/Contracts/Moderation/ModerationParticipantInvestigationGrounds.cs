using System.Security.Cryptography;
using System.Text.Json;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Investigator assertions awaiting native qualification, not established conduct or a permission.
/// Encoded bytes are transient plaintext: the native custody owner protects them and clears them
/// immediately. Neither the assertions nor their digest belongs in ordinary logs or case metadata.
/// </summary>
public sealed record ModerationParticipantInvestigationGrounds(
    string IdentificationGrounds, string AllegedConduct, string EvidenceProvenance,
    string ContradictionsAndCredentialMisuse)
{
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(IdentificationGrounds) && IdentificationGrounds.Length <= 2048
        && !string.IsNullOrWhiteSpace(AllegedConduct) && AllegedConduct.Length <= 2048
        && !string.IsNullOrWhiteSpace(EvidenceProvenance) && EvidenceProvenance.Length <= 2048
        && !string.IsNullOrWhiteSpace(ContradictionsAndCredentialMisuse) && ContradictionsAndCredentialMisuse.Length <= 2048;

    public byte[] Encode(int maximumMaterialBytes)
    {
        if (maximumMaterialBytes is < 1 or > 65536 || !IsValid())
            throw new ArgumentException("Investigation requires bounded identification, conduct, provenance and contradiction grounds.");
        var material = JsonSerializer.SerializeToUtf8Bytes(this);
        if (material.Length <= maximumMaterialBytes) return material;
        CryptographicOperations.ZeroMemory(material);
        throw new ArgumentException("Investigation grounds exceed the independently adopted custody size limit.");
    }

    public override string ToString() => nameof(ModerationParticipantInvestigationGrounds);
}
