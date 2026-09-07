namespace Explore.Application.Features.ConfigurationManifest.Requests.Queries;

using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using MediatR;

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.View)]
public sealed record GetInstanceConfigurationImportReceiptQuery(
    Guid OperationId)
    : IRequest<ConfigurationImportOperationResult>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateInstanceConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.View)]
public sealed record GetTenantConfigurationImportReceiptQuery(
    Guid TenantId,
    Guid OperationId)
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
    AuthorizationActions.InstanceSettings.View)]
public sealed record ListInstanceConfigurationImportHistoryQuery(
    int MaximumCount = 50)
    : IRequest<ImmutableArray<ConfigurationImportOperationResult>>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateInstanceConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(
    ResourceKinds.TenantSetting,
    AuthorizationActions.TenantSettings.View)]
public sealed record ListTenantConfigurationImportHistoryQuery(
    Guid TenantId,
    int MaximumCount = 50)
    : IRequest<ImmutableArray<ConfigurationImportOperationResult>>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        CreateTenantConfigurationImportSessionCommand.ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantSettingAuthorizationFacts(
            TenantId,
            CreateTenantConfigurationImportSessionCommand.ResourceKey);
}
