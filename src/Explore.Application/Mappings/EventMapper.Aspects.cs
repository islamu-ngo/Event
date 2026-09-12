using Explore.Application.DTOs.EventAspects;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

public static partial class EventMapper
{
    public static EventIslamicAspectDto? ToDetail(EventIslamicAspect? source) => MapIslamicAspect(source);
    public static EventTechAspectDto? ToDetail(EventTechAspect? source) => MapTechAspect(source);

    // Extensions contain scalar settings only, not their shared key or cyclic event back reference.
    [MapperIgnoreSource(nameof(EventIslamicAspect.Id))]
    [MapperIgnoreSource(nameof(EventIslamicAspect.Event))]
    [MapProperty("Madhab.FullName", nameof(EventIslamicAspectDto.MadhabName))]
    [MapProperty("PrimaryLanguage.FullName", nameof(EventIslamicAspectDto.PrimaryLanguageName))]
    private static partial EventIslamicAspectDto? MapIslamicAspect(EventIslamicAspect? source);

    [MapperIgnoreSource(nameof(EventTechAspect.Id))]
    [MapperIgnoreSource(nameof(EventTechAspect.Event))]
    private static partial EventTechAspectDto? MapTechAspect(EventTechAspect? source);
}
