using Explore.Application.DTOs.Footer;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Footer.Requests.Queries;

public sealed record GetFooterConfigQuery : IQuery<FooterConfigDto>;
