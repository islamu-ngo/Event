using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record CloneControlPlaneTenantPlanCommand
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public CloneControlPlaneTenantPlanCommand(Guid sourceVersionId, string key, string name)
    {
        SourceVersionId = sourceVersionId;
        Key = key;
        Name = name;
    }

    public const string SettingKey = "control-plane.tenant-plans";

    public Guid SourceVersionId { get; }
    public string Key { get; }
    public string Name { get; }

    string? ISecureRequest.ResourceId => SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
