using System.Collections.Immutable;
using Explore.Application.DTOs.Localization.Validators;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Domain.Constants;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Handlers;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Application.Features.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.Announcements.Requests.Commands;
using Explore.Application.DTOs.Appearance.Validators;
using Explore.Application.Features.Appearance.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Full original ordinary command inputs, not an authorization capability. The
/// concrete stores derive disjoint dedicated/unmoved executable partitions without
/// reconstructing this envelope. Mixed, import and nonhuman producers remain separate.
/// </summary>
public sealed class OrdinarySettingsMutation
{
    private OrdinarySettingsMutation(object original, ImmutableArray<HumanSettingsMutation> effects)
    {
        Original = original;
        Effects = effects;
    }

    public object Original { get; }
    public ImmutableArray<HumanSettingsMutation> Effects { get; }
    public ImmutableArray<string> ReadKeys => Original is DismissAnnouncementCommand
        ? [GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication,
            GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition] : [];
    public bool RequiresTenantVerificationCeiling => Original is UpdateSettingCommand or UpdateSettingBatchCommand
        && Effects.Any(effect => effect.Scope == SettingScope.Tenant
            && effect.Key == Explore.Domain.Constants.GovernanceSettingKeys.Organizations.VerificationRequired);

    public static OrdinarySettingsMutation? Capture(object original, ImmutableArray<HumanSettingsMutation> effects)
    {
        if (original is DismissAnnouncementCommand dismissal)
        {
            ArgumentNullException.ThrowIfNull(dismissal.Observed);
            if (dismissal.ActorUserId == Guid.Empty || effects.Length != 1
                || effects[0] != new HumanSettingsMutation(HumanSettingsOperation.UserSettings, SettingScope.User,
                    dismissal.Observed.TenantId, dismissal.ActorUserId,
                    GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition,
                    HumanSettingsMutationKind.SetValue,
                    AnnouncementSettingValueCodec.SerializeMarker(new(dismissal.Observed))))
                throw new UnauthorizedAccessException("Dismissal effects require the exact original subject, tenant and observation.");
            if (dismissal.ExpectedMarker is { } marker && marker.Observation.TenantId != dismissal.Observed.TenantId)
                throw new ArgumentException("The expected dismissal marker belongs to another tenant.", nameof(original));
            return new(original, effects);
        }
        if (effects.Any(effect => AnnouncementSettingMutationGuard.Handles(effect.Key)))
            throw new UnauthorizedAccessException(AnnouncementSettingMutationGuard.FailureCode);
        if (original is UpdateCurrentUserAppearancePreferencesCommand appearance)
        {
            var validator = new UpdateUserAppearancePreferencesDtoValidator();
            if (!validator.Validate(appearance.Preferences).IsValid)
                throw new UnauthorizedAccessException("Appearance effects require valid original localization inputs.");
            var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            if (appearance.Preferences.Localization!.Language is { } language)
                inputs.Add(GovernanceSettingKeys.Appearance.Language, language.Trim().ToLowerInvariant());
            if (appearance.Preferences.Localization.Direction is { } direction)
                inputs.Add(GovernanceSettingKeys.Appearance.Direction, direction.Trim().ToLowerInvariant());
            if (effects.Length != inputs.Count
                || effects.Select(effect => effect.Key).Distinct(StringComparer.Ordinal).Count() != effects.Length)
                throw new UnauthorizedAccessException("Appearance effects must match the supplied localization keys.");
            foreach (var effect in effects)
            {
                if (!inputs.TryGetValue(effect.Key, out var input)
                    || effect.Operation != HumanSettingsOperation.UserSettings || effect.Scope != SettingScope.User
                    || effect.TenantId.GetValueOrDefault() == Guid.Empty || effect.UserId.GetValueOrDefault() == Guid.Empty
                    || effect.IsLocked.HasValue
                    || effect.Kind == HumanSettingsMutationKind.SetValue
                        && effect.Value != SettingValueSerializer.Serialize(input)
                    || effect.Kind == HumanSettingsMutationKind.Remove && effect.Value is not null
                    || effect.Kind is not (HumanSettingsMutationKind.SetValue or HumanSettingsMutationKind.Remove))
                    throw new UnauthorizedAccessException("Appearance effects differ from the original localization intent.");
            }
            return new(original, effects);
        }
        if (original is UpdateLocalizationGovernanceCommand localization)
        {
            if (!effects.SequenceEqual(CaptureLocalization(localization)))
                throw new UnauthorizedAccessException("Localization effects must match the original governance command.");
            return new(original, effects);
        }
        if (original is not (UpdateSettingCommand or UpdateSettingBatchCommand or ResetSettingCommand
            or LockSettingCommand or UnlockSettingCommand or SetInstanceTenantSettingCommand
            or LockInstanceTenantSettingCommand or UnlockInstanceTenantSettingCommand))
            return null;
        if (original is UpdateSettingBatchCommand batch
            && (batch.Values.Keys.Any(EmailDeliverySettingKeys.Contains)
                || MixedSettingsBatchMutation.RequiresCombinedSource(batch)))
            throw new UnauthorizedAccessException("Email and mixed batches require their original dedicated producer.");
        if (effects.Any(effect => IsSpecialized(effect.Key)))
        {
            if (effects.Any(effect => !IsSpecialized(effect.Key)))
                throw new NotSupportedException("ordinary_specialized_batch_requires_original_combined_source");
            return null;
        }

        foreach (var effect in effects)
        {
            var definition = SettingRegistry.Get(effect.Key)
                ?? throw new UnauthorizedAccessException("An ordinary effect requires its original registry definition.");
            if (original is LockSettingCommand or LockInstanceTenantSettingCommand or UnlockInstanceTenantSettingCommand
                && !definition.IsLockable)
                throw new UnauthorizedAccessException("The original command requires a lockable setting.");
            if (effect.Kind != HumanSettingsMutationKind.SetLock
                && (effect.Scope < definition.MinScope || effect.Scope > definition.MaxScope))
                throw new UnauthorizedAccessException("An ordinary effect must retain its original registry scope.");
            string? input = original switch
            {
                UpdateSettingCommand update when update.Key == effect.Key && update.Scope == effect.Scope => update.Value,
                UpdateSettingBatchCommand update when update.Scope == effect.Scope
                    && definition.Category == update.Category
                    && update.Values.TryGetValue(effect.Key, out var value) => value,
                SetInstanceTenantSettingCommand update when update.Key == effect.Key
                    && update.TenantId == effect.TenantId => update.Value,
                _ => null
            };
            if (effect.Kind == HumanSettingsMutationKind.SetValue)
            {
                if (input is null)
                    throw new UnauthorizedAccessException("An ordinary value must belong to its original typed command.");
                var validated = SettingCommandHelper.ValidateAndSerialize(input, definition);
                if (!validated.IsValid || validated.SerializedValue != effect.Value)
                    throw new UnauthorizedAccessException("The serialized effect differs from the original validated input.");
            }
        }
        return new(original, effects);
    }

    public static ImmutableArray<HumanSettingsMutation> CaptureLocalization(UpdateLocalizationGovernanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validator = new UpdateLocalizationGovernanceDtoValidator();
        if (!validator.Validate(command.Dto).IsValid)
            throw new UnauthorizedAccessException("Localization effects require valid original governance inputs.");
        var effects = ImmutableArray.CreateBuilder<HumanSettingsMutation>();
        if (command.Dto.Tms is { } tms)
        {
            Add(GovernanceSettingKeys.Localization.TmsProvider, SettingValueSerializer.Serialize(tms.Provider.Trim().ToLowerInvariant()));
            Add(GovernanceSettingKeys.Localization.TmsApiUrl, SettingValueSerializer.Serialize(tms.ApiUrl ?? string.Empty));
            Add(GovernanceSettingKeys.Localization.TmsProjectId, SettingValueSerializer.Serialize(tms.ProjectId ?? string.Empty));
            Add(GovernanceSettingKeys.Localization.TmsComponent, SettingValueSerializer.Serialize(tms.Component ?? string.Empty));
        }
        if (command.Dto.Languages is { } languages)
        {
            Add(GovernanceSettingKeys.Localization.DefaultLanguage, SettingValueSerializer.Serialize(languages.DefaultLanguage.Trim().ToLowerInvariant()));
            Add(GovernanceSettingKeys.Localization.EnabledLanguages, SettingValueSerializer.Serialize(
                string.Join(",", languages.EnabledLanguages.Select(code => code.Trim().ToLowerInvariant()))));
            Add(GovernanceSettingKeys.Localization.FallbackLanguage, SettingValueSerializer.Serialize(languages.FallbackLanguage.Trim().ToLowerInvariant()));
        }
        if (command.Dto.Runtime is { } runtime)
        {
            Add(GovernanceSettingKeys.Localization.ClientPickerEnabled, runtime.ClientPickerEnabled ? "true" : "false");
            Add(GovernanceSettingKeys.Localization.ForceOfflineMode, runtime.ForceOfflineMode ? "true" : "false");
        }
        return effects.ToImmutable();

        void Add(string key, string value) => effects.Add(new(
            HumanSettingsOperation.InstanceSettings, SettingScope.Instance, null, null,
            key, HumanSettingsMutationKind.SetValue, value));
    }

    private static bool IsSpecialized(string key) => EmailDeliverySettingKeys.Contains(key)
        || key.Trim().Equals(EventResourceProviderBindingDocument.SettingKey, StringComparison.OrdinalIgnoreCase)
        || EventResourceSettingMutationGuard.Handles(key) || VisitorAccessSettingMutationGuard.Handles(key)
        || PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal)
        || LocationPrivacySettingDefinitions.All.Any(definition => definition.Key == key);
}

/// <summary>Opaque original data observation. Only the selected store can issue or consume it.</summary>
public interface IOrdinarySettingsSource;

public enum OrdinaryUserPreferenceWrite { Create, Update, Remove }

/// <summary>
/// Observes before BEGIN, acquires on the original owner, and checks privately completed
/// effects before human deadlines. This store does not decide human administrator authority.
/// </summary>
public interface IOrdinarySettingsMutationStore
{
    Task<AnnouncementDismissalResult> ApplyAnnouncementDismissalAsync(
        IOrdinarySettingsSource source, DismissAnnouncementCommand original, CancellationToken cancellationToken);
    Task<IOrdinarySettingsSource> ObserveAsync(OrdinarySettingsMutation mutation, HumanCommitSubject subject,
        CancellationToken cancellationToken);
    Task AcquireAsync(IOrdinarySettingsSource source, CancellationToken cancellationToken);
    Task RequireCompleteAsync(IOrdinarySettingsSource source, CancellationToken cancellationToken);
    Task VerifyAsync(IOrdinarySettingsSource source, CancellationToken cancellationToken);
    void Expire(IOrdinarySettingsSource source);
    Task<string?> WriteSystemAsync(IOrdinarySettingsSource source, SystemSetting candidate,
        HumanSettingsMutationKind kind, CancellationToken cancellationToken);
    Task<bool> WriteTenantAsync(IOrdinarySettingsSource source, Guid tenantId, string key,
        HumanSettingsMutationKind kind, string? value, bool? isLocked, CancellationToken cancellationToken);
    Task<UserPreference?> WriteUserAsync(IOrdinarySettingsSource source, Guid tenantId, Guid userId, string key,
        OrdinaryUserPreferenceWrite operation, string? value, CancellationToken cancellationToken);
}
