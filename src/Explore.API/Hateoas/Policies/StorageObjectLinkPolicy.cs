// ABOUTME: HAL link policies for storage object metadata and file access affordances.
// ABOUTME: Emits content, update, create, and delete links through server-side authorization metadata.

namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Hateoas;
using Explore.Domain;

/// <summary>
/// Link policy for StorageObjectDto (detail view).
/// Provides links for storage object operations.
/// </summary>
public sealed class StorageObjectDetailLinkPolicy : ILinkPolicy<StorageObjectDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(StorageObjectDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetStorageObjectById,
            new { id = dto.Id },
            "GET",
            dto.FullName);

        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetStorageObjects,
            null,
            "GET",
            "All storage objects");

        if (CanReadContent(dto))
        {
            yield return new LinkDefinition(
                "content",
                RouteNames.GetStorageObjectContent,
                new { id = dto.Id },
                "GET",
                "Download storage object content",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.StorageObjects.Download, ResourceDescriptors.StorageObject, dto);
        }

        if (CanReadPublicImage(dto))
        {
            yield return new LinkDefinition(
                "public-image",
                RouteNames.GetPublicStorageObjectImage,
                new { id = dto.Id },
                "GET",
                "Public image content");
        }

        if (CanReadContent(dto) && dto.ContentEligibility.PresignedDownloadAllowed)
        {
            yield return new LinkDefinition(
                "presigned-download",
                RouteNames.GetStorageObjectPresignedDownloadUrl,
                new { id = dto.Id },
                "GET",
                "Get a temporary download URL",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.StorageObjects.PresignedDownload, ResourceDescriptors.StorageObject, dto);
        }

        if (CanMutate(dto))
        {
            yield return new LinkDefinition(
                LinkRelations.Edit,
                RouteNames.UpdateStorageObject,
                new { id = dto.Id },
                "PATCH",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.StorageObject, dto);

            yield return LinkDefinition.Delete(
                RouteNames.DeleteStorageObject,
                new { id = dto.Id })
                .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.StorageObject, dto);
        }
    }

    private static bool CanReadContent(StorageObjectDto dto) =>
        string.Equals(dto.LifecycleState, StorageObjectLifecycleStates.Active, StringComparison.Ordinal)
        && !dto.IsDeleted
        && dto.ContentEligibility.ContentAllowed;

    private static bool CanReadPublicImage(StorageObjectDto dto) =>
        CanReadContent(dto)
        && string.Equals(dto.Visibility, StorageObjectVisibilities.PublicImage, StringComparison.Ordinal);

    private static bool CanMutate(StorageObjectDto dto) =>
        !dto.IsDeleted
        && dto.DeletedAt is null
        && dto.LifecycleState is not StorageObjectLifecycleStates.Deleted
            and not StorageObjectLifecycleStates.DeleteRequested;
}

/// <summary>
/// Link policy for StorageObjectListDto (collection items).
/// </summary>
public sealed class StorageObjectCollectionLinkPolicy : ICollectionLinkPolicy<StorageObjectListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(StorageObjectListDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetStorageObjectById,
            new { id = dto.Id },
            "GET",
            dto.FullName);

        if (CanReadContent(dto))
        {
            yield return new LinkDefinition(
                "content",
                RouteNames.GetStorageObjectContent,
                new { id = dto.Id },
                "GET",
                "Download storage object content",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.StorageObjects.Download,
                    ResourceKinds.StorageObject,
                    dto.Id.ToString(),
                    new AuthorizationScope(TenantId: dto.TenantId.ToString()),
                    StorageObjectFacts(dto));
        }

        if (CanReadPublicImage(dto))
        {
            yield return new LinkDefinition(
                "public-image",
                RouteNames.GetPublicStorageObjectImage,
                new { id = dto.Id },
                "GET",
                "Public image content");
        }

        if (CanMutate(dto))
        {
            yield return new LinkDefinition(
                LinkRelations.Edit,
                RouteNames.UpdateStorageObject,
                new { id = dto.Id },
                "PATCH",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update,
                    ResourceKinds.StorageObject,
                    dto.Id.ToString(),
                    new AuthorizationScope(TenantId: dto.TenantId.ToString()),
                    StorageObjectFacts(dto));

            yield return LinkDefinition.Delete(
                RouteNames.DeleteStorageObject,
                new { id = dto.Id })
                .RequirePermission(AuthorizationActions.Delete,
                    ResourceKinds.StorageObject,
                    dto.Id.ToString(),
                    new AuthorizationScope(TenantId: dto.TenantId.ToString()),
                    StorageObjectFacts(dto));
        }
    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetStorageObjects,
            null,
            "GET",
            "All storage objects");

        yield return new LinkDefinition(
            "create-upload-session",
            RouteNames.CreateStorageUploadSession,
            null,
            "POST",
            "Create upload session",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(StorageObjectDto), ResourceKinds.StorageObject);
    }

    private static bool CanReadContent(StorageObjectListDto dto) =>
        string.Equals(dto.LifecycleState, StorageObjectLifecycleStates.Active, StringComparison.Ordinal)
        && dto.ContentEligibility.ContentAllowed;

    private static bool CanReadPublicImage(StorageObjectListDto dto) =>
        CanReadContent(dto)
        && string.Equals(dto.Visibility, StorageObjectVisibilities.PublicImage, StringComparison.Ordinal);

    private static bool CanMutate(StorageObjectListDto dto) =>
        dto.LifecycleState is not StorageObjectLifecycleStates.Deleted
            and not StorageObjectLifecycleStates.DeleteRequested;

    private static IAuthorizationFacts StorageObjectFacts(StorageObjectListDto dto) =>
        new PersistedStorageObjectAuthorizationFacts(
            dto.TenantId,
            dto.Id,
            dto.Visibility,
            dto.LifecycleState,
            CreatedBy: null,
            OwningResourceKind: null,
            OwningResourceId: null);
}
