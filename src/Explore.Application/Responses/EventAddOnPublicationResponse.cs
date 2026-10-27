using Explore.Application.DTOs.EventAddOns;

namespace Explore.Application.Responses;

public sealed record EventAddOnPublicationResponse : BaseCommandResponse<Guid>
{
    private EventAddOnPublicationResponse(BaseCommandResponse<Guid> state, EventAddOnCatalogDto? catalog)
        : base(state, true) => Catalog = catalog;

    public EventAddOnCatalogDto? Catalog { get; }

    public static EventAddOnPublicationResponse Success(EventAddOnCatalogDto catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new(BaseCommandResponse.Success(catalog.Id
            ?? throw new ArgumentException("A published catalog requires its native identity.", nameof(catalog))), catalog);
    }

    public static EventAddOnPublicationResponse Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
}
