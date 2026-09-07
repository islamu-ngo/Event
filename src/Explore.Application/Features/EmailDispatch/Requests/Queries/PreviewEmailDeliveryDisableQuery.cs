// ABOUTME: Requests current email-disable impact for an independently authorized instance or tenant target.
// ABOUTME: Carries no actor authority; the handler resolves the current administrator from server context.

using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Queries;

public sealed record PreviewEmailDeliveryDisableQuery(Guid? TenantId)
    : IRequest<BaseCommandResponse<EmailDeliveryDisablePreviewDto>>;
