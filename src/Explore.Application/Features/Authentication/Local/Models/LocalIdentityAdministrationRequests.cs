// ABOUTME: Defines closed request bodies for generated-only administrative Local credential issuance.
// ABOUTME: Keeps actor authority, target routing, verification, and passwords outside client-controlled creation intent.

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Identity;

namespace Explore.Application.Features.Authentication.Local.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLocalIdentityRequestDto
{
    [Required]
    public required Guid OperationId { get; init; }
    [Required, StringLength(256), EmailAddress]
    public required string Email { get; init; }
    [Required, StringLength(200)]
    public required string FirstName { get; init; }
    [StringLength(200)]
    public required string LastName { get; init; }

    public override string ToString() => nameof(CreateLocalIdentityRequestDto);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResetLocalCredentialRequestDto
{
    [Required]
    public required Guid OperationId { get; init; }
    [Required]
    public required Guid ExpectedCurrentOperationId { get; init; }
    [Required]
    public required Guid ExpectedCurrentOperationConcurrencyStamp { get; init; }
    [Required, StringLength(LocalCredentialResetRequest.MaximumReasonLength)]
    public required string Reason { get; init; }

    public override string ToString() => nameof(ResetLocalCredentialRequestDto);
}
