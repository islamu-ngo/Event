// ABOUTME: Safe administrative representation of registration-file quarantine and release audit state.
// ABOUTME: Exposes no provider object key, raw URI, checksum, or downloadable credential.

using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Registration;

public sealed record RegistrationAnswerFileDto(
    Guid Id,
    Guid RegistrationSubmissionId,
    Guid RegistrationFormFieldId,
    Guid StorageObjectId,
    string SafeDisplayName,
    string ContentType,
    string Extension,
    long Size,
    string QuarantineState,
    string ScanStatus,
    DateTime QuarantinedAt,
    Guid? ReleasedBy,
    DateTime? ReleasedAt,
    string? ReleaseReason)
{
    // Filename disclosure is independent of quarantine release and physical legal holds.
    [JsonIgnore]
    public bool MetadataDisclosureAllowed { get; init; }

    [JsonIgnore]
    public DateTime? DisclosureUntilUtc { get; init; }

    public RegistrationAnswerFileDto ForDisclosureAt(DateTime utcNow) =>
        MetadataDisclosureAllowed && (DisclosureUntilUtc is null || utcNow < DisclosureUntilUtc.Value)
            ? this
            : this with { SafeDisplayName = string.Empty };
}

public sealed record RegistrationAnswerFileReleaseInputDto(string Reason);
