
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Explore.Application.Configuration;

namespace Explore.Application.Features.Authentication.Local.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalPasswordChangeRequestDto
{
    [Required]
    [StringLength(LocalIdentityOptions.MaximumPasswordLength, MinimumLength = 1)]
    public required string CurrentPassword { get; init; }
    [Required]
    [StringLength(LocalIdentityOptions.MaximumPasswordLength, MinimumLength = LocalIdentityOptions.MinimumPasswordLength)]
    public required string NewPassword { get; init; }
    public override string ToString() => nameof(LocalPasswordChangeRequestDto);
}
