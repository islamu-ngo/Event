namespace Explore.Application.Features.ConfigurationManifest.Requests.Queries;

using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.View)]
public sealed record ExportInstanceOperatorIdentityQuery
    : IQuery<BaseCommandResponse<OperatorIdentityManifest>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => OperatorIdentityManifestJson.SettingKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
