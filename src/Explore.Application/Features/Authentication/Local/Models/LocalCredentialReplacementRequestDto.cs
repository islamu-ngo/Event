// ABOUTME: Defines the password-only HTTP body for private Local credential replacement.
// ABOUTME: Rejects extra authority fields and bounds password input without disclosing it in diagnostic formatting.

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Explore.Application.Configuration;

namespace Explore.Application.Features.Authentication.Local.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalCredentialReplacementRequestDto
{
    [Required]
    [StringLength(LocalIdentityOptions.MaximumPasswordLength, MinimumLength = LocalIdentityOptions.MinimumPasswordLength)]
    public required string NewPassword { get; init; }

    public override string ToString() => nameof(LocalCredentialReplacementRequestDto);
}
