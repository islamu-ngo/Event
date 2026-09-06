// ABOUTME: Executes one bounded SQL query for tenant-safe reusable local addresses.
// ABOUTME: Applies authority predicates before projecting bounded exact fields and governance labels.

using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Queries;

public sealed class LocalAddressSuggestionQuery(ExploreDbContext dbContext)
    : ILocalAddressSuggestionQuery
{
    public async Task<IReadOnlyList<LocalAddressSuggestion>> SearchAsync(
        LocalAddressSuggestionCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        cancellationToken.ThrowIfCancellationRequested();

        string searchKey = ValidateAndCreateSearchKey(criteria);
        Guid? organizationId = criteria.OrganizationId;

        var rows = await dbContext.Locations
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(location => location.TenantId == criteria.TenantId)
            .Where(location =>
                location.Pii != null &&
                location.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active &&
                location.LocationKindId != (int)LocationKindEnum.PrivateHome &&
                location.AddressVisibilityId != (int)LocationAddressVisibilityEnum.Quarantined)
            .Where(location =>
                location.AddressVisibilityId == (int)LocationAddressVisibilityEnum.TenantApproved ||
                location.AddressVisibilityId == (int)LocationAddressVisibilityEnum.CreatorPrivate &&
                location.CreatedBy == criteria.ActorId ||
                organizationId.HasValue &&
                location.AddressVisibilityId == (int)LocationAddressVisibilityEnum.OrganizationScoped &&
                location.AddressOrganizationId == organizationId &&
                dbContext.OrganizationMembers.Any(member =>
                    member.UserId == criteria.UserId &&
                    member.TenantId == criteria.TenantId &&
                    !member.IsDeleted &&
                    member.OrganizationTenant.TenantId == criteria.TenantId &&
                    member.OrganizationTenant.OrganizationId == organizationId.Value &&
                    member.OrganizationTenant.ApprovalStatusId == (int)ApprovalStatusEnum.Approved &&
                    !member.OrganizationTenant.IsDeleted &&
                    !member.OrganizationTenant.IsSuspended &&
                    !member.OrganizationTenant.Organization.IsDeleted))
            .Where(location =>
                location.Pii!.AddressSubstringKeyVersion == LocationTextNormalization.CurrentRevision &&
                location.DisplaySortKeyVersion == LocationTextNormalization.CurrentRevision &&
                location.Pii.AddressSubstringKey.Contains(searchKey))
            .OrderBy(location => location.DisplaySortKey)
            .ThenBy(location => location.Id)
            .Select(location => new
            {
                location.Id,
                location.ConcurrencyStamp,
                location.FullName,
                location.Pii!.Address,
                location.Pii.Postcode,
                location.AddressSourceId,
                location.AddressVisibilityId,
                location.City,
                location.Country,
                location.Timezone
            })
            .Take(criteria.Limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new LocalAddressSuggestion(
                row.Id,
                row.ConcurrencyStamp,
                row.FullName,
                row.Address,
                row.Postcode,
                (LocationAddressSourceEnum)row.AddressSourceId,
                (LocationAddressVisibilityEnum)row.AddressVisibilityId,
                row.City,
                row.Country,
                row.Timezone))
            .ToArray();
    }

    private static string ValidateAndCreateSearchKey(LocalAddressSuggestionCriteria criteria)
    {
        if (criteria.TenantId == Guid.Empty || criteria.ActorId == Guid.Empty || criteria.UserId == Guid.Empty)
        {
            throw new ArgumentException("Local address suggestion context is invalid.", nameof(criteria));
        }

        if (criteria.OrganizationId == Guid.Empty)
        {
            throw new ArgumentException("Local address suggestion organization context is invalid.", nameof(criteria));
        }

        string searchText = criteria.SearchText?.Trim() ?? string.Empty;
        if (searchText.Length is
            < LocalAddressSuggestionBounds.MinimumSearchLength
            or > LocalAddressSuggestionBounds.MaximumSearchLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria),
                "Local address suggestion search length is outside the supported range.");
        }

        if (criteria.Limit is
            < LocalAddressSuggestionBounds.MinimumLimit
            or > LocalAddressSuggestionBounds.MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria),
                "Local address suggestion result limit is outside the supported range.");
        }

        return LocationTextNormalization.Normalize(searchText);
    }
}
