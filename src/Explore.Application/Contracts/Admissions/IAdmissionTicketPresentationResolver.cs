using System.Collections.Immutable;

namespace Explore.Application.Contracts.Admissions;

public interface IAdmissionTicketPresentationResolver
{
    Task<ImmutableDictionary<Guid, AdmissionTicketPresentation>> ResolveAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> admissionTicketIds,
        CancellationToken cancellationToken);
}

public sealed record AdmissionTicketPresentation(
    string? HolderDisplayName,
    string TicketTypeName,
    ImmutableArray<AdmissionTicketEntitlementPresentation> Entitlements)
{
    public static AdmissionTicketPresentation Empty { get; } =
        new(null, string.Empty, []);
}

public sealed record AdmissionTicketEntitlementPresentation(
    string ScopeCode,
    string EventTitle,
    string? DayLabel,
    DateOnly? LocalDate,
    string? SessionTitle,
    int IncludedQuantity);
