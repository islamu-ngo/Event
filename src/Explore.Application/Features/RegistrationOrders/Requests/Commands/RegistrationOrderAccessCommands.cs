// ABOUTME: Defines guest-capability and authenticated registration-order entry and lifecycle commands.
// ABOUTME: Keeps caller identity and opaque bearer capability inputs separate from persistence commands.

using System.Text.Json.Serialization;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

public interface IGuestRegistrationOrderAccessCommand
{
    Guid EventId { get; }
    Guid OrderId { get; }
    string? CapabilityToken { get; }
}

public interface IAuthenticatedRegistrationOrderAccessCommand
{
    Guid EventId { get; }
    Guid OrderId { get; }
}

public sealed record StartGuestRegistrationOrderCommand(
    Guid EventId,
    Guid TicketCatalogVersionId,
    BookingPartyTypeEnum BookingPartyType,
    IReadOnlyList<RegistrationOrderLineSelection> Lines,
    int? PlatformContributionBasisPoints = null)
    : IRequest<GuestRegistrationOrderStartDto>
{
    [JsonIgnore]
    public AnonymousRegistrationChallengeAuthority? ChallengeAuthority { get; init; }
}

public sealed record StartAuthenticatedRegistrationOrderCommand(
    Guid EventId,
    Guid TicketCatalogVersionId,
    BookingPartyTypeEnum BookingPartyType,
    IReadOnlyList<RegistrationOrderLineSelection> Lines,
    int? PlatformContributionBasisPoints = null)
    : IRequest<BaseCommandResponse<Guid>>;

public sealed record ReserveAuthenticatedTicketPurchaseCommand(
    Guid EventId,
    Guid OrderId,
    Guid? RequestedPurchaserActorId,
    string OperationKey)
    : IRequest<BaseCommandResponse<Guid>>,
      IAuthenticatedRegistrationOrderAccessCommand;

public sealed record ReserveGuestTicketPurchaseCommand(
    Guid EventId,
    Guid OrderId,
    TicketPurchaseAccessMode AccessMode,
    string? CapabilityToken,
    string OperationKey)
    : IRequest<BaseCommandResponse<Guid>>,
      IGuestRegistrationOrderAccessCommand;

public sealed record ContinueGuestRegistrationOrderCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    int? PlatformContributionBasisPoints = null)
    : IRequest<GuestRegistrationOrderLifecycleResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record FinalizeGuestRegistrationOrderCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<GuestRegistrationOrderLifecycleResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record CancelGuestRegistrationOrderCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<GuestRegistrationOrderLifecycleResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record ClaimGuestRegistrationOrderCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : IRequest<BaseCommandResponse<Guid>>, IGuestRegistrationOrderAccessCommand;

public sealed record ContinueAuthenticatedRegistrationOrderCommand(
    Guid EventId,
    Guid OrderId,
    int? PlatformContributionBasisPoints = null)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record FinalizeAuthenticatedRegistrationOrderCommand(Guid EventId, Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record CancelAuthenticatedRegistrationOrderCommand(Guid EventId, Guid OrderId)
    : IRequest<RegistrationOrderLifecycleResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record MutateGuestRegistrationParticipantsCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    IRegistrationParticipantMutation Mutation)
    : IRequest<BaseCommandResponse<Guid>>, IGuestRegistrationOrderAccessCommand;

public sealed record MutateAuthenticatedRegistrationParticipantsCommand(
    Guid EventId,
    Guid OrderId,
    IRegistrationParticipantMutation Mutation)
    : IRequest<BaseCommandResponse<Guid>>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record LaunchGuestNativeRegistrationAttemptCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    Guid? BindingId = null,
    Guid? SupersededAttemptId = null)
    : IRequest<NativeRegistrationAttemptResult>, IGuestRegistrationOrderAccessCommand;

public sealed record LaunchAuthenticatedNativeRegistrationAttemptCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    Guid? BindingId = null,
    Guid? SupersededAttemptId = null)
    : IRequest<NativeRegistrationAttemptResult>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record LaunchGuestRegistrationProviderAttemptCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId,
    Guid? SupersededAttemptId = null)
    : IRequest<RegistrationProviderAttemptResult>, IGuestRegistrationOrderAccessCommand;

public sealed record LaunchAuthenticatedRegistrationProviderAttemptCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId,
    Guid? SupersededAttemptId = null)
    : IRequest<RegistrationProviderAttemptResult>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record SubmitGuestNativeRegistrationAttemptCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken,
    string? IdempotencyKey,
    IReadOnlyList<RegistrationSubmissionAnswerInput> Answers)
    : IRequest<NativeRegistrationSubmissionResult>, IGuestRegistrationOrderAccessCommand;

public sealed record SubmitAuthenticatedNativeRegistrationAttemptCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken,
    string? IdempotencyKey,
    IReadOnlyList<RegistrationSubmissionAnswerInput> Answers)
    : IRequest<NativeRegistrationSubmissionResult>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record SkipGuestNativeRegistrationRequirementCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken)
    : IRequest<NativeRegistrationSkipResult>, IGuestRegistrationOrderAccessCommand;

public sealed record SkipAuthenticatedNativeRegistrationRequirementCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken)
    : IRequest<NativeRegistrationSkipResult>, IAuthenticatedRegistrationOrderAccessCommand;
