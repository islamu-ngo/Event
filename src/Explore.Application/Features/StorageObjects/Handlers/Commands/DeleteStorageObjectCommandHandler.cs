using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.StorageObjects.Handlers.Commands;

public sealed class DeleteStorageObjectCommandHandler(
    IStorageObjectRepository storageObjects,
    IEventResourceStorageLifecycleRepository lifecycle,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DeleteStorageObjectCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        DeleteStorageObjectCommand request, CancellationToken cancellationToken)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var source = await storageObjects.GetForGenericAccessAsync(request.Id, ct);
                if (source is null)
                    return BaseCommandResponse.NotFound<Guid>(id: request.Id);

                var admission = await lifecycle.TryQueueRetirementAsync(
                    source.TenantId, source.Id, utcNow, ct);
                return admission switch
                {
                    StorageRetirementAdmission.Pending =>
                        BaseCommandResponse.Success(request.Id, "Storage cleanup is pending."),
                    StorageRetirementAdmission.NotFound =>
                        BaseCommandResponse.NotFound<Guid>(id: request.Id),
                    StorageRetirementAdmission.InUse =>
                        BaseCommandResponse.Failure(FailureCodes.StorageObjectInUse,
                            "The storage object is still in use.", id: request.Id),
                    StorageRetirementAdmission.RetentionBlocked =>
                        BaseCommandResponse.Failure(FailureCodes.StorageObjectRetentionBlocked,
                            "Storage retention prevents cleanup.", id: request.Id),
                    StorageRetirementAdmission.InvalidTarget =>
                        BaseCommandResponse.Failure(FailureCodes.StorageObjectInvalidTarget,
                            "The captured storage target cannot be retired.", id: request.Id),
                    _ => throw new InvalidOperationException("Unknown storage retirement acknowledgement.")
                };
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return BaseCommandResponse.Conflict(request.Id);
        }
    }
}
