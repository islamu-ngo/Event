using Explore.Domain;

namespace Explore.Application.Contracts.Services;

public interface ITicketPurchaseAuthorityResolver
{
    Task<TicketPurchaseAuthorityResolution> ResolveAsync(
        TicketPurchaseAuthorityResolutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record TicketPurchaseAuthorityResolutionRequest(
    Guid EventId,
    Guid OrderId,
    TicketPurchaseAccessMode AccessMode,
    Guid? RequestedPurchaserActorId);

public sealed record TicketPurchaseAuthorityResolution
{
    private TicketPurchaseAuthorityResolution(
        TicketPurchaseAuthorityDimension? authority,
        string? failureCode)
    {
        Authority = authority;
        FailureCode = failureCode;
    }

    public bool IsSuccess => Authority is not null;
    public TicketPurchaseAuthorityDimension? Authority { get; }
    public string? FailureCode { get; }

    public static TicketPurchaseAuthorityResolution Success(
        TicketPurchaseAuthorityDimension authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return new TicketPurchaseAuthorityResolution(authority, null);
    }

    public static TicketPurchaseAuthorityResolution Failure(
        string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return new TicketPurchaseAuthorityResolution(
            null,
            failureCode);
    }
}
