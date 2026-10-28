using System.Text.Json.Serialization;
using Explore.Application.Contracts.Services;

namespace Explore.Application.DTOs.User;

public sealed record PrivateProfileCompletionDto(
    IReadOnlyList<ModerationProfileCollectionOption> Options, PrivateBirthDateStatus BirthDateStatus);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordPrivateBirthDateDto
{
    public required DateOnly BirthDate { get; init; }
    public required long ExpectedRevision { get; init; }
    public override string ToString() => nameof(RecordPrivateBirthDateDto);
}
