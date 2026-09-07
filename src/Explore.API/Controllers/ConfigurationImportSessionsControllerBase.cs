namespace Explore.API.Controllers;

using System.Buffers;
using Explore.API.ConfigurationImport;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc;

public abstract class ConfigurationImportSessionsControllerBase
    : EventControllerBase
{
    private protected static async Task<ReadOnlyMemory<byte>> ReadArtifactAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength is > ConfigurationImportApiBoundary
                .MaximumUploadBytes)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TooLarge);
        }

        int capacity = request.ContentLength is > 0
            ? checked((int)request.ContentLength.Value)
            : 16 * 1024;
        using var destination = new MemoryStream(capacity);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                int read = await request.Body.ReadAsync(
                    buffer,
                    cancellationToken);
                if (read == 0)
                    break;
                if (destination.Length + read
                    > ConfigurationImportApiBoundary.MaximumUploadBytes)
                {
                    throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.TooLarge);
                }
                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }
        }
        finally
        {
            Array.Clear(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (destination.Length == 0)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }
        return destination.ToArray();
    }

    private protected HalResource<T> WithSessionLinks<T>(
        T resource,
        Guid sessionId,
        Guid? tenantId)
        where T : class
    {
        object values = tenantId is { } targetTenantId
            ? new { tenantId = targetTenantId, sessionId }
            : new { sessionId };
        string previewRoute = tenantId.HasValue
            ? RouteNames.PreviewTenantConfigurationImportSession
            : RouteNames.PreviewInstanceConfigurationImportSession;
        string refreshRoute = tenantId.HasValue
            ? RouteNames.RefreshTenantConfigurationImportSession
            : RouteNames.RefreshInstanceConfigurationImportSession;
        string cancelRoute = tenantId.HasValue
            ? RouteNames.CancelTenantConfigurationImportSession
            : RouteNames.CancelInstanceConfigurationImportSession;
        string applyRoute = tenantId.HasValue
            ? RouteNames.ApplyTenantConfigurationImportSession
            : RouteNames.ApplyInstanceConfigurationImportSession;
        HalResource<T> result = new HalResource<T>(resource)
            .WithLink(
                LinkRelations.PreviewConfigurationImport,
                HalLink.CreateAction(
                    Url.Link(previewRoute, values)
                    ?? throw new InvalidOperationException(
                        "Configuration import preview route is unavailable."),
                    HttpMethods.Post))
            .WithLink(
                LinkRelations.RefreshConfigurationImportPreview,
                HalLink.CreateAction(
                    Url.Link(refreshRoute, values)
                    ?? throw new InvalidOperationException(
                        "Configuration import refresh route is unavailable."),
                    HttpMethods.Post))
            .WithLink(
                LinkRelations.CancelConfigurationImport,
                HalLink.CreateAction(
                    Url.Link(cancelRoute, values)
                    ?? throw new InvalidOperationException(
                        "Configuration import cancel route is unavailable."),
                    HttpMethods.Delete));
        return resource is ConfigurationImportPreviewResult { IsApplyReady: true }
            ? result.WithLink(
                LinkRelations.ApplyConfigurationImport,
                HalLink.CreateAction(
                    Url.Link(applyRoute, values)
                    ?? throw new InvalidOperationException(
                        "Configuration import apply route is unavailable."),
                    HttpMethods.Post))
            : result;
    }

    private protected HalResource<ConfigurationImportOperationResult>
        WithOperationLinks(
        ConfigurationImportOperationResult operation,
        Guid? tenantId,
        bool canRollback)
    {
        object receiptValues = tenantId is { } targetTenantId
            ? new { tenantId = targetTenantId, operationId = operation.OperationId }
            : new { operationId = operation.OperationId };
        string receiptRoute = tenantId.HasValue
            ? RouteNames.GetTenantConfigurationImportReceipt
            : RouteNames.GetInstanceConfigurationImportReceipt;
        string historyRoute = tenantId.HasValue
            ? RouteNames.ListTenantConfigurationImportHistory
            : RouteNames.ListInstanceConfigurationImportHistory;
        string rollbackRoute = tenantId.HasValue
            ? RouteNames.CreateTenantConfigurationRollbackSession
            : RouteNames.CreateInstanceConfigurationRollbackSession;
        object historyValues = tenantId is { } tenant
            ? new { tenantId = tenant }
            : new { };
        HalResource<ConfigurationImportOperationResult> resource =
            new HalResource<ConfigurationImportOperationResult>(operation)
                .WithLink(
                    LinkRelations.ConfigurationImportReceipt,
                    HalLink.Create(
                        Url.Link(receiptRoute, receiptValues)
                        ?? throw new InvalidOperationException(
                            "Configuration import receipt route is unavailable.")))
                .WithLink(
                    LinkRelations.ConfigurationImportHistory,
                    HalLink.Create(
                        Url.Link(historyRoute, historyValues)
                        ?? throw new InvalidOperationException(
                            "Configuration import history route is unavailable.")));
        return operation.SnapshotAvailable && canRollback
            ? resource.WithLink(
                LinkRelations.CreateConfigurationImportRollback,
                HalLink.CreateAction(
                    Url.Link(rollbackRoute, receiptValues)
                    ?? throw new InvalidOperationException(
                        "Configuration import rollback route is unavailable."),
                    HttpMethods.Post))
            : resource;
    }

    private protected async Task<bool> CanUpdateAsync(
        IAuthorizationProvider authorization,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        bool tenant = tenantId.HasValue;
        AuthorizationDecision decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                tenant
                    ? ResourceKinds.TenantSetting
                    : ResourceKinds.InstanceSetting,
                tenant
                    ? CreateTenantConfigurationImportSessionCommand.ResourceKey
                    : CreateInstanceConfigurationImportSessionCommand.ResourceKey,
                tenant
                    ? AuthorizationActions.TenantSettings.Update
                    : AuthorizationActions.InstanceSettings.Update,
                tenant
                    ? new AuthorizationScope(TenantId: tenantId!.Value.ToString("D"))
                    : AuthorizationScope.Empty,
                tenant
                    ? new TenantSettingAuthorizationFacts(
                        tenantId!.Value,
                        CreateTenantConfigurationImportSessionCommand.ResourceKey)
                    : InstanceScopedAuthorizationFacts.Instance,
                new AuthorizationSubject(RequiredUserId)),
            cancellationToken);
        return decision.IsAllowed;
    }
}
