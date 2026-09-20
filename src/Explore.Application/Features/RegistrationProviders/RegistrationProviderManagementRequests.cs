using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Responses;

namespace Explore.Application.Features.RegistrationProviders.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewRegistrationProviderHealth)]
public sealed record GetRegistrationProviderHealthQuery(Guid TenantId, Guid EventId)
    : IQuery<IReadOnlyList<RegistrationProviderBindingHealthDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record GetRegistrationProviderQueueQuery(Guid TenantId, Guid EventId, int Limit)
    : IQuery<IReadOnlyList<RegistrationProviderParkedQueueItemDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record PollRegistrationProviderReconciliationCommand(Guid TenantId, Guid EventId, Guid BindingId, DateTime SinceUtc)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record QueueManualRegistrationProviderImportCommand(Guid TenantId, Guid EventId, Guid BindingId, string StorageReference, string SourceReference)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record RetryRegistrationProviderParkedItemCommand(Guid TenantId, Guid EventId, Guid? SubmissionId, Guid? EffectOutboxId, int? ExpectedProcessingGeneration, string Reason)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record ResolveRegistrationProviderQueueItemCommand(Guid TenantId, Guid EventId, Guid? SubmissionId, Guid? EffectOutboxId, string DecisionCode, string NoteReference)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record GetRegistrationProviderConnectionsQuery(Guid TenantId, Guid EventId)
    : IQuery<IReadOnlyList<RegistrationProviderConnectionDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Tenants.Update)]
public sealed record GetRegistrationProviderConnectionQuery(Guid TenantId, Guid EventId, Guid ConnectionId)
    : IQuery<RegistrationProviderConnectionDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.TenantFacts(TenantId);
}

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Tenants.Update)]
public sealed record UpsertRegistrationProviderConnectionCommand(Guid TenantId, Guid EventId, Guid? ConnectionId, RegistrationProviderConnectionRequestDto Request)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.TenantFacts(TenantId);
}

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Tenants.Update)]
public sealed record ReplaceRegistrationProviderApprovedOriginsCommand(Guid TenantId, Guid EventId, Guid ConnectionId, IReadOnlyList<string> Origins)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.TenantFacts(TenantId);
}

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Tenants.Update)]
public sealed record DeleteRegistrationProviderConnectionCommand(Guid TenantId, Guid EventId, Guid ConnectionId)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.TenantFacts(TenantId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record GetRegistrationProviderBindingsQuery(Guid TenantId, Guid EventId)
    : IQuery<IReadOnlyList<RegistrationProviderBindingDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record GetRegistrationProviderBindingQuery(Guid TenantId, Guid EventId, Guid BindingId)
    : IQuery<RegistrationProviderBindingDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record CreateRegistrationProviderBindingCommand(Guid TenantId, Guid EventId, RegistrationProviderBindingRequestDto Request)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record UpdateRegistrationProviderBindingCommand(Guid TenantId, Guid EventId, Guid BindingId, RegistrationProviderBindingRequestDto Request)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record DeleteRegistrationProviderBindingCommand(Guid TenantId, Guid EventId, Guid BindingId)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record PublishEventRegistrationProviderBindingCommand(Guid TenantId, Guid EventId, Guid BindingId)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record ReplaceEventDraftRegistrationProviderMappingsCommand(Guid TenantId, Guid EventId, Guid BindingId, ReplaceRegistrationProviderMappingsRequestDto Request)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record ImportExternalRegistrationProviderFormVersionCommand(Guid TenantId, Guid EventId, Guid ConnectionId, ImportExternalRegistrationProviderFormVersionRequestDto Request)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record GetRegistrationChannelsQuery(Guid TenantId, Guid EventId, Guid WorkflowId, Guid RequirementId)
    : IQuery<IReadOnlyList<RegistrationChannelDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record UpsertRegistrationChannelCommand(Guid TenantId, Guid EventId, Guid WorkflowId, Guid RequirementId, Guid? ChannelId, RegistrationChannelRequestDto Request)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record DeleteRegistrationChannelCommand(Guid TenantId, Guid EventId, Guid WorkflowId, Guid RequirementId, Guid ChannelId)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationChannels)]
public sealed record GetRegistrationProviderLaunchDescriptorQuery(Guid TenantId, Guid EventId, Guid WorkflowId, Guid RequirementId, Guid ChannelId, Guid BindingId)
    : IQuery<RegistrationProviderLaunchDescriptorDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => RegistrationProviderAuthorization.EventFacts(TenantId, EventId);
}

public static class RegistrationProviderAuthorization
{
    /// <summary>
    /// Registration-provider management authorizes against its parent event. Missing tenant or event
    /// context yields no facts, which denies rather than falling back to the ambient tenant.
    /// </summary>
    public static IAuthorizationFacts? EventFacts(Guid tenantId, Guid eventId) =>
        tenantId == Guid.Empty || eventId == Guid.Empty
            ? null
            : new EventScopedAuthorizationFacts(tenantId, eventId);

    public static IAuthorizationFacts? TenantFacts(Guid tenantId) =>
        tenantId == Guid.Empty ? null : new TenantScopedAuthorizationFacts(tenantId);
}
