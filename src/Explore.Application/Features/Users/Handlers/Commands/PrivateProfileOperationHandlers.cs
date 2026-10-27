using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Users.Handlers.Commands;

public sealed class RecordPrivateBirthDateCommandHandler(IModerationProfileCollectionAdmissionOwner owner)
    : ICommandHandler<RecordPrivateBirthDateCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(RecordPrivateBirthDateCommand request,
        CancellationToken cancellationToken = default)
    {
        await owner.RecordBirthDateAsync(request.UserId, request.BirthDate, request.ExpectedRevision, cancellationToken);
        return BaseCommandResponse.Success(request.UserId, "Private declaration updated.");
    }
}

public sealed class ClearPrivateBirthDateCommandHandler(IModerationProfileCollectionAdmissionOwner owner)
    : ICommandHandler<ClearPrivateBirthDateCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ClearPrivateBirthDateCommand request,
        CancellationToken cancellationToken = default)
    {
        await owner.ClearBirthDateAsync(request.UserId, request.ExpectedRevision, cancellationToken);
        return BaseCommandResponse.Success(request.UserId, "Private declaration removed.");
    }
}
