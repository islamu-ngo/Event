using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Explore.Application.DTOs.Organization;
using Explore.Domain;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Organizations.Requests.Queries;

public sealed record GetOrganizationDetailsRequest(Guid Id = default) : IQuery<OrganizationDto?>;
