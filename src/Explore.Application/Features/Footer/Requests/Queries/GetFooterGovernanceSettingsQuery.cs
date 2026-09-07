using Explore.Application.DTOs.Footer;
using MediatR;

namespace Explore.Application.Features.Footer.Requests.Queries;

public record GetFooterGovernanceSettingsQuery : IRequest<FooterGovernanceSettingsDto>;
