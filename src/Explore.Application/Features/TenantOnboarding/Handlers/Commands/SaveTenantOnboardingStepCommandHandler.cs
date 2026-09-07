using System.Linq;
using System.Text.Json;
using Explore.Application.Analytics;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.TenantOnboarding.Handlers.Commands;

public class SaveTenantOnboardingStepCommandHandler : IRequestHandler<SaveTenantOnboardingStepCommand, BaseCommandResponse<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantOnboardingStateRepository _tenantOnboardingStateRepository;
    private readonly IAnalyticsProvider _analyticsProvider;
    private readonly IAnalyticsConfigResolver _analyticsConfigResolver;
    private readonly IAnalyticsGovernanceService _analyticsGovernanceService;
    private readonly IAdminContext _adminContext;

    public SaveTenantOnboardingStepCommandHandler(
        ITenantContext tenantContext,
        ITenantOnboardingStateRepository tenantOnboardingStateRepository,
        IAnalyticsProvider analyticsProvider,
        IAnalyticsConfigResolver analyticsConfigResolver,
        IAnalyticsGovernanceService analyticsGovernanceService,
        IAdminContext adminContext)
    {
        _tenantContext = tenantContext;
        _tenantOnboardingStateRepository = tenantOnboardingStateRepository;
        _analyticsProvider = analyticsProvider;
        _analyticsConfigResolver = analyticsConfigResolver;
        _analyticsGovernanceService = analyticsGovernanceService;
        _adminContext = adminContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(SaveTenantOnboardingStepCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await IsUserAuthorizedAsync(tenantId, cancellationToken))
        {
            const string message = "Only tenant administrators or instance administrators can update tenant onboarding progress.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var onboardingState = await _tenantOnboardingStateRepository.GetByTenantId(tenantId);
        if (onboardingState == null)
        {
            onboardingState = await _tenantOnboardingStateRepository.Create(new TenantOnboardingState
            {
                TenantId = tenantId,
                Tenant = null!,
                IsCompleted = false,
                CurrentStep = NormalizeStep(request.CurrentStep, request.TotalSteps),
                TotalSteps = NormalizeTotalSteps(request.TotalSteps),
                CompletedStepsJson = SerializeCompletedSteps(request.CompletedSteps.ToArray()),
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            onboardingState.CurrentStep = NormalizeStep(request.CurrentStep, request.TotalSteps);
            onboardingState.TotalSteps = NormalizeTotalSteps(request.TotalSteps);
            onboardingState.CompletedStepsJson = SerializeCompletedSteps(request.CompletedSteps.ToArray());
            await _tenantOnboardingStateRepository.Update(onboardingState);
        }

        await TrackStepAsync(request.UserId, onboardingState.CurrentStep, onboardingState.TotalSteps, request.CompletedSteps.ToArray(), cancellationToken);

        return BaseCommandResponse.Success(onboardingState.Id, "Tenant onboarding progress saved.");
    }

    private async Task TrackStepAsync(
        Guid userId,
        int currentStep,
        int totalSteps,
        string[] completedSteps,
        CancellationToken cancellationToken)
    {
        var analyticsConfiguration = await _analyticsConfigResolver.ResolveAsync(cancellationToken);
        var stepName = completedSteps.LastOrDefault() ?? string.Empty;
        var rawProperties = new Dictionary<string, object?>
        {
            [AnalyticsEvents.Properties.TenantId] = _tenantContext.TenantId,
            [AnalyticsEvents.Properties.StepIndex] = currentStep,
            [AnalyticsEvents.Properties.StepName] = stepName,
            [AnalyticsEvents.Properties.TotalSteps] = totalSteps,
            [AnalyticsEvents.Properties.CompletedSteps] = completedSteps
        };

        var trackRequest = _analyticsGovernanceService.CreateTrackRequest(
            analyticsConfiguration,
            userId.ToString(),
            AnalyticsEvents.TenantOnboarding.StepCompleted,
            rawProperties);

        if (trackRequest is null)
        {
            return;
        }

        await _analyticsProvider.TrackAsync(
            trackRequest.DistinctId,
            trackRequest.EventName,
            trackRequest.Properties.ToDictionary(x => x.Key, x => x.Value),
            cancellationToken);
    }

    private static string SerializeCompletedSteps(string[] completedSteps)
    {
        if (completedSteps.Length == 0)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(completedSteps);
    }

    private static int NormalizeTotalSteps(int totalSteps)
    {
        return totalSteps < 0 ? 0 : totalSteps;
    }

    private static int NormalizeStep(int currentStep, int totalSteps)
    {
        var normalizedTotal = NormalizeTotalSteps(totalSteps);
        if (normalizedTotal == 0)
        {
            return Math.Max(0, currentStep);
        }

        var clamped = Math.Clamp(currentStep, 0, normalizedTotal);
        return clamped;
    }

    private async Task<bool> IsUserAuthorizedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken))
        {
            return true;
        }

        return await _adminContext.IsInstanceAdminAsync(cancellationToken);
    }
}
