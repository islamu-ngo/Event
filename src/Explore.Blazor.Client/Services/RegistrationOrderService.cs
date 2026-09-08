// ABOUTME: Orchestrates generated registration-order client calls for Studio and recovery pages.
// ABOUTME: Reuses authorized managed-event reads and never logs or persists guest bearer capabilities.

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

public sealed class RegistrationOrderService(
    IRegistrationOrderClient orderClient,
    IAuthenticatedRegistrationOrderClient authenticatedClient,
    IGuestRegistrationOrderClient guestClient,
    IEventService eventService,
    Explore.Blazor.Client.Services.Shell.UiShellState shellState,
    IGuestRegistrationOrderCapabilityStore capabilityStore,
    ILogger<RegistrationOrderService> logger,
    IAnonymousRegistrationChallengeClient challengeClient,
    IAnonymousRegistrationChallengeSolver challengeSolver,
    NavigationManager navigation,
    TimeProvider clock,
    AuthenticationStateProvider authentication,
    IGuestRegistrationStatusClient statusClient) : IRegistrationOrderService
{
    public Task<RegistrationCheckoutCompositionDto?> GetCheckoutAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => orderClient.GetRegistrationCheckoutCompositionAsync(eventId, cancellationToken: cancellationToken));

    private static readonly JsonSerializerOptions IntentJson = EventApiJsonSerializerSettings.Configure(new());
    private GuestIntent? _guestIntent;
    private int _guestBusy;
    public event Action? GuestStartChanged;
    public GuestRegistrationStartPhase GuestStartPhase { get; private set; }
    public int GuestProofAttempts { get; private set; }
    public Guid? PendingGuestEventId => _guestIntent is { Submissions: > 0 } intent ? intent.EventId : null;

    public async Task<GuestRegistrationOrderStartDto?> StartGuestAsync(Guid eventId, StartRegistrationOrderRequest request, CancellationToken cancellationToken = default)
    {
        // A submitted intent may only be resolved through explicit retry, never replaced by a new start.
        if (Interlocked.CompareExchange(ref _guestBusy, 1, 0) != 0) return null;
        try
        {
            if (PendingGuestEventId.HasValue) return null;
            if (!await IsAnonymousAsync())
            {
                SetGuestPhase(GuestRegistrationStartPhase.IntentChanged);
                return null;
            }
            var intent = new GuestIntent(eventId, navigation.BaseUri, JsonSerializer.Serialize(request, IntentJson));
            _guestIntent = intent;
            GuestProofAttempts = 0;
            SetGuestPhase(GuestRegistrationStartPhase.Issuing);
            using var issueTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            issueTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            var challenge = await challengeClient.CreateAnonymousRegistrationChallengeAsync(
                eventId, body: intent.Request(), idempotency_Key: intent.Key, cancellationToken: issueTimeout.Token);
            intent.Challenge = challenge.ProtectedChallenge;
            intent.ExpiresAt = challenge.ExpiresAt;
            SetGuestPhase(GuestRegistrationStartPhase.Solving);
            intent.Nonce = await challengeSolver.SolveAsync(challenge, attempts =>
            {
                GuestProofAttempts = attempts;
                GuestStartChanged?.Invoke();
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsAnonymousAsync() || intent.Origin != navigation.BaseUri
                || intent.Body != JsonSerializer.Serialize(request, IntentJson))
            {
                _guestIntent = null;
                SetGuestPhase(GuestRegistrationStartPhase.IntentChanged);
                return null;
            }
            if (intent.ExpiresAt is null || intent.ExpiresAt <= clock.GetUtcNow())
            {
                _guestIntent = null;
                SetGuestPhase(GuestRegistrationStartPhase.Expired);
                return null;
            }
            return await SubmitGuestIntentAsync(intent, cancellationToken);
        }
        catch (Exception exception) when (IsGuestTransportFailure(exception))
        {
            RecordGuestFailure(cancellationToken);
            return null;
        }
        finally { Volatile.Write(ref _guestBusy, 0); }
    }

    public async Task<GuestRegistrationOrderStartDto?> RetryGuestAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _guestBusy, 1, 0) != 0) return null;
        try
        {
            if (_guestIntent is not { Submissions: > 0 } intent) return null;
            if (!await IsAnonymousAsync() || intent.EventId != eventId || intent.Origin != navigation.BaseUri)
            {
                SetGuestPhase(GuestRegistrationStartPhase.IntentChanged);
                return null;
            }
            if (intent.Submissions >= 3)
            {
                SetGuestPhase(GuestRegistrationStartPhase.RetryExhausted);
                return null;
            }
            // Original expired proof may authenticate exact committed recovery. Never reissue here.
            return await SubmitGuestIntentAsync(intent, cancellationToken);
        }
        catch (Exception exception) when (IsGuestTransportFailure(exception))
        {
            RecordGuestFailure(cancellationToken);
            return null;
        }
        finally { Volatile.Write(ref _guestBusy, 0); }
    }

    private async Task<GuestRegistrationOrderStartDto?> SubmitGuestIntentAsync(GuestIntent intent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        intent.Submissions++;
        SetGuestPhase(GuestRegistrationStartPhase.Submitting);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(65));
        var started = await guestClient.StartGuestRegistrationOrderWithCapabilityAsync(
            intent.EventId, intent.Request(), intent.Key, intent.Challenge!, intent.Nonce!, timeout.Token);
        if (started.Response is not { Success: true, Id: { } orderId })
        {
            SetGuestPhase(intent.Submissions >= 3 ? GuestRegistrationStartPhase.RetryExhausted : GuestRegistrationStartPhase.Uncertain);
            return null;
        }
        capabilityStore.Store(intent.EventId, orderId, new GuestRegistrationOrderCapability(started.Capability));
        _guestIntent = null;
        SetGuestPhase(GuestRegistrationStartPhase.Completed);
        return started.Response;
    }

    private void RecordGuestFailure(CancellationToken cancellationToken)
    {
        if (PendingGuestEventId.HasValue)
            SetGuestPhase(_guestIntent!.Submissions >= 3 ? GuestRegistrationStartPhase.RetryExhausted : GuestRegistrationStartPhase.Uncertain);
        else
        {
            _guestIntent = null;
            SetGuestPhase(cancellationToken.IsCancellationRequested
                ? GuestRegistrationStartPhase.Cancelled : GuestRegistrationStartPhase.Unavailable);
        }
    }

    private async Task<bool> IsAnonymousAsync() =>
        !(await authentication.GetAuthenticationStateAsync()).User.Identities.Any(identity => identity.IsAuthenticated);

    private static bool IsGuestTransportFailure(Exception exception) =>
        exception is ApiException or HttpRequestException or OperationCanceledException or JSException or InvalidOperationException or JsonException;

    private void SetGuestPhase(GuestRegistrationStartPhase phase)
    {
        GuestStartPhase = phase;
        GuestStartChanged?.Invoke();
    }

    private sealed class GuestIntent(Guid eventId, string origin, string body)
    {
        public Guid EventId { get; } = eventId;
        public string Origin { get; } = origin;
        public string Body { get; } = body;
        public string Key { get; } = Guid.CreateVersion7().ToString("N");
        public string? Challenge { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? Nonce { get; set; }
        public int Submissions { get; set; }
        public StartRegistrationOrderRequest Request() => JsonSerializer.Deserialize<StartRegistrationOrderRequest>(Body, IntentJson)!;
    }

    public Task<BaseCommandResponseOfGuid?> StartAuthenticatedAsync(Guid eventId, StartRegistrationOrderRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => authenticatedClient.StartAuthenticatedRegistrationOrderAsync(eventId, body: request, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<HalResourceOfRegistrationOrderDto>> GetActorOrdersAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> authorizedEventIds;
        if (shellState.ActiveActorId is { } actorId)
        {
            var actorEvents = await eventService.GetManagedEventsByActorAsync(actorId, cancellationToken: cancellationToken);
            authorizedEventIds = actorEvents.Items.Select(e => e.Id).OfType<Guid>().ToArray();
        }
        else
        {
            var userEvents = await eventService.GetMyEventsPagedAsync(1, 100, cancellationToken);
            authorizedEventIds = userEvents.Items.Select(e => e.Id).OfType<Guid>().ToArray();
        }

        var collections = await Task.WhenAll(authorizedEventIds.Select(eventId => GetEventOrdersAsync(eventId, cancellationToken)));
        return collections.SelectMany(collection => collection).ToArray();
    }

    public async Task<IReadOnlyList<HalResourceOfRegistrationOrderDto>> GetEventOrdersAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            var resource = await orderClient.GetEventRegistrationOrdersAsync(eventId, cancellationToken: cancellationToken);
            return resource._embedded?.Items?.ToArray() ?? [];
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Registration orders were unavailable for event {EventId}. Status: {StatusCode}.", eventId, exception.StatusCode);
            return [];
        }
    }

    public Task<HalResourceOfRegistrationOrderDto?> GetCurrentAsync(Guid eventId, Guid orderId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => authenticatedClient.GetCurrentRegistrationOrderAsync(eventId, orderId, cancellationToken: cancellationToken));

    public Task<HalResourceOfRegistrationOrderDto?> CancelCurrentAsync(Guid eventId, Guid orderId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => authenticatedClient.CancelAuthenticatedRegistrationOrderAsync(eventId, orderId, cancellationToken: cancellationToken));

    public async Task<HalResourceOfRegistrationOrderDto?> ApplyCurrentPromotionAsync(
        Guid eventId,
        Guid orderId,
        HalResourceOfRegistrationOrderDto order,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (order._links?.ContainsKey("apply-promotion") != true || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return await ExecuteAsync(() => authenticatedClient.ApplyAuthenticatedRegistrationOrderPromotionAsync(
            eventId,
            orderId,
            idempotency_Key: NewIdempotencyKey(),
            body: new PromotionCodeRequest { Code = code.Trim() },
            cancellationToken: cancellationToken)) is null
            ? null
            : await GetCurrentAsync(eventId, orderId, cancellationToken);
    }

    public async Task<HalResourceOfRegistrationOrderDto?> RemoveCurrentPromotionAsync(
        Guid eventId,
        Guid orderId,
        HalResourceOfRegistrationOrderDto order,
        CancellationToken cancellationToken = default)
    {
        if (order._links?.ContainsKey("remove-promotion") != true)
        {
            return null;
        }

        return await ExecuteAsync(() => authenticatedClient.RemoveAuthenticatedRegistrationOrderPromotionAsync(
            eventId,
            orderId,
            idempotency_Key: NewIdempotencyKey(),
            cancellationToken: cancellationToken)) is null
            ? null
            : await GetCurrentAsync(eventId, orderId, cancellationToken);
    }

    public Task<HalResourceOfRegistrationOrderParticipantsDto?> GetCurrentParticipantsAsync(
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => authenticatedClient.GetAuthenticatedRegistrationOrderParticipantsAsync(
            eventId, orderId, cancellationToken: cancellationToken));

    public async Task<HalResourceOfRegistrationOrderParticipantsDto?> SaveCurrentParticipantAsync(
        Guid eventId,
        Guid orderId,
        Guid? participantId,
        Guid lineId,
        int ordinal,
        RegistrationParticipantRequest request,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponseOfGuid? response = participantId is { } existingId
            ? await ExecuteAsync(() => authenticatedClient.UpdateAuthenticatedRegistrationOrderParticipantAsync(
                eventId, orderId, existingId, NewIdempotencyKey(), request, cancellationToken: cancellationToken))
            : await ExecuteAsync(() => authenticatedClient.AddAuthenticatedRegistrationOrderParticipantAsync(
                eventId, orderId, NewIdempotencyKey(), request, cancellationToken: cancellationToken));
        Guid? savedId = participantId ?? response?.Id;
        if (response?.Success != true || savedId is null)
        {
            return null;
        }

        if (participantId is null)
        {
            var assignmentRequest = new RegistrationTicketAssignmentsRequest
            {
                Assignments = [new TicketParticipantAssignmentInputDto
                {
                    RegistrationOrderLineId = lineId,
                    Ordinal = ordinal,
                    ParticipantId = savedId.Value
                }]
            };
            BaseCommandResponseOfGuid? assignment = await ExecuteAsync(() =>
                authenticatedClient.AssignAuthenticatedRegistrationOrderTicketsAsync(
                    eventId, orderId, NewIdempotencyKey(), assignmentRequest, cancellationToken: cancellationToken));
            if (assignment?.Success != true)
            {
                return null;
            }
        }

        return await GetCurrentParticipantsAsync(eventId, orderId, cancellationToken);
    }

    public async Task<HalResourceOfRegistrationOrderParticipantsDto?> DeferCurrentParticipantsAsync(
        Guid eventId,
        Guid orderId,
        IReadOnlyCollection<TicketDeferralInputDto> assignments,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponseOfGuid? response = await ExecuteAsync(() =>
            authenticatedClient.DeferAuthenticatedRegistrationOrderTicketsAsync(
                eventId,
                orderId,
                NewIdempotencyKey(),
                new RegistrationTicketDeferralsRequest { Assignments = assignments.ToArray(), AssignmentDeadline = deadline },
                cancellationToken: cancellationToken));
        return response?.Success == true
            ? await GetCurrentParticipantsAsync(eventId, orderId, cancellationToken)
            : null;
    }

    public Task<HalResourceOfRegistrationOrderDto?> ContinueCurrentAsync(
        Guid eventId,
        Guid orderId,
        int? contributionBasisPoints,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => authenticatedClient.ContinueAuthenticatedRegistrationOrderAsync(
            eventId,
            orderId,
            body: new ContinueRegistrationOrderRequest { PlatformContributionBasisPoints = contributionBasisPoints },
            cancellationToken: cancellationToken));

    public Task<HalResourceOfRegistrationOrderDto?> FinalizeCurrentAsync(
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => authenticatedClient.FinalizeAuthenticatedRegistrationOrderAsync(
            eventId,
            orderId,
            cancellationToken: cancellationToken));

    public Task<HalResourceOfGuestRegistrationOrderDto?> GetGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => guestClient.GetGuestRegistrationOrderAsync(eventId, orderId, capability.Value, cancellationToken: cancellationToken));

    public async Task<HalResourceOfGuestRegistrationStatusDto?> GetGuestStatusAsync(
        Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default)
    {
        try
        {
            return await statusClient.GetGuestRegistrationStatusAsync(eventId, orderId, capability.Value, cancellationToken: cancellationToken);
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Private registration status was unavailable. Status: {StatusCode}.", exception.StatusCode);
            return null;
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Private registration status transport was unavailable.");
            return null;
        }
    }

    public async Task<GuestRegistrationCancellationOutcome> CancelConfirmedGuestRegistrationAsync(
        Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await statusClient.CancelConfirmedGuestRegistrationAsync(eventId, orderId, capability.Value, cancellationToken: timeout.Token);
            return GuestRegistrationCancellationOutcome.Succeeded;
        }
        catch (ApiException exception)
        {
            // Never expose or log the upstream problem body or capability; never retry a mutation.
            logger.LogWarning("Private registration cancellation was unavailable. Status: {StatusCode}.", exception.StatusCode);
            return exception.StatusCode == 409 ? GuestRegistrationCancellationOutcome.Conflict : GuestRegistrationCancellationOutcome.Unavailable;
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Private registration cancellation transport was unavailable.");
            return GuestRegistrationCancellationOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Private registration cancellation transport timed out.");
            return GuestRegistrationCancellationOutcome.Unavailable;
        }
    }

    public Task<HalResourceOfRegistrationOrderParticipantsDto?> GetGuestParticipantsAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => guestClient.GetGuestRegistrationOrderParticipantsAsync(
            eventId, orderId, capability.Value, cancellationToken: cancellationToken));

    public async Task<HalResourceOfRegistrationOrderParticipantsDto?> SaveGuestParticipantAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        Guid? participantId,
        Guid lineId,
        int ordinal,
        RegistrationParticipantRequest request,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponseOfGuid? response = participantId is { } existingId
            ? await ExecuteAsync(() => guestClient.UpdateGuestRegistrationOrderParticipantAsync(
                eventId, orderId, existingId, NewIdempotencyKey(), request, capability.Value, cancellationToken: cancellationToken))
            : await ExecuteAsync(() => guestClient.AddGuestRegistrationOrderParticipantAsync(
                eventId, orderId, NewIdempotencyKey(), request, capability.Value, cancellationToken: cancellationToken));
        Guid? savedId = participantId ?? response?.Id;
        if (response?.Success != true || savedId is null)
        {
            return null;
        }

        if (participantId is null)
        {
            BaseCommandResponseOfGuid? assignment = await ExecuteAsync(() =>
                guestClient.AssignGuestRegistrationOrderTicketsAsync(
                    eventId,
                    orderId,
                    NewIdempotencyKey(),
                    new RegistrationTicketAssignmentsRequest
                    {
                        Assignments = [new TicketParticipantAssignmentInputDto
                        {
                            RegistrationOrderLineId = lineId,
                            Ordinal = ordinal,
                            ParticipantId = savedId.Value
                        }]
                    },
                    capability.Value,
                    cancellationToken: cancellationToken));
            if (assignment?.Success != true)
            {
                return null;
            }
        }

        return await GetGuestParticipantsAsync(eventId, orderId, capability, cancellationToken);
    }

    public async Task<HalResourceOfRegistrationOrderParticipantsDto?> DeferGuestParticipantsAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        IReadOnlyCollection<TicketDeferralInputDto> assignments,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponseOfGuid? response = await ExecuteAsync(() =>
            guestClient.DeferGuestRegistrationOrderTicketsAsync(
                eventId,
                orderId,
                NewIdempotencyKey(),
                new RegistrationTicketDeferralsRequest { Assignments = assignments.ToArray(), AssignmentDeadline = deadline },
                capability.Value,
                cancellationToken: cancellationToken));
        return response?.Success == true
            ? await GetGuestParticipantsAsync(eventId, orderId, capability, cancellationToken)
            : null;
    }

    public Task<GuestRegistrationOrderLifecycleResponseDto?> CancelGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => guestClient.CancelGuestRegistrationOrderAsync(eventId, orderId, capability.Value, cancellationToken: cancellationToken));

    public Task<HalResourceOfGuestRegistrationOrderLifecycleResponseDto?> ContinueGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, int? contributionBasisPoints, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => guestClient.ContinueGuestRegistrationOrderAsync(
            eventId,
            orderId,
            capability.Value,
            body: new ContinueRegistrationOrderRequest { PlatformContributionBasisPoints = contributionBasisPoints },
            cancellationToken: cancellationToken));

    public Task<HalResourceOfGuestRegistrationOrderLifecycleResponseDto?> FinalizeGuestAsync(Guid eventId, Guid orderId, GuestRegistrationOrderCapability capability, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => guestClient.FinalizeGuestRegistrationOrderAsync(eventId, orderId, capability.Value, cancellationToken: cancellationToken));

    public async Task<HalResourceOfGuestRegistrationOrderDto?> ApplyGuestPromotionAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        HalResourceOfGuestRegistrationOrderDto order,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (order._links?.ContainsKey("apply-promotion") != true || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return await ExecuteAsync(() => guestClient.ApplyGuestRegistrationOrderPromotionAsync(
            eventId,
            orderId,
            NewIdempotencyKey(),
            new PromotionCodeRequest { Code = code.Trim() },
            capability.Value,
            cancellationToken: cancellationToken)) is null
            ? null
            : await GetGuestAsync(eventId, orderId, capability, cancellationToken);
    }

    public async Task<HalResourceOfGuestRegistrationOrderDto?> RemoveGuestPromotionAsync(
        Guid eventId,
        Guid orderId,
        GuestRegistrationOrderCapability capability,
        HalResourceOfGuestRegistrationOrderDto order,
        CancellationToken cancellationToken = default)
    {
        if (order._links?.ContainsKey("remove-promotion") != true)
        {
            return null;
        }

        return await ExecuteAsync(() => guestClient.RemoveGuestRegistrationOrderPromotionAsync(
            eventId,
            orderId,
            idempotency_Key: NewIdempotencyKey(),
            x_Registration_Order_Capability: capability.Value,
            cancellationToken: cancellationToken)) is null
            ? null
            : await GetGuestAsync(eventId, orderId, capability, cancellationToken);
    }

    private async Task<T?> ExecuteAsync<T>(Func<Task<T>> execute)
        where T : class
    {
        try
        {
            return await execute();
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Registration order request was unavailable. Status: {StatusCode}.", exception.StatusCode);
            return null;
        }
    }

    private static string NewIdempotencyKey() => Guid.CreateVersion7().ToString("D");
}
