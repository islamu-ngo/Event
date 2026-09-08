using Explore.Application.DTOs.TenantSettingsDocuments;
using MediatR;

namespace Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;

public sealed record GetTenantBrandingSettingsDocumentQuery : IRequest<TenantBrandingSettingsDocumentDto?>;
