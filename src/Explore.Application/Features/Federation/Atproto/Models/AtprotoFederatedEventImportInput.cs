namespace Explore.Application.Features.Federation.Atproto.Models;

public sealed record AtprotoFederatedEventImportInput(
    string Name,
    DateTimeOffset? CreatedAt)
{
    public string? Description { get; init; }
    public string? SourceUrl { get; init; }
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }
    public string? Mode { get; init; }
    public string? Status { get; init; }
    public bool? RsvpExpected { get; init; }
}
