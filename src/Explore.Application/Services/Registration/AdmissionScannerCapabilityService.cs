using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionScannerCapabilityService(
    IAdmissionScannerCapabilityRepository repository,
    IAdmissionScannerCapabilityMaterialService materialService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private const int MaximumDeviceLabelLength = 128;
    private const int MaximumRevocationReasonLength = 200;

    public async Task<AdmissionScannerCapabilityIssuedResult> IssueAsync(
        AdmissionScannerCapabilityIssueRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIssue(request);
        AdmissionTarget? target = await repository.FindPlatformManagedTargetAsync(
            request.TenantId,
            request.EventId,
            request.TargetId,
            cancellationToken);
        if (target is null ||
            target.TenantId != request.TenantId ||
            target.EventId != request.EventId ||
            target.Id != request.TargetId ||
            !target.IsOperational)
        {
            return new AdmissionScannerCapabilityIssuedResult(
                AdmissionScannerCapabilityIssueOutcome.Rejected,
                Guid.Empty,
                null,
                null);
        }

        Guid scannerCapabilityId = Guid.CreateVersion7();
        AdmissionScannerCapabilityMaterial material = await materialService.IssueAsync(
            new AdmissionScannerCapabilityMaterialRequest(
                request.IssueRequestId,
                request.TenantId,
                scannerCapabilityId),
            cancellationToken);
        ValidateMaterial(material);

        DateTime issuedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        AdmissionScannerCapability capability = AdmissionScannerCapability.Issue(
            scannerCapabilityId,
            request.TenantId,
            request.IssueRequestId,
            request.EventId,
            request.TargetId,
            material.KeyVersion,
            material.LookupDigest,
            request.DeviceLabel,
            ToDomainActions(request.Actions),
            request.ExpiresAtUtc.UtcDateTime,
            request.IssuedByActorId,
            issuedAtUtc);
        AdmissionScannerCapabilityStoreResult persisted = await unitOfWork.ExecuteInTransactionAsync(
            token => repository.StoreAsync(capability, token),
            cancellationToken);
        if (persisted.Rejected)
        {
            return new AdmissionScannerCapabilityIssuedResult(
                AdmissionScannerCapabilityIssueOutcome.Rejected,
                Guid.Empty,
                null,
                null);
        }

        AdmissionScannerCapabilityDescriptor descriptor = ToDescriptor(persisted.Capability);

        return new AdmissionScannerCapabilityIssuedResult(
            persisted.Created
                ? AdmissionScannerCapabilityIssueOutcome.Issued
                : AdmissionScannerCapabilityIssueOutcome.AlreadyIssued,
            persisted.Capability.Id,
            persisted.Created ? material.PlaintextCapability : null,
            descriptor);
    }

    public async Task<AdmissionScannerCapabilityDescriptor> ReadAsync(
        AdmissionScannerCapabilityReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.ScannerCapabilityId == Guid.Empty)
        {
            throw new ArgumentException("A tenant and scanner capability identity are required.", nameof(request));
        }

        AdmissionScannerCapability? capability = await repository.GetAsync(
            request.TenantId, request.ScannerCapabilityId, cancellationToken);
        return capability is not null && capability.TenantId == request.TenantId
            ? ToDescriptor(capability)
            : throw new KeyNotFoundException("Scanner capability is unavailable.");
    }

    public async Task<IReadOnlyList<AdmissionScannerCapabilityDescriptor>> ListAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ArgumentException("A tenant and event identity are required.", nameof(eventId));
        }

        IReadOnlyList<AdmissionScannerCapability> capabilities = await repository.ListAsync(
            tenantId, eventId, cancellationToken);
        return capabilities.Where(capability => capability.TenantId == tenantId && capability.EventId == eventId)
            .Select(ToDescriptor)
            .ToArray();
    }

    public Task<AdmissionScannerCapabilityRevocationResult> RevokeAsync(
        AdmissionScannerCapabilityRevokeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.EventId == Guid.Empty ||
            request.ScannerCapabilityId == Guid.Empty ||
            request.RevokedByActorId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > MaximumRevocationReasonLength)
        {
            throw new ArgumentException("A valid scanner capability revocation is required.", nameof(request));
        }

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            AdmissionScannerCapability? capability = await repository.GetAsync(
                request.TenantId, request.ScannerCapabilityId, token);
            if (capability is null ||
                capability.TenantId != request.TenantId ||
                capability.EventId != request.EventId)
            {
                return new AdmissionScannerCapabilityRevocationResult(
                    AdmissionScannerCapabilityRevocationOutcome.Rejected,
                    request.ScannerCapabilityId);
            }

            AdmissionScannerCapabilityRevocationTransition transition = capability.Revoke(
                request.RevokedByActorId,
                request.Reason,
                timeProvider.GetUtcNow().UtcDateTime);
            if (transition == AdmissionScannerCapabilityRevocationTransition.Revoked)
            {
                capability = await repository.UpdateAsync(capability, token);
            }

            return new AdmissionScannerCapabilityRevocationResult(
                AdmissionScannerCapabilityRevocationOutcome.Revoked,
                capability.Id);
        }, cancellationToken);
    }

    private void ValidateIssue(AdmissionScannerCapabilityIssueRequest request)
    {
        if (request is null || request.IssueRequestId == Guid.Empty || request.TenantId == Guid.Empty ||
            request.EventId == Guid.Empty || request.TargetId == Guid.Empty ||
            request.IssuedByActorId == Guid.Empty || request.Actions is null or { Count: 0 } ||
            request.Actions.Any(action => !Enum.IsDefined(action)) ||
            request.Actions.Distinct().Count() != request.Actions.Count ||
            string.IsNullOrWhiteSpace(request.DeviceLabel) ||
            request.DeviceLabel.Trim().Length > MaximumDeviceLabelLength ||
            request.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            throw new ArgumentException("A valid bounded scanner capability issue request is required.", nameof(request));
        }
    }

    private static void ValidateMaterial(AdmissionScannerCapabilityMaterial material)
    {
        if (material is null || string.IsNullOrWhiteSpace(material.PlaintextCapability) ||
            string.IsNullOrWhiteSpace(material.LookupDigest) || material.KeyVersion < 1)
        {
            throw new InvalidOperationException("Scanner capability material service returned invalid material.");
        }
    }

    private static AdmissionScannerCapabilityAction ToDomainActions(
        IReadOnlyList<AdmissionCheckInAction> actions)
    {
        AdmissionScannerCapabilityAction result = AdmissionScannerCapabilityAction.None;
        foreach (AdmissionCheckInAction action in actions)
        {
            result |= action switch
            {
                AdmissionCheckInAction.CheckIn => AdmissionScannerCapabilityAction.CheckIn,
                AdmissionCheckInAction.Undo => AdmissionScannerCapabilityAction.Undo,
                _ => throw new ArgumentOutOfRangeException(nameof(actions))
            };
        }

        return result;
    }

    private static AdmissionScannerCapabilityDescriptor ToDescriptor(
        AdmissionScannerCapability capability) => new(
        capability.Id,
        capability.TenantId,
        capability.EventId,
        capability.AdmissionTargetId,
        ToApplicationActions(capability.Actions),
        capability.DeviceLabel,
        new DateTimeOffset(capability.ExpiresAt),
        capability.RevokedAt.HasValue,
        "********")
    {
        RevokedAtUtc = capability.RevokedAt.HasValue
            ? new DateTimeOffset(capability.RevokedAt.Value)
            : null
    };

    private static AdmissionCheckInAction[] ToApplicationActions(
        AdmissionScannerCapabilityAction actions)
    {
        var result = new List<AdmissionCheckInAction>(2);
        if (actions.HasFlag(AdmissionScannerCapabilityAction.CheckIn))
        {
            result.Add(AdmissionCheckInAction.CheckIn);
        }
        if (actions.HasFlag(AdmissionScannerCapabilityAction.Undo))
        {
            result.Add(AdmissionCheckInAction.Undo);
        }

        return result.ToArray();
    }
}
