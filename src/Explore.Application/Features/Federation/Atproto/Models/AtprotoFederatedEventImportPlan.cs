namespace Explore.Application.Features.Federation.Atproto.Models;

using Explore.Application.DTOs.Event;
using Explore.Application.Models.Storage;

public sealed record AtprotoFederatedEventImportPlan(
    Guid TenantId,
    Guid AtprotoRecordId,
    string Did,
    string AtUri,
    string Name,
    DateTimeOffset CreatedAt,
    string? Description,
    string? SourceUrl,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Mode,
    string? Status,
    bool? RsvpExpected)
{
    public string TimeZoneId { get; init; } = "UTC";
    public required ConfigureEventParticipationDto ParticipationConfiguration { get; init; }
    public AtprotoThumbnailBlobCandidate? Thumbnail { get; init; }
    public FileStorageWriteResult? StagedThumbnail { get; init; }
}
