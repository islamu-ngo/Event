using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Users.Handlers.Commands;

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

public sealed class RecordPrivateBirthDateHandler(IModerationProfileCollectionAdmissionOwner owner)
    : ICommandHandler<RecordPrivateBirthDateCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(RecordPrivateBirthDateCommand request,
        CancellationToken cancellationToken = default)
    {
        await owner.RecordBirthDateAsync(request.UserId, request.BirthDate, request.ExpectedRevision, cancellationToken);
        return BaseCommandResponse.Success(request.UserId, "Private declaration updated.");
    }
}

public sealed class ClearPrivateBirthDateHandler(IModerationProfileCollectionAdmissionOwner owner)
    : ICommandHandler<ClearPrivateBirthDateCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ClearPrivateBirthDateCommand request,
        CancellationToken cancellationToken = default)
    {
        await owner.ClearBirthDateAsync(request.UserId, request.ExpectedRevision, cancellationToken);
        return BaseCommandResponse.Success(request.UserId, "Private declaration removed.");
    }
}
