
using System.Text.Json.Serialization;

namespace Explore.Application.Features.Authentication.Local.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalPasswordChangeRequestDto
{
    [LocalPassword(newCredential: false)]
    public required string CurrentPassword { get; init; }
    [LocalPassword]
    public required string NewPassword { get; init; }
    public override string ToString() => nameof(LocalPasswordChangeRequestDto);
}
