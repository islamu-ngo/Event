using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Exceptions;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using static Explore.API.Mcp.EventMcpBounds;
using EventSessionTemplateDiffDto = Explore.Application.DTOs.EventSessionTemplateSync.TemplateDiffDto;
using EventSessionTemplateSyncHistoryItemDto = Explore.Application.DTOs.EventSessionTemplateSync.EventSessionTemplateSyncHistoryItemDto;
using EventTemplateDiffDto = Explore.Application.DTOs.EventTemplateSync.TemplateDiffDto;
using EventTemplateSyncHistoryItemDto = Explore.Application.DTOs.EventTemplateSync.EventTemplateSyncHistoryItemDto;

namespace Explore.API.Mcp;

/// <summary>
/// The projection half of the Event MCP surface. These functions decide what an assistant sees of each
/// entity and where each collection is cut off; they are deliberately pure so that the shape of an MCP
/// response can be reasoned about — and tested — without a request, a tenant, or a database.
/// <para>
/// Authorization, HAL gating, and location disclosure stay in the tool class and
/// <see cref="EventMcpLocationDisclosureGuard"/>: nothing here may decide whether a caller is allowed to see
/// something, only how an already-authorized value is rendered and truncated.
/// </para>
/// </summary>
internal static class EventMcpDescriptorMappers
{
    internal static EventMcpCreationContextDescriptor MapCreationContext(EventCreationContextDto dto)
    {
        var truncatedFields = new List<string>();
        var publisherOptions = dto.PublisherOptions
            .Take(MaxCreationPublisherOptions)
            .Select(option => MapCreationPublisherOption(option, truncatedFields))
            .ToArray();

        if (dto.PublisherOptions.Count > MaxCreationPublisherOptions)
        {
            truncatedFields.Add("PublisherOptions");
        }

        return new EventMcpCreationContextDescriptor(
            dto.CanCreate,
            dto.AllowPersonalPublishing,
            dto.AllowOrganizationPublishing,
            dto.AllowGroupPublishing,
            dto.RequiresApproval,
            TrimToNull(dto.DefaultPublisherMode, MaxShortTextLength, truncatedFields, nameof(dto.DefaultPublisherMode)),
            TrimToNull(dto.UnavailableReason, MaxShortTextLength, truncatedFields, nameof(dto.UnavailableReason)),
            dto.PublisherOptions.Count,
            publisherOptions.Length,
            dto.PublisherOptions.Count > MaxCreationPublisherOptions,
            publisherOptions,
            truncatedFields);
    }

    internal static EventMcpCreationPublisherOptionDescriptor MapCreationPublisherOption(
        EventCreationPublisherOptionDto dto,
        ICollection<string> truncatedFields)
        => new(
            TrimToEmpty(dto.PublisherMode, MaxShortTextLength, truncatedFields, nameof(dto.PublisherMode)),
            dto.PublisherId,
            TrimToEmpty(dto.DisplayName, MaxShortTextLength, truncatedFields, nameof(dto.DisplayName)),
            dto.CanPublish,
            TrimToNull(dto.Reason, MaxShortTextLength, truncatedFields, nameof(dto.Reason)));

    internal static EventMcpProgramManagementContextDescriptor MapProgramManagementContext(
        EventDto eventDto,
        IReadOnlyCollection<EventSessionListDto> sessions,
        IReadOnlyCollection<EventSessionGroupListDto> sessionGroups,
        IReadOnlyCollection<EventDayListDto> days,
        IReadOnlyCollection<EventAgendaItemListDto> agendaItems)
    {
        var truncatedFields = new List<string>();
        var returnedSessions = sessions
            .Take(MaxManagedSessions)
            .Select(session => MapSession(session))
            .ToArray();
        var returnedSessionGroups = sessionGroups
            .Take(MaxManagedSessionGroups)
            .Select(group => MapSessionGroup(group, truncatedFields))
            .ToArray();
        var returnedDays = days
            .Take(MaxManagedDays)
            .Select(day => MapDay(day, truncatedFields))
            .ToArray();
        var returnedAgendaItems = agendaItems
            .Take(MaxManagedAgendaItems)
            .Select(item => MapAgendaItem(item, truncatedFields))
            .ToArray();

        if (sessions.Count > returnedSessions.Length)
        {
            truncatedFields.Add("Sessions");
        }

        if (sessionGroups.Count > returnedSessionGroups.Length)
        {
            truncatedFields.Add("SessionGroups");
        }

        if (days.Count > returnedDays.Length)
        {
            truncatedFields.Add("Days");
        }

        if (agendaItems.Count > returnedAgendaItems.Length)
        {
            truncatedFields.Add("AgendaItems");
        }

        return new EventMcpProgramManagementContextDescriptor(
            eventDto.Id,
            eventDto.ConcurrencyStamp,
            sessions.Count,
            returnedSessions.Length,
            sessions.Count > returnedSessions.Length,
            returnedSessions,
            sessionGroups.Count,
            returnedSessionGroups.Length,
            sessionGroups.Count > returnedSessionGroups.Length,
            returnedSessionGroups,
            days.Count,
            returnedDays.Length,
            days.Count > returnedDays.Length,
            returnedDays,
            agendaItems.Count,
            returnedAgendaItems.Length,
            agendaItems.Count > returnedAgendaItems.Length,
            returnedAgendaItems,
            truncatedFields);
    }

    internal static EventMcpSessionGroupDescriptor MapSessionGroup(
        EventSessionGroupListDto dto,
        ICollection<string> truncatedFields)
        => new(
            dto.Id,
            dto.EventId,
            TrimToEmpty(dto.Name, MaxShortTextLength, truncatedFields, nameof(dto.Name)),
            TrimToNull(dto.Slug, MaxShortTextLength, truncatedFields, nameof(dto.Slug)),
            TrimToNull(dto.Color, MaxShortTextLength, truncatedFields, nameof(dto.Color)),
            dto.SortOrder,
            dto.IsPublished);

    internal static EventMcpDayDescriptor MapDay(
        EventDayListDto dto,
        ICollection<string> truncatedFields)
        => new(
            dto.Id,
            dto.EventId,
            dto.LocalDate,
            TrimToNull(dto.Label, MaxShortTextLength, truncatedFields, nameof(dto.Label)),
            dto.SortOrder,
            dto.IsPublished,
            dto.AllowsDayScopeRegistration);

    internal static EventMcpAgendaItemDescriptor MapAgendaItem(
        EventAgendaItemListDto dto,
        ICollection<string> truncatedFields)
        => new(
            dto.Id,
            dto.EventId,
            TrimToEmpty(dto.Title, MaxShortTextLength, truncatedFields, nameof(dto.Title)),
            dto.StartTime,
            dto.EndTime,
            dto.LocalStartDate,
            dto.LocalStartTime,
            dto.LocalEndTime,
            dto.KindId,
            TrimToNull(dto.KindFullName, MaxShortTextLength, truncatedFields, nameof(dto.KindFullName)),
            dto.SortOrder);

    internal static EventMcpCustomPropertyDefinitionDescriptor MapCustomPropertyDefinition(
        EventCustomPropertyDefinitionListDto dto,
        ICollection<string> truncatedFields)
        => new(
            dto.Id,
            dto.EventId,
            TrimToEmpty(dto.Namespace, MaxShortTextLength, truncatedFields, nameof(dto.Namespace)),
            TrimToEmpty(dto.Key, MaxShortTextLength, truncatedFields, nameof(dto.Key)),
            TrimToEmpty(dto.DisplayName, MaxShortTextLength, truncatedFields, nameof(dto.DisplayName)),
            dto.PropertyType.ToString(),
            dto.IsRequired,
            dto.IsActive,
            dto.SortOrder,
            dto.ExposureLevel.ToString(),
            dto.SourceTemplateId.HasValue,
            dto.OptionCount);

    internal static EventMcpCustomPropertyValueDescriptor MapCustomPropertyValue(
        EventCustomPropertyValueDto dto,
        ICollection<string> truncatedFields)
    {
        var (valueType, value) = FormatCustomPropertyValue(dto, truncatedFields);
        return new EventMcpCustomPropertyValueDescriptor(
            dto.Id,
            dto.EventCustomPropertyDefinitionId,
            dto.EventId,
            dto.Ordinal,
            valueType,
            value);
    }

    internal static (string ValueType, string? Value) FormatCustomPropertyValue(
        EventCustomPropertyValueDto dto,
        ICollection<string> truncatedFields)
    {
        if (dto.TextValue is not null)
        {
            return ("Text", TrimToNull(dto.TextValue, MaxShortTextLength, truncatedFields, nameof(dto.TextValue)));
        }

        if (dto.NumberValue.HasValue)
        {
            return ("Number", dto.NumberValue.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (dto.BooleanValue.HasValue)
        {
            return ("Boolean", dto.BooleanValue.Value ? "true" : "false");
        }

        if (dto.DateTimeValue.HasValue)
        {
            return ("DateTime", dto.DateTimeValue.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        if (dto.OptionId.HasValue)
        {
            return ("Option", dto.OptionId.Value.ToString("D"));
        }

        return ("Empty", null);
    }

    internal static EventMcpRegistrationDescriptor MapRegistrationOrder(
        RegistrationOrderDto dto,
        ICollection<string> truncatedFields)
        => new(
            dto.Id,
            dto.EventId,
            dto.StatusId,
            TrimToNull(dto.StatusCode, MaxShortTextLength, truncatedFields, nameof(dto.StatusCode)),
            TrimToNull(dto.StatusName, MaxShortTextLength, truncatedFields, nameof(dto.StatusName)),
            TrimToNull(dto.CurrencyCode, MaxShortTextLength, truncatedFields, nameof(dto.CurrencyCode)),
            dto.TotalDueMinor,
            dto.ExpiresAt);

    internal static EventMcpTeamMemberDescriptor MapTeamMember(
        EventTeamMemberDto dto,
        ICollection<string> truncatedFields)
        => new(
            dto.AssignmentId,
            TrimToEmpty(dto.UserEmail, MaxShortTextLength, truncatedFields, nameof(dto.UserEmail)),
            TrimToEmpty(dto.UserFullName, MaxShortTextLength, truncatedFields, nameof(dto.UserFullName)),
            TrimToEmpty(dto.RoleName, MaxShortTextLength, truncatedFields, nameof(dto.RoleName)),
            TrimToEmpty(dto.RoleMasterCode, MaxShortTextLength, truncatedFields, nameof(dto.RoleMasterCode)),
            dto.Status.ToString(),
            dto.StartsAtUtc,
            dto.ExpiresAtUtc,
            dto.IsEffective);

    internal static EventMcpCurrentUserPermissionsDescriptor MapCurrentUserPermissions(
        CurrentUserEventPermissionsDto dto,
        ICollection<string> truncatedFields)
    {
        var roleCodes = dto.RoleCodes
            .WhereNotBlank()
            .Take(MaxPermissionCodes)
            .ToArray();
        var permissionCodes = dto.PermissionCodes
            .WhereNotBlank()
            .Take(MaxPermissionCodes)
            .ToArray();

        if (dto.RoleCodes.Count > roleCodes.Length)
        {
            truncatedFields.Add("CurrentUserPermissions.RoleCodes");
        }

        if (dto.PermissionCodes.Count > permissionCodes.Length)
        {
            truncatedFields.Add("CurrentUserPermissions.PermissionCodes");
        }

        return new EventMcpCurrentUserPermissionsDescriptor(
            dto.EventId,
            dto.HasAnyRole,
            dto.IsOwner,
            dto.IsManager,
            roleCodes,
            permissionCodes);
    }

    internal static EventMcpRolePresetDescriptor MapRolePreset(
        EventRolePresetDto dto,
        ICollection<string> truncatedFields)
    {
        var permissionCodes = dto.PermissionCodes
            .WhereNotBlank()
            .Take(MaxPermissionCodes)
            .ToArray();
        if (dto.PermissionCodes.Count > permissionCodes.Length)
        {
            truncatedFields.Add($"AssignableRolePreset:{dto.MasterCode}:PermissionCodes");
        }

        return new EventMcpRolePresetDescriptor(
            dto.RoleId,
            TrimToEmpty(dto.MasterCode, MaxShortTextLength, truncatedFields, nameof(dto.MasterCode)),
            TrimToEmpty(dto.FullName, MaxShortTextLength, truncatedFields, nameof(dto.FullName)),
            TrimToNull(dto.Description, MaxShortTextLength, truncatedFields, nameof(dto.Description)),
            permissionCodes);
    }

    internal static EventMcpTemplateDescriptor MapTemplate(
        EventTemplateListDto dto,
        ICollection<string> truncatedFields)
        => new(
            dto.Id,
            TrimToEmpty(dto.TemplateKey, MaxShortTextLength, truncatedFields, nameof(dto.TemplateKey)),
            TrimToEmpty(dto.DisplayName, MaxShortTextLength, truncatedFields, nameof(dto.DisplayName)),
            TrimToNull(dto.Description, MaxShortTextLength, truncatedFields, nameof(dto.Description)),
            dto.EventTypeId,
            dto.Version,
            dto.IsPublished,
            dto.IsActive,
            dto.SortOrder,
            dto.DefinitionCount);

    internal static EventMcpSessionTemplateDescriptor MapSessionTemplate(EventSessionTemplateListDto dto)
    {
        var truncatedFields = new List<string>();
        return new EventMcpSessionTemplateDescriptor(
            dto.Id,
            dto.EventTemplateId,
            TrimToEmpty(dto.SessionTemplateKey, MaxShortTextLength, truncatedFields, nameof(dto.SessionTemplateKey)),
            TrimToEmpty(dto.DisplayName, MaxShortTextLength, truncatedFields, nameof(dto.DisplayName)),
            TrimToNull(dto.Description, MaxShortTextLength, truncatedFields, nameof(dto.Description)),
            dto.Version,
            dto.IsPublished,
            dto.IsActive,
            dto.SortOrder,
            dto.DefinitionCount,
            truncatedFields);
    }

    internal static EventMcpTemplateDiffDescriptor MapTemplateDiff(EventTemplateDiffDto dto)
        => new(
            dto.TargetTemplateVersion,
            dto.BaseProvenanceVersion,
            CountTemplateDiffChanges(dto),
            dto.AddedDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            dto.ModifiedDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            dto.RetiredDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            dto.AddedOptions.Select(option => CompositeKey(option.Namespace, option.Key)).Take(MaxSyncKeys).ToArray(),
            dto.ModifiedOptions.Select(option => CompositeKey(option.Namespace, option.Key)).Take(MaxSyncKeys).ToArray(),
            dto.RetiredOptions.Select(option => CompositeKey(option.Namespace, option.Key)).Take(MaxSyncKeys).ToArray(),
            dto.UntouchedLocalDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            BuildTemplateDiffTruncatedFields(
                dto.AddedDefinitions.Count,
                dto.ModifiedDefinitions.Count,
                dto.RetiredDefinitions.Count,
                dto.AddedOptions.Count,
                dto.ModifiedOptions.Count,
                dto.RetiredOptions.Count,
                dto.UntouchedLocalDefinitions.Count));

    internal static EventMcpTemplateDiffDescriptor MapTemplateDiff(EventSessionTemplateDiffDto dto)
        => new(
            dto.TargetTemplateVersion,
            dto.BaseProvenanceVersion,
            CountTemplateDiffChanges(dto),
            dto.AddedDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            dto.ModifiedDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            dto.RetiredDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            dto.AddedOptions.Select(option => CompositeKey(option.Namespace, option.Key)).Take(MaxSyncKeys).ToArray(),
            dto.ModifiedOptions.Select(option => CompositeKey(option.Namespace, option.Key)).Take(MaxSyncKeys).ToArray(),
            dto.RetiredOptions.Select(option => CompositeKey(option.Namespace, option.Key)).Take(MaxSyncKeys).ToArray(),
            dto.UntouchedLocalDefinitions.Select(definition => CompositeKey(definition.Namespace, definition.Key)).Take(MaxSyncKeys).ToArray(),
            BuildTemplateDiffTruncatedFields(
                dto.AddedDefinitions.Count,
                dto.ModifiedDefinitions.Count,
                dto.RetiredDefinitions.Count,
                dto.AddedOptions.Count,
                dto.ModifiedOptions.Count,
                dto.RetiredOptions.Count,
                dto.UntouchedLocalDefinitions.Count));

    internal static EventMcpTemplateSyncHistoryPageDescriptor MapEventTemplateSyncHistory(
        Explore.Application.Responses.PaginatedResult<EventTemplateSyncHistoryItemDto> page,
        bool pageSizeWasClamped,
        ICollection<string> truncatedFields)
        => new(
            page.PageNumber,
            page.PageSize,
            pageSizeWasClamped,
            page.TotalCount,
            page.TotalPages,
            page.HasPreviousPage,
            page.HasNextPage,
            page.Items.Select(item => MapTemplateSyncHistoryItem(
                item.BaseProvenanceVersion,
                item.TargetTemplateVersion,
                item.Applied,
                item.Skipped,
                item.Conflicts.Select(conflict => (conflict.Key, conflict.Reason)).ToArray(),
                item.SyncedAt,
                truncatedFields)).ToArray());

    internal static EventMcpTemplateSyncHistoryPageDescriptor MapEventSessionTemplateSyncHistory(
        Explore.Application.Responses.PaginatedResult<EventSessionTemplateSyncHistoryItemDto> page,
        bool pageSizeWasClamped,
        ICollection<string> truncatedFields)
        => new(
            page.PageNumber,
            page.PageSize,
            pageSizeWasClamped,
            page.TotalCount,
            page.TotalPages,
            page.HasPreviousPage,
            page.HasNextPage,
            page.Items.Select(item => MapTemplateSyncHistoryItem(
                item.BaseProvenanceVersion,
                item.TargetTemplateVersion,
                item.Applied,
                item.Skipped,
                item.Conflicts.Select(conflict => (conflict.Key, conflict.Reason)).ToArray(),
                item.SyncedAt,
                truncatedFields)).ToArray());

    internal static EventMcpTemplateSyncHistoryItemDescriptor MapTemplateSyncHistoryItem(
        int baseProvenanceVersion,
        int targetTemplateVersion,
        IReadOnlyList<string> applied,
        IReadOnlyList<string> skipped,
        IReadOnlyList<(string Key, string Reason)> conflicts,
        DateTimeOffset syncedAt,
        ICollection<string> truncatedFields)
    {
        var appliedKeys = applied.WhereNotBlank().Take(MaxSyncKeys).ToArray();
        var skippedKeys = skipped.WhereNotBlank().Take(MaxSyncKeys).ToArray();
        var conflictItems = conflicts
            .Take(MaxSyncKeys)
            .Select(conflict => new EventMcpTemplateSyncConflictDescriptor(
                TrimToEmpty(conflict.Key, MaxShortTextLength, truncatedFields, "SyncConflict.Key"),
                TrimToEmpty(conflict.Reason, MaxShortTextLength, truncatedFields, "SyncConflict.Reason")))
            .ToArray();

        if (applied.Count > appliedKeys.Length)
        {
            truncatedFields.Add("SyncHistory.Applied");
        }

        if (skipped.Count > skippedKeys.Length)
        {
            truncatedFields.Add("SyncHistory.Skipped");
        }

        if (conflicts.Count > conflictItems.Length)
        {
            truncatedFields.Add("SyncHistory.Conflicts");
        }

        return new EventMcpTemplateSyncHistoryItemDescriptor(
            baseProvenanceVersion,
            targetTemplateVersion,
            applied.Count,
            appliedKeys,
            skipped.Count,
            skippedKeys,
            conflicts.Count,
            conflictItems,
            syncedAt);
    }

    internal static EventMcpSummaryDescriptor MapSummary(EventListDto dto)
        => new(
            dto.Id,
            dto.Title,
            TrimToNull(dto.Subtitle, MaxShortTextLength),
            TrimToNull(dto.Description, MaxShortTextLength),
            dto.Slug,
            dto.EventTypeFullName,
            dto.ActorDisplayName,
            dto.EventStatusFullName,
            dto.VisibilityTypeFullName,
            dto.EventFormatFullName,
            dto.FirstSessionDate,
            dto.LastSessionDate,
            dto.Timezone,
            dto.SessionCount,
            dto.ParticipationConfiguration?.ParticipationHandlingModeCode,
            dto.ParticipationConfiguration?.ParticipationHandlingModeName,
            dto.ParticipationConfiguration?.AdvanceRegistrationObligationCode,
            dto.ParticipationConfiguration?.AdvanceRegistrationObligationName,
            dto.ParticipationConfiguration?.IdentityAccessModeCode,
            dto.ParticipationConfiguration?.IdentityAccessModeName,
            dto.ParticipationConfiguration?.GuestRecoveryPolicy);

    internal static EventMcpDetailDescriptor MapDetail(EventDto dto)
    {
        var truncatedFields = new List<string>();

        return new EventMcpDetailDescriptor(
            dto.Id,
            dto.Title,
            TrimToNull(dto.Subtitle, MaxShortTextLength, truncatedFields, nameof(dto.Subtitle)),
            TrimToNull(dto.Description, MaxShortTextLength, truncatedFields, nameof(dto.Description)),
            TrimToNull(dto.Content, MaxLongTextLength, truncatedFields, nameof(dto.Content)),
            dto.Slug,
            dto.EventTypeFullName,
            dto.ActorDisplayName,
            dto.EventStatusFullName,
            dto.VisibilityTypeFullName,
            dto.EventFormatFullName,
            dto.FirstSessionDate,
            dto.LastSessionDate,
            dto.Timezone,
            dto.SessionCount,
            dto.ParticipationConfiguration?.ParticipationHandlingModeCode,
            dto.ParticipationConfiguration?.ParticipationHandlingModeName,
            dto.ParticipationConfiguration?.AdvanceRegistrationObligationCode,
            dto.ParticipationConfiguration?.AdvanceRegistrationObligationName,
            dto.ParticipationConfiguration?.IdentityAccessModeCode,
            dto.ParticipationConfiguration?.IdentityAccessModeName,
            dto.ParticipationConfiguration?.GuestRecoveryPolicy,
            dto.Categories.Select(category => category.FullName).WhereNotBlank().ToArray(),
            dto.Tags.Select(tag => tag.FullName).WhereNotBlank().ToArray(),
            dto.AvailableAspects.WhereNotBlank().ToArray(),
            truncatedFields);
    }

    internal static EventMcpProgramSummaryDescriptor MapProgram(EventProgramSummaryDto dto)
    {
        var truncatedFields = new List<string>();
        var remainingProgramItems = MaxPublicProgramItems;
        var programItemsWereTruncated = false;
        var warningCount = dto.ReadinessWarnings.Count;
        var readinessWarnings = dto.ReadinessWarnings
            .Take(MaxReadinessWarnings)
            .Select(warning => MapWarning(warning, truncatedFields))
            .ToArray();

        var sections = new List<EventMcpProgramSectionDescriptor>();
        foreach (var section in dto.Sections.Take(MaxPublicProgramSections))
        {
            sections.Add(MapProgramSection(section, truncatedFields, ref remainingProgramItems, ref programItemsWereTruncated));
        }

        if (dto.Sections.Count > sections.Count)
        {
            programItemsWereTruncated = true;
            truncatedFields.Add("Program.Sections");
        }

        if (programItemsWereTruncated)
        {
            truncatedFields.Add("Program.Items");
        }

        if (warningCount > MaxReadinessWarnings)
        {
            truncatedFields.Add("ReadinessWarnings");
        }

        return new EventMcpProgramSummaryDescriptor(
            dto.EventId,
            TrimToEmpty(dto.EventTitle, MaxShortTextLength, truncatedFields, nameof(dto.EventTitle)),
            TrimToNull(dto.TimeZoneId, MaxShortTextLength, truncatedFields, nameof(dto.TimeZoneId)),
            dto.Sections.Count,
            CountProgramItems(dto),
            warningCount,
            programItemsWereTruncated,
            warningCount > MaxReadinessWarnings,
            sections,
            readinessWarnings,
            truncatedFields);
    }

    internal static EventMcpProgramSectionDescriptor MapProgramSection(
        EventProgramSectionDto dto,
        ICollection<string> truncatedFields,
        ref int remainingProgramItems,
        ref bool programItemsWereTruncated)
    {
        var sessionGroups = new List<EventMcpProgramSessionGroupDescriptor>();
        foreach (var group in dto.SessionGroups.Take(MaxPublicProgramSessionGroups))
        {
            sessionGroups.Add(MapProgramSessionGroup(group, truncatedFields, ref remainingProgramItems, ref programItemsWereTruncated));
        }

        if (dto.SessionGroups.Count > sessionGroups.Count)
        {
            programItemsWereTruncated = true;
            truncatedFields.Add("ProgramSection.SessionGroups");
        }

        return new EventMcpProgramSectionDescriptor(
            TrimToEmpty(dto.SectionKey, MaxShortTextLength, truncatedFields, nameof(dto.SectionKey)),
            TrimToEmpty(dto.Title, MaxShortTextLength, truncatedFields, nameof(dto.Title)),
            dto.SortOrder,
            sessionGroups);
    }

    internal static EventMcpProgramSessionGroupDescriptor MapProgramSessionGroup(
        EventProgramSessionGroupSectionDto dto,
        ICollection<string> truncatedFields,
        ref int remainingProgramItems,
        ref bool programItemsWereTruncated)
    {
        var days = new List<EventMcpProgramDayDescriptor>();
        foreach (var day in dto.Days.Take(MaxPublicProgramDays))
        {
            days.Add(MapProgramDay(day, truncatedFields, ref remainingProgramItems, ref programItemsWereTruncated));
        }

        if (dto.Days.Count > days.Count)
        {
            programItemsWereTruncated = true;
            truncatedFields.Add("ProgramSessionGroup.Days");
        }

        return new EventMcpProgramSessionGroupDescriptor(
            dto.SessionGroupId,
            TrimToEmpty(dto.Title, MaxShortTextLength, truncatedFields, nameof(dto.Title)),
            dto.SortOrder,
            TrimToNull(dto.Color, MaxShortTextLength, truncatedFields, nameof(dto.Color)),
            MapPublicLocation(dto.EventLocation, truncatedFields),
            days);
    }

    internal static EventMcpProgramDayDescriptor MapProgramDay(
        EventProgramDayGroupDto dto,
        ICollection<string> truncatedFields,
        ref int remainingProgramItems,
        ref bool programItemsWereTruncated)
    {
        if (!dto.LocalDate.HasValue)
        {
            throw new InvalidOperationException("Public program days must have a local date.");
        }

        var items = new List<EventMcpProgramItemDescriptor>();

        foreach (var item in dto.Items)
        {
            if (remainingProgramItems <= 0)
            {
                programItemsWereTruncated = true;
                break;
            }

            items.Add(MapProgramItem(item, truncatedFields));
            remainingProgramItems--;
        }

        return new EventMcpProgramDayDescriptor(
            dto.LocalDate.Value,
            TrimToEmpty(dto.DisplayLabel, MaxShortTextLength, truncatedFields, nameof(dto.DisplayLabel)),
            items);
    }

    internal static EventMcpProgramItemDescriptor MapProgramItem(
        EventProgramItemDto dto,
        ICollection<string> truncatedFields)
    {
        if (!dto.StartsAtUtc.HasValue || !dto.EndsAtUtc.HasValue || !dto.LocalDate.HasValue ||
            !dto.LocalStartTime.HasValue || !dto.LocalEndTime.HasValue)
        {
            throw new InvalidOperationException("Public program items must be fully scheduled.");
        }

        var warningCount = dto.ReadinessWarnings.Count;
        var warnings = dto.ReadinessWarnings
            .Take(MaxReadinessWarnings)
            .Select(warning => MapWarning(warning, truncatedFields))
            .ToArray();

        if (warningCount > MaxReadinessWarnings)
        {
            truncatedFields.Add($"ProgramItem:{dto.SessionId}:ReadinessWarnings");
        }

        return new EventMcpProgramItemDescriptor(
            dto.SessionId,
            TrimToEmpty(dto.Title, MaxShortTextLength, truncatedFields, nameof(dto.Title)),
            dto.EventSessionKindId,
            TrimToNull(dto.EventSessionKindName, MaxShortTextLength, truncatedFields, nameof(dto.EventSessionKindName)),
            TrimToNull(dto.EventSessionKindMasterCode, MaxShortTextLength, truncatedFields, nameof(dto.EventSessionKindMasterCode)),
            dto.StartsAtUtc.Value,
            dto.EndsAtUtc.Value,
            dto.LocalDate.Value,
            dto.LocalStartTime.Value,
            dto.LocalEndTime.Value,
            dto.SortOrder,
            dto.SessionGroupId,
            MapPublicLocation(dto.EventLocation, truncatedFields),
            dto.Capacity,
            TrimToNull(dto.RegistrationModeName, MaxShortTextLength, truncatedFields, nameof(dto.RegistrationModeName)),
            warnings);
    }

    internal static EventMcpReadinessWarningDescriptor MapWarning(
        EventProgramReadinessWarningDto dto,
        ICollection<string> truncatedFields)
        => new(
            TrimToEmpty(dto.Path, MaxShortTextLength, truncatedFields, nameof(dto.Path)),
            TrimToEmpty(dto.Severity, MaxShortTextLength, truncatedFields, nameof(dto.Severity)),
            TrimToEmpty(dto.Message, MaxShortTextLength, truncatedFields, nameof(dto.Message)));

    internal static EventMcpSessionListResultDescriptor MapSessions(Guid eventId, IReadOnlyCollection<EventSessionListDto> sessions)
    {
        var returnedSessions = sessions
            .Take(MaxPublicSessions)
            .Select(session => MapSession(session, includePublicLocation: true))
            .ToArray();

        return new EventMcpSessionListResultDescriptor(
            Found: true,
            EventId: eventId,
            FailureCode: null,
            TotalCount: sessions.Count,
            ReturnedCount: returnedSessions.Length,
            SessionsWereTruncated: sessions.Count > MaxPublicSessions,
            Sessions: returnedSessions);
    }

    internal static EventMcpSessionSummaryDescriptor MapSession(
        EventSessionListDto dto,
        bool includePublicLocation = false)
    {
        var truncatedFields = new List<string>();

        return new EventMcpSessionSummaryDescriptor(
            dto.Id,
            dto.EventId,
            TrimToEmpty(dto.EventTitle, MaxShortTextLength, truncatedFields, nameof(dto.EventTitle)),
            TrimToNull(dto.Title, MaxShortTextLength, truncatedFields, nameof(dto.Title)),
            TrimToNull(dto.Slug, MaxShortTextLength, truncatedFields, nameof(dto.Slug)),
            dto.EventSessionKindId,
            TrimToNull(dto.EventSessionKindFullName, MaxShortTextLength, truncatedFields, nameof(dto.EventSessionKindFullName)),
            TrimToNull(dto.EventSessionKindMasterCode, MaxShortTextLength, truncatedFields, nameof(dto.EventSessionKindMasterCode)),
            dto.StartTime,
            dto.EndTime,
            dto.LocalStartDate,
            dto.LocalStartTime,
            dto.LocalEndTime,
            dto.SortOrder,
            dto.MaxAudienceAttendees,
            TrimToNull(dto.RegistrationModeFullName, MaxShortTextLength, truncatedFields, nameof(dto.RegistrationModeFullName)),
            dto.SessionGroups
                .OrderByDescending(group => group.IsPrimary)
                .ThenBy(group => group.SortOrder)
                .Select(group => group.Name)
                .WhereNotBlank()
                .Take(10)
                .ToArray(),
            includePublicLocation ? MapPublicLocation(dto.EventLocation, truncatedFields) : null,
            truncatedFields);
    }

    internal static EventMcpLocationDescriptor? MapPublicLocation(
        EventLocationPublicDto? dto,
        ICollection<string> truncatedFields)
    {
        if (dto is null)
        {
            return null;
        }

        EventLocationPublicFieldsDto? fields = dto.Fields;
        return new EventMcpLocationDescriptor(
            dto.EventLocationId,
            dto.State.ToString(),
            TrimToNull(fields?.Country, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.Country)),
            TrimToNull(fields?.Timezone, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.Timezone)),
            TrimToNull(fields?.City, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.City)),
            TrimToNull(fields?.VenueName, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.VenueName)),
            TrimToNull(fields?.RoomName, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.RoomName)),
            TrimToNull(fields?.StreetAddress, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.StreetAddress)),
            TrimToNull(fields?.Postcode, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.Postcode)),
            fields?.Latitude,
            fields?.Longitude,
            TrimToNull(fields?.FormattedAddress, MaxLongTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.FormattedAddress)),
            TrimToNull(fields?.MapUrl, MaxLongTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.MapUrl)),
            TrimToNull(fields?.Geohash, MaxShortTextLength, truncatedFields, nameof(EventLocationPublicFieldsDto.Geohash)));
    }


    internal static string CompositeKey(string? @namespace, string? key)
    {
        var normalizedNamespace = TrimToNull(@namespace, MaxShortTextLength);
        var normalizedKey = TrimToNull(key, MaxShortTextLength);
        return normalizedNamespace is null
            ? normalizedKey ?? string.Empty
            : $"{normalizedNamespace}.{normalizedKey ?? string.Empty}";
    }

    internal static string? TrimToNull(string? value, int maxLength)
        => TrimToNull(value, maxLength, truncatedFields: null, fieldName: null);

    internal static string TrimToEmpty(
        string? value,
        int maxLength,
        ICollection<string>? truncatedFields,
        string? fieldName)
        => TrimToNull(value, maxLength, truncatedFields, fieldName) ?? string.Empty;

    internal static string? TrimToNull(
        string? value,
        int maxLength,
        ICollection<string>? truncatedFields,
        string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            truncatedFields?.Add(fieldName);
        }

        return trimmed[..maxLength];
    }


    internal static int CountTemplateDiffChanges(EventTemplateDiffDto dto)
        => dto.AddedDefinitions.Count
           + dto.ModifiedDefinitions.Count
           + dto.RetiredDefinitions.Count
           + dto.AddedOptions.Count
           + dto.ModifiedOptions.Count
           + dto.RetiredOptions.Count;

    internal static int CountTemplateDiffChanges(EventSessionTemplateDiffDto dto)
        => dto.AddedDefinitions.Count
           + dto.ModifiedDefinitions.Count
           + dto.RetiredDefinitions.Count
           + dto.AddedOptions.Count
           + dto.ModifiedOptions.Count
           + dto.RetiredOptions.Count;

    internal static IReadOnlyList<string> BuildTemplateDiffTruncatedFields(
        int addedDefinitions,
        int modifiedDefinitions,
        int retiredDefinitions,
        int addedOptions,
        int modifiedOptions,
        int retiredOptions,
        int untouchedLocalDefinitions)
    {
        var truncatedFields = new List<string>();
        AddIfTruncated(addedDefinitions, "Diff.AddedDefinitions", truncatedFields);
        AddIfTruncated(modifiedDefinitions, "Diff.ModifiedDefinitions", truncatedFields);
        AddIfTruncated(retiredDefinitions, "Diff.RetiredDefinitions", truncatedFields);
        AddIfTruncated(addedOptions, "Diff.AddedOptions", truncatedFields);
        AddIfTruncated(modifiedOptions, "Diff.ModifiedOptions", truncatedFields);
        AddIfTruncated(retiredOptions, "Diff.RetiredOptions", truncatedFields);
        AddIfTruncated(untouchedLocalDefinitions, "Diff.UntouchedLocalDefinitions", truncatedFields);
        return truncatedFields;
    }

    internal static void AddIfTruncated(int count, string fieldName, ICollection<string> truncatedFields)
    {
        if (count > MaxSyncKeys)
        {
            truncatedFields.Add(fieldName);
        }
    }


    internal static int CountProgramItems(EventProgramSummaryDto dto)
        => dto.Sections.Sum(section => section.SessionGroups.Sum(group => group.Days.Sum(day => day.Items.Count)));

}
