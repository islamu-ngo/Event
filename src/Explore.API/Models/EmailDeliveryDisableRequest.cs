// ABOUTME: Binds only preview evidence and deliberate acknowledgement for an email-disable confirmation.
// ABOUTME: Scope and actor authority are never accepted from the HTTP body.

using System.Text.Json.Serialization;

namespace Explore.API.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmailDeliveryDisableRequest
{
    public required long ExpectedRevision { get; init; }
    public string? ConfirmationToken { get; init; }
    public string? Acknowledgement { get; init; }
}
