// ABOUTME: Defines the password-only body for an ordinary authenticated Local password change.
// ABOUTME: Keeps subject, credential stamp, and binding authority out of caller-controlled JSON.

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
