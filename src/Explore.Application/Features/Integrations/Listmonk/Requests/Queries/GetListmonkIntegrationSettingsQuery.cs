using Explore.Application.DTOs.Integrations;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Queries;

public sealed record GetListmonkIntegrationSettingsQuery : IRequest<ListmonkIntegrationSettingsDto>
{
}
