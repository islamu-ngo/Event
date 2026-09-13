using Explore.Application.DTOs.Integrations;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Queries;

public sealed record GetListmonkIntegrationSettingsQuery : IQuery<ListmonkIntegrationSettingsDto>
{
}
