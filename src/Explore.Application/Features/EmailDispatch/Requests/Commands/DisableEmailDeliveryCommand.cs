
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

public sealed record DisableEmailDeliveryCommand(
    Guid? TenantId,
    long ExpectedRevision,
    string? Acknowledgement,
    string? ConfirmationToken) : ICommand<BaseCommandResponse<Guid>>
{
    public const string RequiredAcknowledgement = "DISABLE EMAIL DELIVERY";
}
