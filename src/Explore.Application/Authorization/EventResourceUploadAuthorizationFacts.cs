namespace Explore.Application.Authorization;

/// <summary>Server-resolved upload ownership; generic storage roles never authorize these sessions.</summary>
public sealed record EventResourceUploadAuthorizationFacts(Guid TenantId, Guid UploadSessionId, Guid ResourceId)
    : IAuthorizationFacts;
