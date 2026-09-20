using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;

public sealed record EnsureTenantBrandingSettingsDocumentCommand : ICommand<TenantBrandingSettingsDocumentDto?>;
