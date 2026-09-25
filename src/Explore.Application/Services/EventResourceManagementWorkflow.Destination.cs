using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventResources;
using Explore.Application.Responses;
using Explore.Application.Validation;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed partial class EventResourceManagementWorkflow
{
    public async Task<BaseCommandResponse<Guid>> ConfigureExternalDestinationAsync(Guid resourceId,
        Guid expectedVersion, string destination, IEventResourceDestinationProtector protector,
        CancellationToken cancellationToken)
    {
        var request = Request(resourceId, "update");
        await using var authorized = await authority.AuthorizeAsync(request, EmptyPreparation, cancellationToken);
        if (authorized.Lease is not { } lease) return Failure(authorized.Outcome, resourceId, cancellationToken);
        var policy = lease.Snapshot.Facts.Access.GovernancePolicy;
        if (policy is null) return Failure(EventResourceAuthorityOutcome.Unavailable, resourceId, cancellationToken);
        if (!policy.EnabledDeliveryTypes.Contains(EventResourceDeliveryTypeEnum.ExternalLink)
            || !EventResourceDestinationValidator.TryValidate(destination, policy,
                out string? validated, out string? safeOrigin))
            return Invalid();

        string protectedDestination;
        try
        {
            protectedDestination = protector.Protect(validated!, request.TenantId, resourceId,
                protector.CurrentVersion);
        }
        catch (Exception)
        {
            // Keyring failures may carry provider coordinates; neither errors nor audit may echo them.
            return BaseCommandResponse.Failure<Guid>(EventResourceManagementFailureCodes.Unavailable);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var outcome = await authority.RecheckMutationAsync(lease, expectedVersion, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed)
                    return Failure(outcome, resourceId, ct);
                var eventId = lease.Snapshot.Facts.Access.Parent.EventId;
                var resource = await resources.GetByIdForUpdateAsync(request.TenantId, eventId, resourceId, ct);
                if (resource is null) return BaseCommandResponse.NotFound<Guid>();
                if (resource.IsDeleted || resource.PublicationStateId == (int)EventResourcePublicationStateEnum.Archived
                    || resource.ConcurrencyStamp != expectedVersion)
                    return BaseCommandResponse.Conflict(resourceId);

                if (resource.StorageObjectId is { } oldObject)
                    await lifecycle.RetireAsync(request.TenantId, [], [oldObject], now, ct);
                Guid? detached = resource.SetExternalDestination(protectedDestination, protector.CurrentVersion, safeOrigin!,
                    expectedVersion, request.SubjectUserId!.Value, now);
                resources.Update(resource);
                if (policy.AuditRetentionDays > 0)
                    await resources.AddAuditEntryAsync(Audit(request, resourceId,
                        EventResourceAuditAction.ConfigureDelivery, now), ct);
                await resources.SaveChangesAsync(ct);
                if (detached is { } removedObject)
                    await lifecycle.RemoveTransferredSourcesAsync(request.TenantId, [], [removedObject], ct);
                return BaseCommandResponse.Success(resourceId);
            }, cancellationToken);
        }
        catch (ArgumentException) { return Invalid(); }
        catch (ConcurrencyConflictException) { return BaseCommandResponse.Conflict(resourceId); }
    }
}
