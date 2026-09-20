
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Requests.Commands;

public sealed record PreviewEmailDeliveryDisableCommand(Guid? TenantId)
    : ICommand<BaseCommandResponse<EmailDeliveryDisablePreviewDto>>;
