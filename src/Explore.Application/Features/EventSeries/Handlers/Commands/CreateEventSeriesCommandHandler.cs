using System.Linq;
using System.Text.RegularExpressions;
using AutoMapper;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Commands;

public class CreateEventSeriesCommandHandler : IRequestHandler<CreateEventSeriesCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IMapper _mapper;

    public CreateEventSeriesCommandHandler(
        IEventSeriesRepository eventSeriesRepository,
        ITenantContext tenantContext,
        IAdminContext adminContext,
        IStorageObjectRepository storageObjectRepository,
        IMapper mapper)
    {
        _eventSeriesRepository = eventSeriesRepository;
        _tenantContext = tenantContext;
        _adminContext = adminContext;
        _storageObjectRepository = storageObjectRepository;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventSeriesCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        Guid? userId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        IReadOnlyList<Guid> adminTenantIds = userId.HasValue
            ? await _adminContext.GetAdminTenantIdsAsync(userId.Value, cancellationToken)
            : [];

        if (!adminTenantIds.Contains(tenantId))
        {
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Create);
        }

        var validator = new CreateEventSeriesDtoValidator();
        var validationResult = await validator.ValidateAsync(request.EventSeriesDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event series creation failed due to validation errors.");
        }

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                tenantId,
                request.EventSeriesDto.FeaturedImageId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Featured image must be an active public safe-raster object in the current tenant."],
                "Event series creation failed due to validation errors.");
        }

        var series = _mapper.Map<Domain.EventSeries>(request.EventSeriesDto);
        series.TenantId = tenantId;
        series.TotalViews = 0;
        series.VisibilityTypeId = 1; // Default: Public

        if (string.IsNullOrWhiteSpace(series.Slug))
        {
            series.Slug = GenerateSlug(series.Title);
        }

        series = await _eventSeriesRepository.Create(series);

        return BaseCommandResponse.Success(series.Id, "Event series created successfully.");
    }

    private static string GenerateSlug(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return $"series-{Guid.CreateVersion7().ToString("N")[..8]}";

        var slug = title.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "")
            .Replace(".", "")
            .Replace(",", "");

        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

        if (slug.Length > 50)
            slug = slug[..50];

        return slug;
    }
}
