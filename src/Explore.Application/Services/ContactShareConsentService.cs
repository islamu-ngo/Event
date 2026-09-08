using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public class ContactShareConsentService : IContactShareConsentService
{
    private readonly IEventContactShareConsentRepository _consentRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IUserRepository _userRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<ContactShareConsentService> _logger;

    public ContactShareConsentService(
        IEventContactShareConsentRepository consentRepository,
        IEventSessionRepository eventSessionRepository,
        IEventRepository eventRepository,
        IUserRepository userRepository,
        IActorRepository actorRepository,
        IOrganizationRepository organizationRepository,
        ILogger<ContactShareConsentService> logger)
    {
        _consentRepository = consentRepository;
        _eventSessionRepository = eventSessionRepository;
        _eventRepository = eventRepository;
        _userRepository = userRepository;
        _actorRepository = actorRepository;
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    public async Task<Guid?> ProcessRegistrationConsent(
        Guid tenantId,
        Guid userId,
        Guid eventId,
        Guid registrationOrderId,
        bool shareEmailWithOrganizer,
        string? consentText,
        string? consentUiVersion)
    {
        if (!shareEmailWithOrganizer)
            return null;

        // Resolve event → actor → organisation
        var @event = await _eventRepository.GetById(eventId);
        if (@event == null || @event.TenantId != tenantId)
        {
            _logger.LogWarning("Cannot process consent: event {EventId} not found", eventId);
            return null;
        }

        if (@event.OrganizerActorId is null)
        {
            _logger.LogWarning("Cannot process consent: event {EventId} has no organizer actor", eventId);
            return null;
        }

        var actor = await _actorRepository.GetById(@event.OrganizerActorId.Value);
        if (actor?.OrganizationId == null)
        {
            _logger.LogWarning("Cannot process consent: event {EventId} actor {ActorId} has no organisation", eventId, @event.ActorId);
            return null;
        }

        var organization = await _organizationRepository.GetById(actor.OrganizationId.Value);
        if (organization is null || !organization.TenantParticipations.Any(
                participation => participation.TenantId == tenantId &&
                    participation.ApprovalStatusId == (int)ApprovalStatusEnum.Approved))
        {
            _logger.LogWarning("Cannot process consent: organisation {OrgId} is not approved", actor.OrganizationId);
            return null;
        }

        // Load user PII for email snapshot
        var user = await _userRepository.GetUserWithDetails(userId);
        if (user?.Pii == null || string.IsNullOrWhiteSpace(user.Pii.Email))
        {
            _logger.LogWarning("Cannot process consent: user has no email");
            return null;
        }

        var email = user.Pii.Email;
        var purposeCode = ConsentPurposeCodes.OrganizerFutureCommunications;
        var uiVersion = consentUiVersion ?? ConsentUiVersions.V1;
        var text = consentText ?? $"Share my email address with {organization.FullName} so they can contact me about future events and related updates.";
        var grantedAt = DateTime.UtcNow;

        // Check for existing consent (per-organizer scope)
        var existing = await _consentRepository.GetByScope(tenantId, (int)ContactShareConsentSubjectTypeEnum.User, userId,
            @event.OrganizerActorId.Value, purposeCode);

        if (existing != null)
        {
            EventContactShareConsentHistory history = existing.Regrant(
                email, text, uiVersion, eventId, registrationOrderId, null, userId, grantedAt);
            await _consentRepository.UpdateWithHistory(existing, history);

            _logger.LogInformation(
                "Reactivated contact share consent {ConsentId} for user {UserId} → actor {ActorId}",
                existing.Id, userId, @event.OrganizerActorId.Value);

            return existing.Id;
        }

        // Create new consent
        var consent = EventContactShareConsent.Grant(tenantId, ContactShareConsentSubjectTypeEnum.User, userId,
            @event.OrganizerActorId.Value, purposeCode, email, text, uiVersion, grantedAt);
        EventContactShareConsentHistory grantHistory = consent.CreateGrantHistory(
            eventId, registrationOrderId, null, userId, grantedAt);

        consent = await _consentRepository.CreateWithHistory(consent, grantHistory);

        _logger.LogInformation(
            "Created contact share consent {ConsentId} for user {UserId} → actor {ActorId}",
            consent.Id, userId, @event.OrganizerActorId.Value);

        return consent.Id;
    }

    public async Task<bool> HasGrantedConsentForOrganizer(Guid tenantId, Guid userId, Guid recipientActorId)
    {
        var existing = await _consentRepository.GetByScope(
            tenantId, (int)ContactShareConsentSubjectTypeEnum.User, userId, recipientActorId, ConsentPurposeCodes.OrganizerFutureCommunications);
        return existing?.Status == ConsentStatus.Granted;
    }

    public async Task<Guid?> ResolveRecipientOrganizationId(Guid recipientActorId)
    {
        var actor = await _actorRepository.GetById(recipientActorId);
        return actor?.OrganizationId;
    }

    public async Task WithdrawConsent(Guid tenantId, Guid userId, Guid consentId)
    {
        var consent = await _consentRepository.GetById(consentId);
        if (consent == null)
            throw new KeyNotFoundException($"Consent {consentId} not found");

        if (consent.TenantId != tenantId || consent.SubjectTypeId != (int)ContactShareConsentSubjectTypeEnum.User || consent.SubjectId != userId)
            throw new UnauthorizedAccessException("Cannot withdraw consent belonging to another user");

        if (consent.Status == ConsentStatus.Withdrawn)
            return; // Already withdrawn, idempotent

        EventContactShareConsentHistory history = consent.Withdraw(null, userId, DateTime.UtcNow);
        await _consentRepository.UpdateWithHistory(consent, history);

        _logger.LogInformation(
            "Withdrawn contact share consent {ConsentId} for user {UserId}", consentId, userId);
    }

    public async Task<List<UserContactShareConsentDto>> GetUserConsents(Guid tenantId, Guid userId)
    {
        var consents = await _consentRepository.GetByUser(tenantId, userId);

        return consents.Select(c => new UserContactShareConsentDto
        {
            Id = c.Id,
            RecipientActorId = c.RecipientActorId,
            OrganizationName = c.RecipientActor?.Organization?.FullName
                            ?? c.RecipientActor?.Pii?.DisplayName,
            SourceEventId = null,
            SourceEventTitle = null,
            PurposeCode = c.PurposeCode,
            Status = (int)c.Status,
            EmailSnapshot = c.EmailSnapshot,
            GrantedAt = c.GrantedAt,
            WithdrawnAt = c.WithdrawnAt
        }).ToList();
    }
}
