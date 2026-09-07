using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Explore.Application.DTOs.Organization;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Organizations.Requests.Queries;

public sealed record GetOrganizationDetailsRequest(Guid Id = default) : IRequest<OrganizationDto?>;
