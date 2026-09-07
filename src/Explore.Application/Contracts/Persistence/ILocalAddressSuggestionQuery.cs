using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public static class LocalAddressSuggestionBounds
{
    public const int MinimumSearchLength = 2;
    public const int MaximumSearchLength = 200;
    public const int MinimumLimit = 1;
    public const int MaximumLimit = 20;
}

public interface ILocalAddressSuggestionQuery
{
    Task<IReadOnlyList<LocalAddressSuggestion>> SearchAsync(
        LocalAddressSuggestionCriteria criteria,
        CancellationToken cancellationToken);
}

public sealed record LocalAddressSuggestionCriteria(
    Guid TenantId,
    Guid ActorId,
    Guid UserId,
    Guid? OrganizationId,
    string SearchText,
    int Limit);

public sealed record LocalAddressSuggestion(
    Guid LocationId,
    Guid ConcurrencyStamp,
    string DisplayName,
    string Address,
    string Postcode,
    LocationAddressSourceEnum Source,
    LocationAddressVisibilityEnum Visibility,
    string? City = null,
    string? Country = null,
    string? Timezone = null);
