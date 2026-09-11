using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Application.DTOs.EventSessionKind;
using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.DTOs.RegistrationScope;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class RegistrationMapper
{
    public static partial RegistrationScopeListDto ToListItem(RegistrationScope source);

    public static partial EventRegistrationPolicyListDto ToListItem(EventRegistrationPolicy source);

    public static partial EventSessionKindListDto ToListItem(EventSessionKind source);

    public static partial ScheduleItemKindListDto ToListItem(ScheduleItemKind source);

    public static partial RegistrationModeListDto ToListItem(RegistrationMode source);

    public static partial RegistrationModeDto ToDetail(RegistrationMode source);
}
