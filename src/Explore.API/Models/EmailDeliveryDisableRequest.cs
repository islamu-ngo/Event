
using System.Text.Json.Serialization;

namespace Explore.API.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmailDeliveryDisableRequest
{
    public required long ExpectedRevision { get; init; }
    public string? ConfirmationToken { get; init; }
    public string? Acknowledgement { get; init; }
}
