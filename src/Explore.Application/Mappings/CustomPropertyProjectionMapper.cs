using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class CustomPropertyProjectionMapper
{
    // Handlers derive operational signals from the live backlog and clock after projection.
    [MapperIgnoreSource(nameof(CustomPropertyProjectionStatus.Tenant))]
    [MapperIgnoreSource(nameof(CustomPropertyProjectionStatus.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(ProjectionStatusDto.PendingDirtyScopeCount))]
    [MapperIgnoreTarget(nameof(ProjectionStatusDto.RequiresOperatorAction))]
    [MapperIgnoreTarget(nameof(ProjectionStatusDto.OperationalState))]
    [MapperIgnoreTarget(nameof(ProjectionStatusDto.RecommendedAction))]
    public static partial ProjectionStatusDto ToStatus(CustomPropertyProjectionStatus source);

    [MapperIgnoreSource(nameof(CustomPropertyProjectionDirtyScope.Tenant))]
    public static partial ProjectionDirtyScopeDto ToDirtyScope(CustomPropertyProjectionDirtyScope source);

    // Read models expose scalar row values, never the source aggregate/navigation graphs.
    [MapperIgnoreSource(nameof(EventCustomPropertyProjection.Definition))]
    [MapperIgnoreSource(nameof(EventCustomPropertyProjection.Value))]
    [MapperIgnoreSource(nameof(EventCustomPropertyProjection.Event))]
    [MapperIgnoreSource(nameof(EventCustomPropertyProjection.Tenant))]
    [MapperIgnoreSource(nameof(EventCustomPropertyProjection.Option))]
    public static partial EventCustomPropertyProjectionDto ToEventRow(EventCustomPropertyProjection source);

    [MapperIgnoreSource(nameof(EventSessionCustomPropertyProjection.Definition))]
    [MapperIgnoreSource(nameof(EventSessionCustomPropertyProjection.Value))]
    [MapperIgnoreSource(nameof(EventSessionCustomPropertyProjection.EventSession))]
    [MapperIgnoreSource(nameof(EventSessionCustomPropertyProjection.Tenant))]
    [MapperIgnoreSource(nameof(EventSessionCustomPropertyProjection.Option))]
    public static partial EventSessionCustomPropertyProjectionDto ToSessionRow(EventSessionCustomPropertyProjection source);
}
