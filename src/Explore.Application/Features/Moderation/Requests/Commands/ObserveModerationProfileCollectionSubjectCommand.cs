using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.Requests.Commands;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ObserveModerationProfileCollectionSubjectCommand : ICommand<BaseCommandResponse<Guid>>
{
    [JsonRequired]
    public required Guid ProfileId { get; init; }

    [JsonRequired]
    public required ModerationNativeMode Mode { get; init; }

    public NominateModerationProfileCollectionSubjectObservation ToNomination() => new(ProfileId, Mode);
}
