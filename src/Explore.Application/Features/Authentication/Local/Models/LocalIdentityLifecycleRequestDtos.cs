
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;

namespace Explore.Application.Features.Authentication.Local.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalEmailVerificationRequestDto
{
    [StringLength(256, MinimumLength = 1)]
    public string? Identifier { get; init; }
    [StringLength(256, MinimumLength = 1)]
    [EmailAddress]
    public string? ProposedEmail { get; init; }
    public override string ToString() => nameof(LocalEmailVerificationRequestDto);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalPasswordRecoveryRequestDto
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public required string Identifier { get; init; }
    public override string ToString() => nameof(LocalPasswordRecoveryRequestDto);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalEmailConfirmationRequestDto
{
    public required Guid OperationId { get; init; }
    public required Guid LocalSubjectId { get; init; }
    public required Guid PersonalActorId { get; init; }
    public required Guid ExternalLoginId { get; init; }
    public required LocalIdentityLifecyclePurpose Purpose { get; init; }
    public required Guid Generation { get; init; }
    [Required]
    [StringLength(8192, MinimumLength = 1)]
    public required string Token { get; init; }
    public override string ToString() => nameof(LocalEmailConfirmationRequestDto);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocalPasswordRecoveryCompletionRequestDto
{
    public required Guid OperationId { get; init; }
    public required Guid LocalSubjectId { get; init; }
    public required Guid PersonalActorId { get; init; }
    public required Guid ExternalLoginId { get; init; }
    public required LocalIdentityLifecyclePurpose Purpose { get; init; }
    public required Guid Generation { get; init; }
    [Required]
    [StringLength(8192, MinimumLength = 1)]
    public required string Token { get; init; }
    [Required]
    [StringLength(LocalIdentityOptions.MaximumPasswordLength, MinimumLength = LocalIdentityOptions.MinimumPasswordLength)]
    public required string NewPassword { get; init; }
    public override string ToString() => nameof(LocalPasswordRecoveryCompletionRequestDto);
}
