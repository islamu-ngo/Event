using System.Globalization;
using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public sealed record NotificationFanoutRecipientTemplate(
    Guid OccurrenceId,
    string TemplateKey,
    bool IsCancellation,
    bool IsSessionScoped,
    NotificationFanoutChangeSetV1 ChangeSet,
    NotificationFanoutSnapshotV1 Before,
    NotificationFanoutSnapshotV1 After,
    bool IsModerationAvailabilityRequired = false)
{
    public NotificationFanoutLocationSnapshotV1? LocationForDisclosure =>
        !IsModerationAvailabilityRequired
        && !IsCancellation
        && ChangeSet.Fields.Any(changedField => changedField is NotificationFanoutChangeField.Location or NotificationFanoutChangeField.Room)
            ? After.Location
            : null;
}

public sealed class NotificationFanoutRecipientTemplateFactory
{
    public const int CurrentTemplateVersion = 1;
    public const int CurrentPolicyVersion = 1;
    public const string EventCancelledTemplateKey = "event.cancelled";
    public const string EventUpdatedTemplateKey = "event.updated";
    public const string SessionCancelledTemplateKey = "event.session.cancelled";
    public const string SessionUpdatedTemplateKey = "event.session.updated";
    public const string OccurrenceSourceType = "notification_fanout_occurrence";
    public const string ModerationUnavailableTitle = "Event unavailable";
    public const string ModerationUnavailableBody = "An event you registered for is no longer available.";

    public NotificationFanoutRecipientTemplate Parse(NotificationFanoutOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (occurrence.TemplateVersion != CurrentTemplateVersion)
        {
            throw new JsonException("Fanout template version is unsupported.");
        }

        if (occurrence.PolicyVersion != CurrentPolicyVersion)
        {
            throw new JsonException("Fanout delivery policy is unsupported.");
        }

        if (occurrence.DeliveryPolicyId == (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired)
        {
            return ParseModerationAvailabilityRequired(occurrence);
        }

        if (occurrence.DeliveryPolicyId != (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional)
        {
            throw new JsonException("Fanout delivery policy is unsupported.");
        }

        (bool isCancellation, bool isSessionScoped) = occurrence.TemplateKey switch
        {
            EventCancelledTemplateKey => (true, false),
            EventUpdatedTemplateKey => (false, false),
            SessionCancelledTemplateKey => (true, true),
            SessionUpdatedTemplateKey => (false, true),
            _ => throw new JsonException("Fanout template key is unsupported.")
        };
        if (isSessionScoped != occurrence.SessionId.HasValue)
        {
            throw new JsonException("Fanout template scope does not match its occurrence.");
        }

        NotificationFanoutChangeSetV1 changeSet = NotificationFanoutTemplateJson.Canonicalize(
            JsonSerializer.Deserialize(
                    occurrence.ChangeSetJson,
                    NotificationFanoutTemplateJsonContext.Default.NotificationFanoutChangeSetV1)
                ?? throw new JsonException("Fanout change set is required."));
        NotificationFanoutSnapshotV1 before = NotificationFanoutTemplateJson.Canonicalize(
            JsonSerializer.Deserialize(
                    occurrence.SafeBeforeSnapshotJson,
                    NotificationFanoutTemplateJsonContext.Default.NotificationFanoutSnapshotV1)
                ?? throw new JsonException("Fanout before snapshot is required."));
        NotificationFanoutSnapshotV1 after = NotificationFanoutTemplateJson.Canonicalize(
            JsonSerializer.Deserialize(
                    occurrence.SafeAfterSnapshotJson,
                    NotificationFanoutTemplateJsonContext.Default.NotificationFanoutSnapshotV1)
                ?? throw new JsonException("Fanout after snapshot is required."));

        ValidateChangeSet(changeSet, isCancellation);
        ValidateSnapshot(before, isSessionScoped);
        ValidateSnapshot(after, isSessionScoped);
        ValidateSessionDisplayTimes(changeSet, isCancellation, isSessionScoped, before, after);
        return new(
            occurrence.Id,
            occurrence.TemplateKey,
            isCancellation,
            isSessionScoped,
            changeSet,
            before,
            after);
    }

    public RecipientNotificationMaterialization CreateMaterialization(
        NotificationFanoutOccurrence occurrence,
        NotificationFanoutRecipientTemplate template,
        Guid recipientUserId,
        string? verifiedEmail,
        bool emailPreferenceEnabled,
        string? emailSkipReason,
        FanoutAttendeeLocationAuthorizationResult? locationAuthorization)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(template);
        RetryStableRecipientGraphMetadata graphMetadata = CreateRetryStableRecipientGraphMetadata();
        if (template.IsModerationAvailabilityRequired)
        {
            return CreateModerationAvailabilityRequiredMaterialization(
                occurrence,
                template,
                recipientUserId,
                verifiedEmail,
                emailSkipReason,
                locationAuthorization,
                graphMetadata);
        }

        if (occurrence.Id != template.OccurrenceId
            || !string.Equals(occurrence.TemplateKey, template.TemplateKey, StringComparison.Ordinal)
            || occurrence.TemplateVersion != CurrentTemplateVersion
            || occurrence.PolicyVersion != CurrentPolicyVersion
            || occurrence.DeliveryPolicyId != (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional
            || occurrence.SessionId.HasValue != template.IsSessionScoped
            || recipientUserId == Guid.Empty
            || !MatchesLocationAuthorization(
                occurrence,
                recipientUserId,
                template.LocationForDisclosure,
                locationAuthorization))
        {
            throw new InvalidOperationException("Fanout recipient materialization authority does not match the parsed occurrence.");
        }

        string title = ScopeTitle(template.IsCancellation ? template.Before : template.After, template.IsSessionScoped);
        string notificationTitle = template.IsCancellation
            ? $"{ScopeName(template)} cancelled"
            : $"{ScopeName(template)} updated";
        string body = template.IsCancellation
            ? $"{title} has been cancelled."
            : BuildUpdateBody(template, locationAuthorization);
        string deduplicationKey = $"notification-fanout-occurrence:{occurrence.Id:N}:recipient:{recipientUserId:N}";
        bool hasEmail = emailPreferenceEnabled && !string.IsNullOrWhiteSpace(verifiedEmail);
        EmailDispatchOutbox? email = hasEmail
            ? CreateEmail(occurrence, template, recipientUserId, verifiedEmail!, notificationTitle, title, body)
            : null;

        return new RecipientNotificationMaterialization(
            graphMetadata.IntentId,
            new NotificationIntentDraft(
                NotificationCategory.EventLifecycle,
                TenantId: occurrence.TenantId,
                RecipientKind: nameof(NotificationRecipientKindEnum.User),
                TemplateKey: occurrence.TemplateKey,
                SafePayloadReference: $"notification-fanout-occurrence:{occurrence.Id:D}",
                DeduplicationKey: deduplicationKey,
                CorrelationId: occurrence.Id.ToString("D"),
                UserId: recipientUserId,
                EventId: occurrence.EventId,
                FanoutOccurrenceId: occurrence.Id),
            NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            "critical_event_update",
            new RecipientInAppNotificationDraft(
                template.IsCancellation
                    ? (int)NotificationTypeEnum.EventCancelled
                    : (int)NotificationTypeEnum.EventUpdated,
                notificationTitle,
                body,
                (int)ActorTypeEnum.User,
                (int)NotificationReasonEnum.System,
                template.IsSessionScoped
                    ? (int)NotificationEntityTypeEnum.EventSession
                    : (int)NotificationEntityTypeEnum.Event,
                (template.IsSessionScoped ? occurrence.SessionId!.Value : occurrence.EventId).ToString("D")),
            email,
            IncludeEmailChannel: true,
            EmailRequired: false,
            EmailSkipReason: email is null
                ? emailPreferenceEnabled
                    ? emailSkipReason ?? "recipient_email_missing_or_unverified"
                    : "email_preference_disabled"
                : null,
            PreferenceCategoryCode: NotificationPreferenceCategoryCodes.EventUpdates,
            EmailPreferenceEnabled: emailPreferenceEnabled,
            PolicyVersion: occurrence.PolicyVersion,
            TemplateVersion: occurrence.TemplateVersion,
            LinkAllowed: false,
            InAppNotificationId: graphMetadata.InAppNotificationId,
            InAppDeliveryId: graphMetadata.InAppDeliveryId,
            EmailDeliveryId: graphMetadata.EmailDeliveryId,
            MaterializedAt: graphMetadata.MaterializedAt);
    }

    private static NotificationFanoutRecipientTemplate ParseModerationAvailabilityRequired(
        NotificationFanoutOccurrence occurrence)
    {
        if (!string.Equals(
                occurrence.TemplateKey,
                NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey,
                StringComparison.Ordinal)
            || occurrence.SessionId.HasValue
            || !IsEmptyJsonObject(occurrence.ChangeSetJson)
            || !IsEmptyJsonObject(occurrence.SafeBeforeSnapshotJson)
            || !IsEmptyJsonObject(occurrence.SafeAfterSnapshotJson))
        {
            throw new JsonException("Required moderation fanout payload is unsupported.");
        }

        var emptySnapshot = new NotificationFanoutSnapshotV1(
            string.Empty,
            SessionTitle: null,
            StartsAt: null,
            EndsAt: null,
            Timezone: null,
            Location: null);
        return new NotificationFanoutRecipientTemplate(
            occurrence.Id,
            occurrence.TemplateKey,
            IsCancellation: false,
            IsSessionScoped: false,
            new NotificationFanoutChangeSetV1([]),
            emptySnapshot,
            emptySnapshot,
            IsModerationAvailabilityRequired: true);
    }

    private static RecipientNotificationMaterialization CreateModerationAvailabilityRequiredMaterialization(
        NotificationFanoutOccurrence occurrence,
        NotificationFanoutRecipientTemplate template,
        Guid recipientUserId,
        string? verifiedEmail,
        string? emailSkipReason,
        FanoutAttendeeLocationAuthorizationResult? locationAuthorization,
        RetryStableRecipientGraphMetadata graphMetadata)
    {
        if (occurrence.Id != template.OccurrenceId
            || !string.Equals(occurrence.TemplateKey, template.TemplateKey, StringComparison.Ordinal)
            || !string.Equals(
                occurrence.TemplateKey,
                NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey,
                StringComparison.Ordinal)
            || occurrence.TemplateVersion != CurrentTemplateVersion
            || occurrence.PolicyVersion != CurrentPolicyVersion
            || occurrence.DeliveryPolicyId != (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired
            || occurrence.SessionId.HasValue
            || recipientUserId == Guid.Empty
            || locationAuthorization is not null)
        {
            throw new InvalidOperationException("Required moderation materialization authority does not match the parsed occurrence.");
        }

        string deduplicationKey = $"notification-fanout-occurrence:{occurrence.Id:N}:recipient:{recipientUserId:N}";
        EmailDispatchOutbox? email = string.IsNullOrWhiteSpace(verifiedEmail)
            ? null
            : new EmailDispatchOutbox
            {
                Id = Guid.CreateVersion7(),
                TenantId = occurrence.TenantId,
                Kind = EmailDispatchKind.ModerationAvailabilityRequired,
                SourceType = OccurrenceSourceType,
                SourceId = occurrence.Id,
                EventId = occurrence.EventId,
                RecipientUserId = recipientUserId,
                RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
                RecipientEmail = verifiedEmail.Trim(),
                Subject = ModerationUnavailableTitle,
                PlainTextBody = $"Assalamu alaykum,\n\n{ModerationUnavailableBody}\n\nEvent Platform",
                HtmlBody = $"<p>Assalamu alaykum,</p><p>{ModerationUnavailableBody}</p><p>Event Platform</p>",
                CorrelationId = occurrence.Id.ToString("D"),
                CreatedAt = occurrence.OccurredAt
            };

        return new RecipientNotificationMaterialization(
            graphMetadata.IntentId,
            new NotificationIntentDraft(
                NotificationCategory.TrustSafetyModeration,
                TenantId: occurrence.TenantId,
                RecipientKind: nameof(NotificationRecipientKindEnum.User),
                TemplateKey: occurrence.TemplateKey,
                SafePayloadReference: $"notification-fanout-occurrence:{occurrence.Id:D}",
                DeduplicationKey: deduplicationKey,
                CorrelationId: occurrence.Id.ToString("D"),
                UserId: recipientUserId,
                EventId: occurrence.EventId,
                FanoutOccurrenceId: occurrence.Id),
            NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
            "moderation_availability_required",
            new RecipientInAppNotificationDraft(
                (int)NotificationTypeEnum.General,
                ModerationUnavailableTitle,
                ModerationUnavailableBody,
                (int)ActorTypeEnum.User,
                (int)NotificationReasonEnum.System,
                IsRequired: true),
            email,
            IncludeEmailChannel: true,
            EmailRequired: true,
            EmailSkipReason: email is null
                ? emailSkipReason ?? "recipient_email_missing_or_unverified"
                : null,
            PolicyVersion: occurrence.PolicyVersion,
            TemplateVersion: occurrence.TemplateVersion,
            LinkAllowed: false,
            InAppNotificationId: graphMetadata.InAppNotificationId,
            InAppDeliveryId: graphMetadata.InAppDeliveryId,
            EmailDeliveryId: graphMetadata.EmailDeliveryId,
            MaterializedAt: graphMetadata.MaterializedAt);
    }

    private static RetryStableRecipientGraphMetadata CreateRetryStableRecipientGraphMetadata() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        DateTime.UtcNow);

    private static bool IsEmptyJsonObject(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && !document.RootElement.EnumerateObject().Any();
    }

    private static EmailDispatchOutbox CreateEmail(
        NotificationFanoutOccurrence occurrence,
        NotificationFanoutRecipientTemplate template,
        Guid recipientUserId,
        string verifiedEmail,
        string notificationTitle,
        string title,
        string body)
    {
        string subject = $"{notificationTitle}: {title}";
        string plainTextBody = $"Assalamu alaykum,\n\n{body}\n\nEvent Platform";
        string encodedBody = WebUtility.HtmlEncode(body)
            .Replace("\r\n", "<br />", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal)
            .Replace("\r", "<br />", StringComparison.Ordinal);
        return new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = occurrence.TenantId,
            Kind = template.IsCancellation ? EmailDispatchKind.EventCancelled : EmailDispatchKind.EventUpdated,
            SourceType = OccurrenceSourceType,
            SourceId = occurrence.Id,
            EventId = occurrence.EventId,
            RecipientUserId = recipientUserId,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = verifiedEmail.Trim(),
            Subject = subject,
            PlainTextBody = plainTextBody,
            HtmlBody = $"<p>Assalamu alaykum,</p><p>{encodedBody}</p><p>Event Platform</p>",
            CorrelationId = occurrence.Id.ToString("D"),
            CreatedAt = occurrence.OccurredAt
        };
    }

    private static string BuildUpdateBody(
        NotificationFanoutRecipientTemplate template,
        FanoutAttendeeLocationAuthorizationResult? locationAuthorization)
    {
        var changes = new List<string>();
        foreach (NotificationFanoutChangeField field in template.ChangeSet.Fields)
        {
            switch (field)
            {
                case NotificationFanoutChangeField.StartTime:
                    changes.Add($"Start time: {FormatTime(template.Before.StartsAt, template.Before.Timezone)} → {FormatTime(template.After.StartsAt, template.After.Timezone)}.");
                    break;
                case NotificationFanoutChangeField.EndTime:
                    changes.Add($"End time: {FormatTime(template.Before.EndsAt, template.Before.Timezone)} → {FormatTime(template.After.EndsAt, template.After.Timezone)}.");
                    break;
                case NotificationFanoutChangeField.Timezone:
                    changes.Add($"Timezone: {Text(template.Before.Timezone)} → {Text(template.After.Timezone)}.");
                    AddChangedSessionDisplayTimes(changes, template);
                    break;
                case NotificationFanoutChangeField.Location:
                case NotificationFanoutChangeField.Room:
                    if (!changes.Any(value => value.StartsWith("Location:", StringComparison.Ordinal)))
                    {
                        string location = SelectImmutableLocation(template.After.Location, locationAuthorization);
                        changes.Add(string.IsNullOrEmpty(location)
                            ? "Location: details changed; exact information is not available in this notification."
                            : $"Location: {location}.");
                    }
                    break;
                case NotificationFanoutChangeField.Cancelled:
                default:
                    throw new InvalidOperationException("An unsupported update field reached template rendering.");
            }
        }

        return $"Important information for {ScopeTitle(template.After, template.IsSessionScoped)} changed. {string.Join(' ', changes)}";
    }

    private static bool MatchesLocationAuthorization(
        NotificationFanoutOccurrence occurrence,
        Guid recipientUserId,
        NotificationFanoutLocationSnapshotV1? snapshot,
        FanoutAttendeeLocationAuthorizationResult? authorization) =>
        authorization is null
        || snapshot is not null
            && authorization.TenantId == occurrence.TenantId
            && authorization.EventId == occurrence.EventId
            && authorization.RecipientUserId == recipientUserId
            && authorization.EventLocationId == snapshot.EventLocationId
            && authorization.RoomId == snapshot.RoomId;

    private static string SelectImmutableLocation(
        NotificationFanoutLocationSnapshotV1? snapshot,
        FanoutAttendeeLocationAuthorizationResult? authorization)
    {
        if (snapshot is null
            || authorization is null
            || authorization.State != EventLocationDisclosureState.Available
            || authorization.EventLocationId != snapshot.EventLocationId)
        {
            return string.Empty;
        }

        var values = new List<string>();
        AddAllowed(values, snapshot.VenueName, EventLocationDisclosureField.VenueName, authorization);
        AddAllowed(values, snapshot.RoomName, EventLocationDisclosureField.RoomName, authorization);
        AddAllowed(values, snapshot.StreetAddress, EventLocationDisclosureField.StreetAddress, authorization);
        AddAllowed(values, snapshot.Postcode, EventLocationDisclosureField.Postcode, authorization);
        AddAllowed(values, snapshot.City, EventLocationDisclosureField.City, authorization);
        AddAllowed(values, snapshot.Country, EventLocationDisclosureField.Country, authorization);
        return string.Join(", ", values.Distinct(StringComparer.Ordinal));
    }

    private static void AddAllowed(
        List<string> values,
        string? snapshotValue,
        EventLocationDisclosureField field,
        FanoutAttendeeLocationAuthorizationResult authorization)
    {
        if (authorization.AllowedFields.Contains(field) && !string.IsNullOrWhiteSpace(snapshotValue))
        {
            values.Add(snapshotValue.Trim());
        }
    }

    private static void ValidateChangeSet(NotificationFanoutChangeSetV1 changeSet, bool isCancellation)
    {
        if (changeSet.Fields.Length == 0
            || changeSet.Fields.Any(field => !Enum.IsDefined(field))
            || changeSet.Fields.Distinct().Count() != changeSet.Fields.Length
            || isCancellation != (changeSet.Fields.Length == 1
                && changeSet.Fields[0] == NotificationFanoutChangeField.Cancelled))
        {
            throw new JsonException("Fanout change set does not match its template.");
        }
    }

    private static void ValidateSnapshot(NotificationFanoutSnapshotV1 snapshot, bool isSessionScoped)
    {
        if (string.IsNullOrWhiteSpace(snapshot.EventTitle)
            || isSessionScoped && string.IsNullOrWhiteSpace(snapshot.SessionTitle)
            || !isSessionScoped && snapshot.SessionTitle is not null
            || isSessionScoped && snapshot.SessionDisplayTimes is not null
            || snapshot.Location is { EventLocationId: var eventLocationId } && eventLocationId == Guid.Empty
            || snapshot.Location?.RoomId == Guid.Empty)
        {
            throw new JsonException("Fanout snapshot does not match its template scope.");
        }
    }

    private static void ValidateSessionDisplayTimes(
        NotificationFanoutChangeSetV1 changeSet,
        bool isCancellation,
        bool isSessionScoped,
        NotificationFanoutSnapshotV1 before,
        NotificationFanoutSnapshotV1 after)
    {
        IReadOnlyList<NotificationFanoutSessionDisplayTimeV1>? beforeSessions = before.SessionDisplayTimes;
        IReadOnlyList<NotificationFanoutSessionDisplayTimeV1>? afterSessions = after.SessionDisplayTimes;
        if (beforeSessions is null && afterSessions is null)
        {
            return;
        }

        if (isCancellation
            || isSessionScoped
            || !changeSet.Fields.Contains(NotificationFanoutChangeField.Timezone))
        {
            throw new JsonException("Fanout affected-session snapshots do not match an event timezone update.");
        }

        if (beforeSessions is null || afterSessions is null)
        {
            throw new JsonException("Fanout session display-time snapshots must be absent on both sides or complete on both sides.");
        }

        if (string.IsNullOrWhiteSpace(before.Timezone)
            || string.IsNullOrWhiteSpace(after.Timezone))
        {
            throw new JsonException("Fanout enriched timezone snapshots require both timezone identifiers.");
        }

        ValidateSessionDisplayTimeSet(beforeSessions);
        ValidateSessionDisplayTimeSet(afterSessions);
        if (beforeSessions.Count != afterSessions.Count)
        {
            throw new JsonException("Fanout session display-time snapshots have different session sets.");
        }

        bool anyDisplayTimeChanged = false;
        for (var index = 0; index < beforeSessions.Count; index++)
        {
            NotificationFanoutSessionDisplayTimeV1 prior = beforeSessions[index];
            NotificationFanoutSessionDisplayTimeV1 current = afterSessions[index];
            if (prior.SessionId == Guid.Empty
                || prior.SessionId != current.SessionId
                || prior.StartsAt.ToUniversalTime() != current.StartsAt.ToUniversalTime()
                || prior.EndsAt?.ToUniversalTime() != current.EndsAt?.ToUniversalTime())
            {
                throw new JsonException("Fanout affected-session display times are invalid or mutable.");
            }

            anyDisplayTimeChanged |= !prior.StartsAt.EqualsExact(current.StartsAt)
                || !ExactEquals(prior.EndsAt, current.EndsAt);
        }

        if (!anyDisplayTimeChanged)
        {
            throw new JsonException("Fanout session display-time snapshots contain no attendee-visible change.");
        }
    }

    private static void ValidateSessionDisplayTimeSet(IReadOnlyList<NotificationFanoutSessionDisplayTimeV1>? sessions)
    {
        if (sessions is null)
        {
            return;
        }

        if (sessions.Count == 0
            || sessions.Any(session => session is null
                || session.SessionId == Guid.Empty
                || string.IsNullOrWhiteSpace(session.SessionTitle)
                || session.StartsAt == default
                || session.EndsAt is { } endsAt
                    && (endsAt == default || endsAt <= session.StartsAt))
            || sessions.Select(session => session.SessionId).Distinct().Count() != sessions.Count)
        {
            throw new JsonException("Fanout session display-time snapshot is empty, duplicated, or incomplete.");
        }
    }

    private static void AddChangedSessionDisplayTimes(
        List<string> changes,
        NotificationFanoutRecipientTemplate template)
    {
        if (template.Before.SessionDisplayTimes is not { Count: > 0 } beforeSessions
            || template.After.SessionDisplayTimes is not { Count: > 0 } afterSessions)
        {
            return;
        }

        for (var index = 0; index < beforeSessions.Count; index++)
        {
            NotificationFanoutSessionDisplayTimeV1 prior = beforeSessions[index];
            NotificationFanoutSessionDisplayTimeV1 current = afterSessions[index];
            if (prior.StartsAt.EqualsExact(current.StartsAt)
                && ExactEquals(prior.EndsAt, current.EndsAt))
            {
                continue;
            }

            string sessionTitle = Text(current.SessionTitle ?? prior.SessionTitle ?? "Session");
            changes.Add(
                $"{sessionTitle}: {FormatInterval(prior, template.Before.Timezone)} → {FormatInterval(current, template.After.Timezone)}.");
        }
    }

    private static string FormatInterval(NotificationFanoutSessionDisplayTimeV1 session, string? timezone)
    {
        string start = FormatTime(session.StartsAt, timezone);
        return session.EndsAt is null
            ? start
            : $"{start} to {FormatTime(session.EndsAt, timezone)}";
    }

    private static bool ExactEquals(DateTimeOffset? left, DateTimeOffset? right) =>
        left.HasValue == right.HasValue
        && (!left.HasValue || left.Value.EqualsExact(right!.Value));

    private static string ScopeName(NotificationFanoutRecipientTemplate template) =>
        template.IsSessionScoped ? "Session" : "Event";

    private static string ScopeTitle(NotificationFanoutSnapshotV1 snapshot, bool sessionScoped) =>
        (sessionScoped ? snapshot.SessionTitle : snapshot.EventTitle)!.Trim();

    private static string FormatTime(DateTimeOffset? value, string? timezone) => value is null
        ? "not scheduled"
        : $"{value.Value.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(timezone) ? string.Empty : $" ({timezone.Trim()})")}";

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "not specified" : value.Trim();

    private sealed record RetryStableRecipientGraphMetadata(
        Guid IntentId,
        Guid InAppNotificationId,
        Guid InAppDeliveryId,
        Guid EmailDeliveryId,
        DateTime MaterializedAt);
}
