using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.DTOs.EventSession;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.Event;

public sealed record CreateEventDto
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? Content { get; init; }
    public string? Slug { get; init; }
    public int? EventTypeId { get; init; }
    public int? AudienceGenderId { get; init; }
    public int? AudienceAgeId { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? GroupId { get; init; }
    public Guid? FeaturedImageId { get; init; }
    public required ConfigureEventParticipationDto ParticipationConfiguration { get; init; }
    public int EventStatusId { get; init; } = 1;
    public int VisibilityTypeId { get; init; } = 1;
    public int EventFormatId { get; init; } = 1;
    public int? MadhabId { get; init; }
    public string? Timezone { get; init; }
    public string? EventTimeZoneId { get; init; }
    public string? BackgroundColor { get; init; }
    public string? BackgroundEffect { get; init; }
    public Guid? BackgroundImageId { get; init; }
    public Guid? TemplateId { get; init; }
    public Guid? EventSeriesId { get; init; }
    public int? SeriesOrder { get; init; }
    public int? RegistrationPolicyId { get; init; }
    public CreateUpdateIslamicAspectDto? IslamicAspect { get; init; }
    private IReadOnlyList<Guid>? _categoryIds = ImmutableArray<Guid>.Empty;
    private IReadOnlyList<Guid>? _tagIds = ImmutableArray<Guid>.Empty;
    private IReadOnlyList<CreateEventLocationDto>? _locations = ImmutableArray<CreateEventLocationDto>.Empty;
    private IReadOnlyList<CreateEventGraphSessionDto>? _sessions = ImmutableArray<CreateEventGraphSessionDto>.Empty;
    private IReadOnlyList<CreateEventGraphDayDto>? _days = ImmutableArray<CreateEventGraphDayDto>.Empty;
    private IReadOnlyList<CreateEventRoomDto>? _rooms = ImmutableArray<CreateEventRoomDto>.Empty;
    private IReadOnlyList<CreateEventGraphAgendaItemDto>? _agendaItems = ImmutableArray<CreateEventGraphAgendaItemDto>.Empty;

    public IReadOnlyList<Guid> CategoryIds { get => _categoryIds!; init => _categoryIds = value?.ToImmutableArray(); }
    public IReadOnlyList<Guid> TagIds { get => _tagIds!; init => _tagIds = value?.ToImmutableArray(); }
    public IReadOnlyList<CreateEventLocationDto> Locations { get => _locations!; init => _locations = value?.ToImmutableArray(); }
    public IReadOnlyList<CreateEventGraphSessionDto> Sessions { get => _sessions!; init => _sessions = value?.ToImmutableArray(); }
    public IReadOnlyList<CreateEventGraphDayDto> Days { get => _days!; init => _days = value?.ToImmutableArray(); }
    public IReadOnlyList<CreateEventRoomDto> Rooms { get => _rooms!; init => _rooms = value?.ToImmutableArray(); }
    public IReadOnlyList<CreateEventGraphAgendaItemDto> AgendaItems { get => _agendaItems!; init => _agendaItems = value?.ToImmutableArray(); }
}
