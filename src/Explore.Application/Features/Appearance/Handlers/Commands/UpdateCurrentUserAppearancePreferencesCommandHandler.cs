namespace Explore.Application.Features.Appearance.Handlers.Commands;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Appearance.Validators;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using MediatR;

public class UpdateCurrentUserAppearancePreferencesCommandHandler : IRequestHandler<UpdateCurrentUserAppearancePreferencesCommand, BaseCommandResponse<Guid>>
{
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly IHierarchicalSettingsResolver _hierarchicalSettingsResolver;
    private readonly IPrivacyErasureStateRepository _privacyErasureStateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCurrentUserAppearancePreferencesCommandHandler(
        IUserPreferenceRepository userPreferenceRepository,
        IHierarchicalSettingsResolver hierarchicalSettingsResolver,
        IPrivacyErasureStateRepository privacyErasureStateRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork)
    {
        _userPreferenceRepository = userPreferenceRepository;
        _hierarchicalSettingsResolver = hierarchicalSettingsResolver;
        _privacyErasureStateRepository = privacyErasureStateRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateCurrentUserAppearancePreferencesCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["User not authenticated."],
                "User not authenticated.");
        }

        if (await IsFencedAsync(userId.Value, cancellationToken))
        {
            return FencedResponse();
        }

        var validator = new UpdateUserAppearancePreferencesDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Preferences, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationResponse = BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Appearance preference update failed.");
            return await MaskIfFencedAsync(userId.Value, validationResponse, cancellationToken);
        }

        var tenantId = _tenantContext.TenantId;

        DateTime utcNow = DateTime.UtcNow;
        var response = await _unitOfWork.ExecuteSerializableAsync(
            async ct => await PersistPreferencesAsync(request, tenantId, userId.Value, utcNow, ct),
            cancellationToken);

        if (response.IsSuccess)
        {
            _hierarchicalSettingsResolver.InvalidateUserCache(tenantId, userId.Value);
        }

        return response;
    }

    private async Task<BaseCommandResponse<Guid>> PersistPreferencesAsync(
        UpdateCurrentUserAppearancePreferencesCommand request,
        Guid tenantId,
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (await IsFencedAsync(userId, cancellationToken))
        {
            return FencedResponse();
        }

        var parentAppearance = await _hierarchicalSettingsResolver.ResolveGroupAsync<AppearanceSettingGroup>(
            new SettingContext(TenantId: tenantId),
            cancellationToken);

        var localization = request.Preferences.Localization!;
        if (localization.Language is not null)
        {
            await UpsertOrRemoveOverrideAsync(
                tenantId,
                userId,
                GovernanceSettingKeys.Appearance.Language,
                localization.Language.Trim().ToLowerInvariant(),
                parentAppearance.Language,
                utcNow);
        }

        if (localization.Direction is not null)
        {
            await UpsertOrRemoveOverrideAsync(
                tenantId,
                userId,
                GovernanceSettingKeys.Appearance.Direction,
                localization.Direction.Trim().ToLowerInvariant(),
                parentAppearance.Direction,
                utcNow);
        }

        return BaseCommandResponse.Success(userId, "Appearance preferences updated successfully.");
    }

    private async Task<bool> IsFencedAsync(Guid userId, CancellationToken cancellationToken) =>
        await _privacyErasureStateRepository.GetBySubjectAsync(userId, cancellationToken) is not null;

    private async Task<BaseCommandResponse<Guid>> MaskIfFencedAsync(
        Guid userId,
        BaseCommandResponse<Guid> response,
        CancellationToken cancellationToken) =>
        await IsFencedAsync(userId, cancellationToken) ? FencedResponse() : response;

    private static BaseCommandResponse<Guid> FencedResponse() =>
        BaseCommandResponse.Validation<Guid>(
            ["Appearance preference update failed."],
            "Appearance preference update failed.");

    private async Task UpsertOrRemoveOverrideAsync(
        Guid tenantId,
        Guid userId,
        string settingKey,
        string normalizedValue,
        string parentValue,
        DateTime utcNow)
    {
        if (string.Equals(parentValue, normalizedValue, StringComparison.Ordinal))
        {
            await _userPreferenceRepository.RemoveOverride(tenantId, userId, settingKey);
            return;
        }

        var serializedValue = SettingValueSerializer.Serialize(normalizedValue);
        var existing = await _userPreferenceRepository.GetByUserAndKey(tenantId, userId, settingKey);

        if (existing is not null)
        {
            existing.Value = serializedValue;
            existing.UpdatedAt = utcNow;
            existing.UpdatedBy = userId;
            await _userPreferenceRepository.Update(existing);
            return;
        }

        await _userPreferenceRepository.Create(new UserPreference
        {
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            SettingKey = settingKey,
            Value = serializedValue,
            CreatedAt = utcNow,
            CreatedBy = userId
        });
    }
}
