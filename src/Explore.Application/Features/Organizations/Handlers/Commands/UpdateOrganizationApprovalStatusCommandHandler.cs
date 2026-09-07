using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Organizations.Handlers.Commands;

public class UpdateOrganizationApprovalStatusCommandHandler : IRequestHandler<UpdateOrganizationApprovalStatusCommand, Unit>
{
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IApprovalStatusRepository _statusTypeRepository;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;

    public UpdateOrganizationApprovalStatusCommandHandler(
        IOrganizationTenantRepository organizationTenantRepository,
        IApprovalStatusRepository statusTypeRepository,
        ITenantContext tenantContext,
        HybridCache cache)
    {
        _organizationTenantRepository = organizationTenantRepository;
        _statusTypeRepository = statusTypeRepository;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    public async Task<Unit> Handle(UpdateOrganizationApprovalStatusCommand request, CancellationToken cancellationToken)
    {
        var participation = await _organizationTenantRepository.GetByOrganizationAndTenant(
            request.OrganizationId,
            _tenantContext.TenantId,
            cancellationToken);
        if (participation == null)
        {
            throw new NotFoundException(nameof(Organization), request.OrganizationId);
        }

        var validator = new UpdateOrganizationApprovalStatusDtoValidator(_statusTypeRepository);
        var validationResult = await validator.ValidateAsync(request.ApprovalStatusDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult);
        }

        participation.ApprovalStatusId = request.ApprovalStatusDto.ApprovalStatusId;

        await _organizationTenantRepository.Update(participation);
        await _cache.RemoveAsync($"organization:detail:{participation.OrganizationId}", cancellationToken);

        return Unit.Value;
    }
}
