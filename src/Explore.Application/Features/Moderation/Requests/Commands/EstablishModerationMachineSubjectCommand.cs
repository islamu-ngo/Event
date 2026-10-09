using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>Explicitly selects an existing service identity; current installation authority is server-owned.</summary>
public sealed record EstablishModerationMachineSubjectCommand : ICommand<ModerationMachineSubjectResponse>
{
    public required Guid Id { get; init; }
    public required Guid HostBindingId { get; init; }
    public required ModerationMachineCredentialSource CredentialSource { get; init; }
    public required string KeyId { get; init; }
    public Guid? AuthorityTenantId { get; init; }
    public required ExternalApiKeyOwnerType OwnerType { get; init; }
    public required Guid OwnerId { get; init; }
    public required Guid ServicePrincipalId { get; init; }
}

public sealed record ModerationMachineSubjectResponse : BaseCommandResponse<Guid>
{
    private ModerationMachineSubjectResponse(BaseCommandResponse<Guid> state,
        Guid? servicePrincipalId = null, long? revision = null) : base(state, true)
    {
        ServicePrincipalId = servicePrincipalId;
        Revision = revision;
    }

    public Guid? ServicePrincipalId { get; }
    public long? Revision { get; }

    internal static ModerationMachineSubjectResponse FromOutcome(
        Guid id, ModerationMachineSubjectBindingResult result) => result.Outcome switch
        {
            ModerationAuthoritySourceOutcome.Established when result.Binding is { } binding =>
                new(BaseCommandResponse.Success(binding.Id), binding.ServicePrincipalId, binding.Revision),
            ModerationAuthoritySourceOutcome.Unauthenticated => new(BaseCommandResponse.Authentication<Guid>()),
            ModerationAuthoritySourceOutcome.Denied => new(BaseCommandResponse.Authorization<Guid>()),
            ModerationAuthoritySourceOutcome.Conflict => new(BaseCommandResponse.Conflict(id)),
            _ => new(BaseCommandResponse.Failure<Guid>("ModerationAuthorityUnavailable"))
        };

    internal static ModerationMachineSubjectResponse Invalid(IEnumerable<string> errors) =>
        new(BaseCommandResponse.Validation<Guid>(errors));
}
