using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.Announcements.Requests.Commands;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanSettingsMutationPreparation
{
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateInstanceAnnouncementCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareTypedAsync(new(command), cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateTenantAnnouncementCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareTypedAsync(new(command), cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateManagedTenantAnnouncementCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareTypedAsync(new(command), cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(DismissAnnouncementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Observed);
        return PrepareCoreAsync(command, SettingScope.User, command.Observed.TenantId, Authority.Subject,
            command.ActorUserId, (tenant, actor) =>
            [
                new(HumanSettingsOperation.UserSettings, SettingScope.User, tenant, actor,
                    GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition,
                    HumanSettingsMutationKind.SetValue, AnnouncementSettingValueCodec.SerializeMarker(new(command.Observed)))
            ],
            [GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication,
                GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition],
            cancellationToken);
    }
}
