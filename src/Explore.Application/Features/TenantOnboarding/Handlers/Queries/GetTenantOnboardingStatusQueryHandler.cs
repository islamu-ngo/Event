using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TenantOnboarding.Handlers.Queries;

public class GetTenantOnboardingStatusQueryHandler : IRequestHandler<GetTenantOnboardingStatusQuery, TenantOnboardingStatusDto>
{
    private readonly ITenantOnboardingStateRepository _tenantOnboardingStateRepository;
    private readonly IAdminContext _adminContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;

    public GetTenantOnboardingStatusQueryHandler(
        ITenantOnboardingStateRepository tenantOnboardingStateRepository,
        IAdminContext adminContext,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService)
    {
        _tenantOnboardingStateRepository = tenantOnboardingStateRepository;
        _adminContext = adminContext;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
    }

    public async Task<TenantOnboardingStatusDto> Handle(GetTenantOnboardingStatusQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var onboardingState = await _tenantOnboardingStateRepository.GetByTenantId(tenantId);
        var completedSteps = ParseCompletedSteps(onboardingState?.CompletedStepsJson);
        var totalSteps = onboardingState?.TotalSteps ?? 0;
        var currentStep = onboardingState?.CurrentStep ?? 0;

        var response = new TenantOnboardingStatusDto
        {
            TenantId = tenantId,
            IsCompleted = onboardingState?.IsCompleted == true,
            IsAuthenticated = _currentUserService.IsAuthenticated,
            IsCurrentUserTenantAdministrator = false,
            IsCurrentUserPlatformAdministrator = false,
            CurrentStep = currentStep,
            TotalSteps = totalSteps,
            CompletedSteps = completedSteps,
            ProgressPercentage = CalculateProgressPercentage(currentStep, totalSteps)
        };

        if (!_currentUserService.IsAuthenticated)
        {
            return response;
        }

        response.IsCurrentUserTenantAdministrator = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);
        response.IsCurrentUserPlatformAdministrator = await _adminContext.IsInstanceAdminAsync(cancellationToken);

        return response;
    }

    private static string[] ParseCompletedSteps(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static int CalculateProgressPercentage(int currentStep, int totalSteps)
    {
        if (totalSteps <= 0)
        {
            return 0;
        }

        var percent = (int)Math.Round((double)currentStep / totalSteps * 100, MidpointRounding.AwayFromZero);
        return Math.Clamp(percent, 0, 100);
    }
}
