using Explore.Application.Authorization;
using Explore.Domain;

namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookOwnershipScopeResolver
{
    Task<WebhookOwnershipScopeResolution> ResolveAsync(
        int ownerKindId,
        Guid? requestedOwnerId,
        CancellationToken cancellationToken);

    Task<WebhookOwnershipScopeResolution> ResolvePersistedAsync(
        WebhookOwnedResourceKind resourceKind,
        Guid resourceId,
        CancellationToken cancellationToken);
}

public sealed record WebhookOwnershipScopeResolution(
    WebhookOwnershipScope? Scope,
    string? FailureCode,
    string? Error)
{
    public bool IsResolved => Scope is not null;

    public static WebhookOwnershipScopeResolution Resolved(WebhookOwnershipScope scope) =>
        new(scope, null, null);

    public static WebhookOwnershipScopeResolution Failed(string failureCode, string error) =>
        new(null, failureCode, error);
}
