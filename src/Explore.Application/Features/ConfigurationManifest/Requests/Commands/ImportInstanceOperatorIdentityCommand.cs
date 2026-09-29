namespace Explore.Application.Features.ConfigurationManifest.Requests.Commands;

using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using System.Text.Json.Serialization;

[AuthorizeResource(
    ResourceKinds.InstanceSetting,
    AuthorizationActions.InstanceSettings.Update)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportInstanceOperatorIdentityCommand(
    OperatorIdentityManifest Manifest,
    string ExpectedRevisionHash)
    : ICommand<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => OperatorIdentityManifestJson.SettingKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;

    public override string ToString() => nameof(ImportInstanceOperatorIdentityCommand);
}
