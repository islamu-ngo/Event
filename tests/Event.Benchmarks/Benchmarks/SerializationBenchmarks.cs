using System.Text.Json;

using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Configuration;
using Explore.Application.DTOs.Event;
using Explore.Application.Serialization;
using Explore.Domain.Enums;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
public class SerializationBenchmarks
{
    private EventListDto _eventDto = null!;
    private string _serializedJson = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _eventDto = new EventListDto
        {
            Id = Guid.NewGuid(),
            Title = "Enterprise Architecture Summit 2026",
            Subtitle = "Scaling clean architecture in distributed systems",
            Description = "A technical summit for architecture, CQRS, and performance engineering.",
            Slug = "enterprise-architecture-summit-2026",
            EventTypeId = 2,
            EventTypeFullName = "Conference",
            AudienceGenderId = 1,
            AudienceGenderFullName = "Mixed",
            AudienceAgeId = 3,
            AudienceAgeFullName = "Adults",
            AudienceAgeMinAge = 18,
            AudienceAgeMaxAge = 70,
            ActorId = Guid.NewGuid(),
            ActorDisplayName = "ISLAMU Engineering",
            ActorTypeId = 2,
            ActorTypeFullName = "Organization",
            ActorProfilePictureId = Guid.NewGuid(),
            ActorProfilePictureUri = "https://cdn.openislamu.org/actors/engineering.png",
            TicketPriceSummary = new EventTicketPriceSummaryDto
            {
                SummaryCode = "FIXED",
                CurrencyCode = "EUR",
                CurrencyMinorUnitDigits = 2,
                FromAmountMinor = 4_999
            },
            FeaturedImageId = Guid.NewGuid(),
            FeaturedImageUri = "https://cdn.openislamu.org/events/summit-2026.jpg",
            ParticipationConfiguration = new EventParticipationConfigurationDto
            {
                EventId = Guid.NewGuid(),
                ConcurrencyStamp = Guid.NewGuid(),
                ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged,
                ParticipationHandlingModeCode = "PLATFORM_MANAGED",
                ParticipationHandlingModeName = "Platform managed",
                AdvanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.Required,
                AdvanceRegistrationObligationCode = "REQUIRED",
                AdvanceRegistrationObligationName = "Required",
                IdentityAccessModeId = (int)IdentityAccessModeEnum.AccountRequired,
                IdentityAccessModeCode = "ACCOUNT_REQUIRED",
                IdentityAccessModeName = "Account required"
            },
            EventStatusId = 2,
            EventStatusFullName = "Published",
            VisibilityTypeId = 1,
            VisibilityTypeFullName = "Public",
            EventFormatId = 1,
            EventFormatFullName = "Hybrid",
            MadhabId = 1,
            MadhabFullName = "General",
            SessionCount = 12,
            FirstSessionDate = new DateOnly(2026, 10, 10),
            LastSessionDate = new DateOnly(2026, 10, 12),
            Timezone = "Europe/Paris",
            TotalViews = 34891,
            TenantId = Guid.NewGuid()
        };

        _serializedJson = JsonSerializer.Serialize(_eventDto, ExploreJsonContext.Default.EventListDto);
    }

    [Benchmark(Baseline = true)]
    public string Serialize_SourceGenerated()
    {
        return JsonSerializer.Serialize(_eventDto, ExploreJsonContext.Default.EventListDto);
    }

    [Benchmark]
    public string Serialize_Reflection()
    {
        return JsonSerializer.Serialize(_eventDto);
    }

    [Benchmark]
    public EventListDto? Deserialize_SourceGenerated()
    {
        return JsonSerializer.Deserialize(_serializedJson, ExploreJsonContext.Default.EventListDto);
    }

    [Benchmark]
    public EventListDto? Deserialize_Reflection()
    {
        return JsonSerializer.Deserialize<EventListDto>(_serializedJson);
    }
}
