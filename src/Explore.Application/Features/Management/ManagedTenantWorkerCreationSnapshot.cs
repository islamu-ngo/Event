using System.Text.Json;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.Management.Validators;
using Explore.Domain;

namespace Explore.Application.Features.Management;

/// <summary>
/// Complete retained operation data. A snapshot is not admission authority: the
/// worker issuer must separately retain its genuine original outbox execution.
/// </summary>
public sealed class ManagedTenantWorkerCreationSnapshot
{
    private readonly string operationImage;
    private readonly string requestImage;

    private ManagedTenantWorkerCreationSnapshot(ManagedTenantProvisioningOperation operation,
        ManagementTenantProvisioningRequestDto normalizedRequest)
    {
        operationImage = JsonSerializer.Serialize(operation);
        requestImage = ManagedTenantProvisioningRequestCodec.Serialize(normalizedRequest);
        OperationId = operation.Id;
        ManagedInstanceId = operation.ManagedInstanceId;
        OutboxMessageId = operation.CurrentOutboxMessageId;
        RequestHash = operation.RequestHash;
        RowVersion = operation.RowVersion;
    }

    public Guid OperationId { get; }
    public Guid ManagedInstanceId { get; }
    public Guid OutboxMessageId { get; }
    public string RequestHash { get; }
    public uint RowVersion { get; }

    /// <summary>Returns a detached request; callers cannot mutate the retained original image.</summary>
    public ManagementTenantProvisioningRequestDto ReadRequest() =>
        ManagedTenantProvisioningRequestCodec.Deserialize(requestImage);

    public static ManagedTenantWorkerCreationSnapshot Capture(
        ManagedTenantProvisioningOperation operation, Guid expectedOutboxMessageId)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Id == Guid.Empty || operation.ManagedInstanceId == Guid.Empty
            || expectedOutboxMessageId == Guid.Empty || operation.CurrentOutboxMessageId != expectedOutboxMessageId
            || operation.Status != ManagedTenantProvisioningStatus.Processing
            || string.IsNullOrWhiteSpace(operation.RequestJson))
            throw Changed();
        ManagementTenantProvisioningRequestDto request;
        try
        {
            request = ManagedTenantProvisioningRequestCodec.Deserialize(operation.RequestJson);
            if (!new ManagementTenantProvisioningRequestValidator().Validate(request).IsValid)
                throw Changed();
            request = ManagedTenantProvisioningRequestCodec.Normalize(request);
        }
        catch (JsonException)
        {
            throw Changed();
        }
        if (ManagedTenantProvisioningRequestCodec.ComputeHash(request) != operation.RequestHash
            || request.ExternalRequestId != operation.ExternalRequestId
            || request.ExternalCustomerReference != operation.ExternalCustomerReference
            || request.TenantSlug != operation.TenantSlug)
            throw Changed();
        return new(operation, request);
    }

    public void RequireCurrent(ManagedTenantProvisioningOperation current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (JsonSerializer.Serialize(current) != operationImage)
            throw Changed();
    }

    private static InvalidOperationException Changed() => new("managed_worker_creation_source_changed");
}
