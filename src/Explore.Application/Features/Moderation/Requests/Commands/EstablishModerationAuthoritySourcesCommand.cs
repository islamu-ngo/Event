using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>
/// Installation-owned, already allocated UUIDv7 coordinates and an actual selected tenant.
/// Current identity and permission are resolved by the server, never supplied by this command.
/// </summary>
public sealed record EstablishModerationAuthoritySourcesCommand : ICommand<ModerationAuthoritySourcesResponse>
{
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid SolutionId { get; init; }
    public required Guid InstanceId { get; init; }
    public required Guid MaintenanceServicePrincipalId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid EpochId { get; init; }
}

public sealed record ModerationAuthoritySourcesResponse : BaseCommandResponse<Guid>
{
    private ModerationAuthoritySourcesResponse(BaseCommandResponse<Guid> state, Guid? epochId = null, long? revision = null)
        : base(state, true)
    {
        EpochId = epochId;
        Revision = revision;
    }

    public Guid? EpochId { get; }
    public long? Revision { get; }

    internal static ModerationAuthoritySourcesResponse FromOutcome(
        Guid bindingId, ModerationAuthoritySourceResult result) => result.Outcome switch
        {
            ModerationAuthoritySourceOutcome.Established when result.Binding is { } binding && result.Epoch is { } epoch =>
                new(BaseCommandResponse.Success(binding.Id), epoch.Id, epoch.Revision),
            ModerationAuthoritySourceOutcome.Unauthenticated => new(BaseCommandResponse.Authentication<Guid>()),
            ModerationAuthoritySourceOutcome.Denied => new(BaseCommandResponse.Authorization<Guid>()),
            ModerationAuthoritySourceOutcome.Conflict => new(BaseCommandResponse.Conflict(bindingId)),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationAuthorityUnavailable"))
        };

    internal static ModerationAuthoritySourcesResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
