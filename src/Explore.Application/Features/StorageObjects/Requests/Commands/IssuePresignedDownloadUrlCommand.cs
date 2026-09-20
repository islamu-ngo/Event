using Explore.Application.Authorization;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Requests.Commands;

/// <summary>
/// Issues a time-limited download capability for a storage object by its ID.
/// </summary>
[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.PresignedDownload)]
public sealed record IssuePresignedDownloadUrlCommand : ICommand<PresignedDownloadUrlResponseDto?>, ISecureRequest
{
    /// <summary>
    /// The ID of the storage object.
    /// </summary>
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    /// <summary>
    /// The expiration time in minutes for the presigned URL. Default is 60.
    /// </summary>
    public int ExpirationMinutes { get; init; } = 60;

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new StorageObjectCollectionAuthorizationFacts(TenantId);
}
