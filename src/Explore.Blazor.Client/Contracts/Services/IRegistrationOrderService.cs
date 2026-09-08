using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IRegistrationOrderService
{
    event Action? GuestStartChanged;
    GuestRegistrationStartPhase GuestStartPhase { get; }
    int GuestProofAttempts { get; }
    Guid? PendingGuestEventId { get; }
    Task<GuestRegistrationOrderStartDto?> RetryGuestAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<RegistrationCheckoutCompositionDto?> GetCheckoutAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<GuestRegistrationOrderStartDto?> StartGuestAsync(Guid eventId, StartRegistrationOrderRequest request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> StartAuthenticatedAsync(Guid eventId, StartRegistrationOrderRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HalResourceOfRegistrationOrderDto>> GetActorOrdersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HalResourceOfRegistrationOrderDto>> GetEventOrdersAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderDto?> GetCurrentAsync(Guid eventId, Guid orderId, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderDto?> ContinueCurrentAsync(Guid eventId, Guid orderId, int? contributionBasisPoints, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderDto?> FinalizeCurrentAsync(Guid eventId, Guid orderId, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderDto?> CancelCurrentAsync(Guid eventId, Guid orderId, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderDto?> ApplyCurrentPromotionAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, string code, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderDto?> RemoveCurrentPromotionAsync(Guid eventId, Guid orderId, HalResourceOfRegistrationOrderDto order, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderParticipantsDto?> GetCurrentParticipantsAsync(Guid eventId, Guid orderId, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderParticipantsDto?> SaveCurrentParticipantAsync(Guid eventId, Guid orderId, Guid? participantId, Guid lineId, int ordinal, RegistrationParticipantRequest request, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderParticipantsDto?> DeferCurrentParticipantsAsync(Guid eventId, Guid orderId, IReadOnlyCollection<TicketDeferralInputDto> assignments, DateTimeOffset deadline, CancellationToken cancellationToken = default);
    Task<HalResourceOfGuestRegistrationOrderDto?> GetGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default);
    Task<HalResourceOfGuestRegistrationStatusDto?> GetGuestStatusAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default);
    Task<GuestRegistrationCancellationOutcome> CancelConfirmedGuestRegistrationAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderParticipantsDto?> GetGuestParticipantsAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderParticipantsDto?> SaveGuestParticipantAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, Guid? participantId, Guid lineId, int ordinal, RegistrationParticipantRequest request, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationOrderParticipantsDto?> DeferGuestParticipantsAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, IReadOnlyCollection<TicketDeferralInputDto> assignments, DateTimeOffset deadline, CancellationToken cancellationToken = default);
    Task<GuestRegistrationOrderLifecycleResponseDto?> CancelGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default);
    Task<HalResourceOfGuestRegistrationOrderLifecycleResponseDto?> ContinueGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, int? contributionBasisPoints, CancellationToken cancellationToken = default);
    Task<HalResourceOfGuestRegistrationOrderLifecycleResponseDto?> FinalizeGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default);
    Task<HalResourceOfGuestRegistrationOrderDto?> ApplyGuestPromotionAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, HalResourceOfGuestRegistrationOrderDto order, string code, CancellationToken cancellationToken = default);
    Task<HalResourceOfGuestRegistrationOrderDto?> RemoveGuestPromotionAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, HalResourceOfGuestRegistrationOrderDto order, CancellationToken cancellationToken = default);
}
