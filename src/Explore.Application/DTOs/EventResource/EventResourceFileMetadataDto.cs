using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventResource;

/// <summary>Descriptive file metadata, never a storage locator, checksum, or reusable access grant.</summary>
public sealed record EventResourceFileMetadataDto(
    string FileName, string? ContentType, long SizeBytes, string SafetyState)
{
    [JsonIgnore]
    internal string? AttachmentGeneration { get; init; }
    public override string ToString() => nameof(EventResourceFileMetadataDto);
}
