using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetTenantReportingIntakePolicyQueryHandler(
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver settingsResolver)
    : IRequestHandler<GetTenantReportingIntakePolicyQuery, TenantReportingIntakePolicyDto>
{
    public async Task<TenantReportingIntakePolicyDto> Handle(
        GetTenantReportingIntakePolicyQuery request,
        CancellationToken cancellationToken)
    {
        new GetTenantReportingIntakePolicyQueryValidator().ValidateAndThrow(request);

        if (request.TenantId != tenantContext.TenantId)
        {
            throw new AuthorizationException(
                ResourceKinds.TenantSetting,
                AuthorizationActions.TenantSettings.View);
        }

        var context = new SettingContext(TenantId: request.TenantId);
        ResolvedSetting intake = await settingsResolver.ResolveWithMetadataAsync(
            GovernanceSettingKeys.EventReporting.IntakeEnabled,
            context,
            cancellationToken)
            ?? throw new InvalidOperationException("The reporting-intake setting could not be resolved.");

        EventSettingGroup eventPolicy = await settingsResolver.ResolveGroupAsync<EventSettingGroup>(
            context,
            cancellationToken);
        ReportingIntakePolicyEvaluation disablement = ReportingIntakePolicyEvaluator.Evaluate(
            new ReportingIntakePolicyState(
                IntakeEnabled: false,
                eventPolicy.RequireApproval,
                eventPolicy.UserSubmissionEnabled,
                eventPolicy.OrganizationSubmissionEnabled,
                eventPolicy.GroupSubmissionEnabled));
        bool isLockedByInstance = intake.IsLocked && intake.Source == SettingSource.SystemLocked;

        return new TenantReportingIntakePolicyDto
        {
            TenantId = request.TenantId,
            Enabled = SettingValueSerializer.Deserialize(intake.Value, true),
            Source = intake.Source,
            IsLockedByInstance = isLockedByInstance,
            CanDisable = !isLockedByInstance && disablement.Allowed,
            ReasonCode = isLockedByInstance
                ? PublicationPolicyMutationFailureCodes.LockedPolicy
                : disablement.ReasonCode,
            Reason = isLockedByInstance
                ? PublicationPolicyMutationMessages.LockedPolicy
                : disablement.Message
        };
    }
}

public sealed class GetTenantReportingIntakePolicyQueryValidator
    : AbstractValidator<GetTenantReportingIntakePolicyQuery>
{
    public GetTenantReportingIntakePolicyQueryValidator()
    {
        RuleFor(request => request.TenantId).NotEmpty();
    }
}
