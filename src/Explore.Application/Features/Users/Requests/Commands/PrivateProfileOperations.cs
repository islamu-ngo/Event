using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Responses;

namespace Explore.Application.Features.Users.Requests.Commands;

public sealed record ReadProfileCollectionOptionsRequest(Guid UserId) : IQuery<IReadOnlyList<ModerationProfileCollectionOption>>;
public sealed record ReadPrivateBirthDateStatusRequest(Guid UserId) : IQuery<PrivateBirthDateStatus>;

public sealed record RecordPrivateBirthDateCommand(Guid UserId, DateOnly BirthDate, long ExpectedRevision)
    : ICommand<BaseCommandResponse<Guid>>
{
    public override string ToString() => nameof(RecordPrivateBirthDateCommand);
}

public sealed record ClearPrivateBirthDateCommand(Guid UserId, long ExpectedRevision) : ICommand<BaseCommandResponse<Guid>>;
