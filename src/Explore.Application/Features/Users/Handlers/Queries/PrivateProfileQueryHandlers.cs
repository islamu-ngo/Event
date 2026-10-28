using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Users.Requests.Commands;

namespace Explore.Application.Features.Users.Handlers.Queries;

public sealed class ReadProfileCollectionOptionsHandler(IModerationProfileCollectionAdmissionOwner owner)
    : IQueryHandler<ReadProfileCollectionOptionsRequest, IReadOnlyList<ModerationProfileCollectionOption>>
{
    public Task<IReadOnlyList<ModerationProfileCollectionOption>> QueryAsync(ReadProfileCollectionOptionsRequest request,
        CancellationToken cancellationToken = default) => owner.ReadOptionsAsync(request.UserId, cancellationToken);
}

public sealed class ReadPrivateBirthDateStatusHandler(IModerationProfileCollectionAdmissionOwner owner)
    : IQueryHandler<ReadPrivateBirthDateStatusRequest, PrivateBirthDateStatus>
{
    public Task<PrivateBirthDateStatus> QueryAsync(ReadPrivateBirthDateStatusRequest request,
        CancellationToken cancellationToken = default) => owner.ReadBirthDateStatusAsync(request.UserId, cancellationToken);
}
