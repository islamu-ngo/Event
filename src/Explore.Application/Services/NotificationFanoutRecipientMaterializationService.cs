using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class NotificationFanoutRecipientMaterializationService(
    IUserRepository userRepository,
    IPrivacyErasureStateRepository privacyErasureStateRepository,
    INotificationPreferenceResolver preferenceResolver,
    IFanoutAttendeeLocationAuthorizationService locationAuthorizationService,
    NotificationFanoutRecipientTemplateFactory templateFactory,
    IRecipientNotificationMaterializer materializer) : INotificationFanoutRecipientMaterializationService
{
    public async Task<RecipientNotificationMaterializationResult> MaterializeAsync(
        NotificationFanoutOccurrence occurrence,
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        if (await privacyErasureStateRepository.GetBySubjectAsync(recipientUserId, cancellationToken) is not null)
        {
            return RecipientNotificationMaterializationResult.Skipped();
        }

        NotificationFanoutRecipientTemplate template = templateFactory.Parse(occurrence);
        User? user = await userRepository.GetUserWithDetails(recipientUserId, cancellationToken);
        RecipientEmailAddressResolution emailResolution = RecipientEmailAddressResolver.Resolve(user, recipientUserId);
        bool emailPreferenceEnabled = true;
        if (!template.IsModerationAvailabilityRequired)
        {
            NotificationPreferenceDecision emailPreference = await preferenceResolver.ResolveAsync(
                new NotificationPreferenceResolveRequest(
                    occurrence.TenantId,
                    recipientUserId,
                    OrganizationId: null,
                    GroupId: null,
                    NotificationPreferenceCategoryCodes.EventUpdates,
                    NotificationPreferenceChannelCodes.Email),
                cancellationToken);
            emailPreferenceEnabled = emailPreference.IsEnabled;
        }

        FanoutAttendeeLocationAuthorizationResult? locationAuthorization = null;
        if (template.LocationForDisclosure is { } location)
        {
            locationAuthorization = await locationAuthorizationService.AuthorizeAsync(
                new FanoutAttendeeLocationAuthorizationRequest(
                    occurrence.TenantId,
                    occurrence.EventId,
                    recipientUserId,
                    location.EventLocationId,
                    location.RoomId),
                cancellationToken);
        }

        RecipientNotificationMaterialization request = templateFactory.CreateMaterialization(
            occurrence,
            template,
            recipientUserId,
            emailResolution.Email,
            emailPreferenceEnabled,
            emailResolution.SkipReason,
            locationAuthorization);
        return await materializer.MaterializeAsync(request, cancellationToken);
    }
}
