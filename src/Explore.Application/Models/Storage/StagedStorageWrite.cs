namespace Explore.Application.Models.Storage;

/// <summary>Server-issued receipt; activation must recheck its durable producer under the database fence.</summary>
public sealed record StagedStorageWrite(
    Guid OperationId, Guid TenantId, Guid BindingId, FileStorageWriteResult Write);
