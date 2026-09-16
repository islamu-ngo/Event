using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

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
    : ICommand<GuestRegistrationOrderStartDto>
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
    : ICommand<BaseCommandResponse<Guid>>;

public sealed record ReserveAuthenticatedTicketPurchaseCommand(
    Guid EventId,
    Guid OrderId,
    Guid? RequestedPurchaserActorId,
    string OperationKey)
    : ICommand<BaseCommandResponse<Guid>>,
      IAuthenticatedRegistrationOrderAccessCommand;

public sealed record ReserveGuestTicketPurchaseCommand(
    Guid EventId,
    Guid OrderId,
    TicketPurchaseAccessMode AccessMode,
    string? CapabilityToken,
    string OperationKey)
    : ICommand<BaseCommandResponse<Guid>>,
      IGuestRegistrationOrderAccessCommand;

public sealed record ContinueGuestRegistrationOrderCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    int? PlatformContributionBasisPoints = null)
    : ICommand<GuestRegistrationOrderLifecycleResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record FinalizeGuestRegistrationOrderCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : ICommand<GuestRegistrationOrderLifecycleResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record CancelGuestRegistrationOrderCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : ICommand<GuestRegistrationOrderLifecycleResponseDto>, IGuestRegistrationOrderAccessCommand;

public sealed record ClaimGuestRegistrationOrderCommand(Guid EventId, Guid OrderId, string? CapabilityToken)
    : ICommand<BaseCommandResponse<Guid>>, IGuestRegistrationOrderAccessCommand;

public sealed record ContinueAuthenticatedRegistrationOrderCommand(
    Guid EventId,
    Guid OrderId,
    int? PlatformContributionBasisPoints = null)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record FinalizeAuthenticatedRegistrationOrderCommand(Guid EventId, Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record CancelAuthenticatedRegistrationOrderCommand(Guid EventId, Guid OrderId)
    : ICommand<RegistrationOrderLifecycleResponseDto>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record MutateGuestRegistrationParticipantsCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    IRegistrationParticipantMutation Mutation)
    : ICommand<BaseCommandResponse<Guid>>, IGuestRegistrationOrderAccessCommand;

public sealed record MutateAuthenticatedRegistrationParticipantsCommand(
    Guid EventId,
    Guid OrderId,
    IRegistrationParticipantMutation Mutation)
    : ICommand<BaseCommandResponse<Guid>>, IAuthenticatedRegistrationOrderAccessCommand;

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
    : ICommand<NativeRegistrationAttemptResult>, IGuestRegistrationOrderAccessCommand;

public sealed record LaunchAuthenticatedNativeRegistrationAttemptCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    Guid? BindingId = null,
    Guid? SupersededAttemptId = null)
    : ICommand<NativeRegistrationAttemptResult>, IAuthenticatedRegistrationOrderAccessCommand;

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
    : ICommand<RegistrationProviderAttemptResult>, IGuestRegistrationOrderAccessCommand;

public sealed record LaunchAuthenticatedRegistrationProviderAttemptCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId,
    Guid? SupersededAttemptId = null)
    : ICommand<RegistrationProviderAttemptResult>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record SubmitGuestNativeRegistrationAttemptCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken,
    string? IdempotencyKey,
    IReadOnlyList<RegistrationSubmissionAnswerInput> Answers)
    : ICommand<NativeRegistrationSubmissionResult>, IGuestRegistrationOrderAccessCommand;

public sealed record SubmitAuthenticatedNativeRegistrationAttemptCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken,
    string? IdempotencyKey,
    IReadOnlyList<RegistrationSubmissionAnswerInput> Answers)
    : ICommand<NativeRegistrationSubmissionResult>, IAuthenticatedRegistrationOrderAccessCommand;

public sealed record SkipGuestNativeRegistrationRequirementCommand(
    Guid EventId,
    Guid OrderId,
    string? CapabilityToken,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken)
    : ICommand<NativeRegistrationSkipResult>, IGuestRegistrationOrderAccessCommand;

public sealed record SkipAuthenticatedNativeRegistrationRequirementCommand(
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken)
    : ICommand<NativeRegistrationSkipResult>, IAuthenticatedRegistrationOrderAccessCommand;
