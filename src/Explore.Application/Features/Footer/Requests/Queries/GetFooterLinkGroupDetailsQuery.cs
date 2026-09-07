using Explore.Application.DTOs.Footer;
using MediatR;

namespace Explore.Application.Features.Footer.Requests.Queries;

public record GetFooterLinkGroupDetailsQuery(Guid GroupId) : IRequest<FooterLinkGroupDetailsDto>;
