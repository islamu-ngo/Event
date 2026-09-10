
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Queries;

public sealed record PreviewEmailDeliveryDisableQuery(Guid? TenantId)
    : IRequest<BaseCommandResponse<EmailDeliveryDisablePreviewDto>>;
