namespace Explore.Application.Features.ConfigurationManifest.Requests.Commands;

using Explore.Application.Authorization;
using Explore.Application.Features.ConfigurationManifest.Importing;
using MediatR;

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.Update)]
public sealed record CreateInstanceConfigurationImportSessionCommand(
    ReadOnlyMemory<byte> Artifact)
    : IRequest<ConfigurationImportSessionCreatedResult>, ISecureRequest
{
    public const string ResourceKey = "instance.configuration-import";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;

    public override string ToString() =>
        nameof(CreateInstanceConfigurationImportSessionCommand);
}

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.Update)]
public sealed record CreateTenantConfigurationImportSessionCommand(
    Guid TenantId,
    ReadOnlyMemory<byte> Artifact)
    : IRequest<ConfigurationImportSessionCreatedResult>, ISecureRequest
{
    public const string ResourceKey = "tenant.configuration-import";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(TenantId, ResourceKey);

    public override string ToString() =>
        nameof(CreateTenantConfigurationImportSessionCommand);
}

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.Update)]
public sealed record PreviewInstanceConfigurationImportSessionCommand(
    Guid SessionId,
    string AccessToken,
    ConfigurationImportPreviewRequest Preview)
    : IRequest<ConfigurationImportPreviewResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateInstanceConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;

    public override string ToString() =>
        nameof(PreviewInstanceConfigurationImportSessionCommand);
}

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.Update)]
public sealed record PreviewTenantConfigurationImportSessionCommand(
    Guid TenantId,
    Guid SessionId,
    string AccessToken,
    ConfigurationImportPreviewRequest Preview)
    : IRequest<ConfigurationImportPreviewResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateTenantConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(
            TenantId,
            CreateTenantConfigurationImportSessionCommand.ResourceKey);

    public override string ToString() =>
        nameof(PreviewTenantConfigurationImportSessionCommand);
}

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.Update)]
public sealed record CancelInstanceConfigurationImportSessionCommand(
    Guid SessionId,
    string AccessToken)
    : IRequest, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateInstanceConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;

    public override string ToString() =>
        nameof(CancelInstanceConfigurationImportSessionCommand);
}

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.Update)]
public sealed record CancelTenantConfigurationImportSessionCommand(
    Guid TenantId,
    Guid SessionId,
    string AccessToken)
    : IRequest, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateTenantConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(
            TenantId,
            CreateTenantConfigurationImportSessionCommand.ResourceKey);

    public override string ToString() =>
        nameof(CancelTenantConfigurationImportSessionCommand);
}

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.Update)]
public sealed record ApplyInstanceConfigurationImportCommand(
    Guid SessionId,
    string AccessToken,
    ConfigurationImportPreviewRequest Preview,
    Guid? RollbackOfOperationId = null,
    Guid? ManagedScheduleId = null)
    : IRequest<ConfigurationImportOperationResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateInstanceConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.Update)]
public sealed record ApplyTenantConfigurationImportCommand(
    Guid TenantId,
    Guid SessionId,
    string AccessToken,
    ConfigurationImportPreviewRequest Preview,
    Guid? RollbackOfOperationId = null,
    Guid? ManagedScheduleId = null)
    : IRequest<ConfigurationImportOperationResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateTenantConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(
            TenantId,
            CreateTenantConfigurationImportSessionCommand.ResourceKey);
}

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.Update)]
public sealed record CreateInstanceConfigurationRollbackSessionCommand(
    Guid OperationId)
    : IRequest<ConfigurationImportRollbackSessionCreatedResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateInstanceConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.Update)]
public sealed record CreateTenantConfigurationRollbackSessionCommand(
    Guid TenantId,
    Guid OperationId)
    : IRequest<ConfigurationImportRollbackSessionCreatedResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateTenantConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(
            TenantId,
            CreateTenantConfigurationImportSessionCommand.ResourceKey);
}
