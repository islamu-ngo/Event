using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.Organization.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Organizations.Handlers.Commands;

public class UpdateOrganizationCommandHandler : IRequestHandler<UpdateOrganizationCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly HybridCache _cache;

    public UpdateOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        HybridCache cache)
    {
        _organizationRepository = organizationRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateOrganizationDtoValidator();
        var validationResult = await validator.ValidateAsync(request.UpdateOrganizationDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Organization update failed.");
        }

        var organization = await _organizationRepository.GetById(request.OrganizationId);
        if (organization == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Organization not found.");
        }

        var authorizationFailure = await AuthorizeOrganizationAdminAsync(request, cancellationToken);
        if (authorizationFailure is not null)
        {
            throw new AuthorizationException(ResourceKinds.Organization, AuthorizationActions.Update);
        }

        if (organization.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The organization was modified by another request. Reload and retry.",
                nameof(Organization),
                organization.Id.ToString());
        }

        ApplyFullName(organization, request.UpdateOrganizationDto.FullName);
        ApplyWebsiteUrl(organization, request.UpdateOrganizationDto.WebsiteUrl);
        ApplyEmail(organization, request.UpdateOrganizationDto.Email);
        ApplyCountry(organization, request.UpdateOrganizationDto.Country);
        ApplyCity(organization, request.UpdateOrganizationDto.City);
        ApplyPostcode(organization, request.UpdateOrganizationDto.Postcode);
        ApplyAddress(organization, request.UpdateOrganizationDto.Address);

        await _organizationRepository.Update(organization);

        await _cache.RemoveAsync($"organization:detail:{organization.Id}", cancellationToken);
        return BaseCommandResponse.Success(organization.Id, "Organization updated successfully.");
    }

    private async Task<string?> AuthorizeOrganizationAdminAsync(UpdateOrganizationCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userGuid))
        {
            return "Invalid user ID.";
        }

        var members = await _organizationMemberRepository.GetMembersByOrganizationId(request.OrganizationId);
        var requesterMember = members.FirstOrDefault(member => member.UserId == userGuid);
        return requesterMember?.RoleId == (int)RoleEnum.OrgAdmin
            ? null
            : "You are not authorized to update this organization.";
    }

    private static void ApplyFullName(Organization organization, UpdateOrganizationFullNameDto? group)
    {
        if (group is not null)
        {
            organization.FullName = group.Value;
        }
    }

    private static void ApplyWebsiteUrl(Organization organization, UpdateOrganizationWebsiteUrlDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            organization.WebsiteUrl = group.Value.Value;
        }
    }

    private static void ApplyEmail(Organization organization, UpdateOrganizationEmailDto? group)
    {
        if (group is not null)
        {
            organization.Email = group.Value;
        }
    }

    private static void ApplyCountry(Organization organization, UpdateOrganizationCountryDto? group)
    {
        if (group is not null)
        {
            organization.Country = group.Value;
        }
    }

    private static void ApplyCity(Organization organization, UpdateOrganizationCityDto? group)
    {
        if (group is not null)
        {
            organization.City = group.Value;
        }
    }

    private static void ApplyPostcode(Organization organization, UpdateOrganizationPostcodeDto? group)
    {
        if (group is not null)
        {
            organization.Postcode = group.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static void ApplyAddress(Organization organization, UpdateOrganizationAddressDto? group)
    {
        if (group is not null)
        {
            organization.Address = group.Value;
        }
    }
}
