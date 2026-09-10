
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

public sealed record DisableEmailDeliveryCommand(
    Guid? TenantId,
    long ExpectedRevision,
    string? Acknowledgement,
    string? ConfirmationToken) : IRequest<BaseCommandResponse<Guid>>
{
    public const string RequiredAcknowledgement = "DISABLE EMAIL DELIVERY";
}
