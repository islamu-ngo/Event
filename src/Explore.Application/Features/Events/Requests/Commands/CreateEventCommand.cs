using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Responses;
using Explore.Domain.Ai;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Create)]
public sealed record CreateEventCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string PreCreateResourceId = "create";

    public required CreateEventDto EventDto { get; init; }

    [JsonIgnore]
    internal AiDraftCreationBinding? AiDraftOrigin { get; private init; }

    /// <summary>
    /// Trusted in-process AI construction, not an HTTP/MCP input. Mapping uses the exact acquired
    /// payload; the handler and repository independently reauthorize its persisted confirmation.
    /// Attachment transfer remains a separate, later operation after primary creation.
    /// </summary>
    public static CreateEventCommand FromAiDraft(
        AiToolExecution execution, string payloadJson, CreateEventDraftAiActionMappingContext? mappingContext = null)
    {
        if (!execution.HasDurablePrimaryReservation
            || !string.Equals(execution.PayloadDigest, AiToolExecution.DigestPayload(payloadJson), StringComparison.Ordinal))
            throw new InvalidOperationException("AI creation requires the exact acquired draft payload.");
        var mapped = new CreateEventDraftAiActionMapper().Map(payloadJson, mappingContext);
        if (!mapped.Succeeded)
            throw new InvalidOperationException("The acquired AI event draft cannot be mapped.");
        var dto = mapped.Draft!.ToCreateEventDto();
        return new CreateEventCommand
        {
            EventDto = dto,
            AiDraftOrigin = new(AiEventDraftRecoveryIdentity.FromExecution(execution),
                execution.Id, execution.ReservedEventId!.Value, AiToolExecution.DigestPayload(JsonSerializer.Serialize(dto)))
        };
    }

    internal bool HasUnchangedAiDraftPayload() => AiDraftOrigin is null
        || string.Equals(AiDraftOrigin.CommandDigest,
            AiToolExecution.DigestPayload(JsonSerializer.Serialize(EventDto)), StringComparison.Ordinal);

    internal sealed record AiDraftCreationBinding(
        AiEventDraftRecoveryIdentity Identity, Guid ExecutionId, Guid ReservedEventId, string CommandDigest)
    {
        public override string ToString() => "AiDraftCreationBinding { Redacted = true }";
    }

    string? ISecureRequest.ResourceId => PreCreateResourceId;

    // No event row exists yet, so the requested owning organization or group is the only context a
    // policy can weigh. The handler still verifies the caller's membership before persisting.
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new PreCreateAuthorizationFacts(
        Guid.Empty,
        ParentEventId: null,
        OrganizationId: EventDto.OrganizationId,
        GroupId: EventDto.GroupId);
}
