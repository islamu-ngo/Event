using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventParticipationConfigurationTests
{
    [Test]
    [Arguments(ParticipationHandlingModeEnum.InformationOnly, 1)]
    [Arguments(ParticipationHandlingModeEnum.WalkIn, 2)]
    [Arguments(ParticipationHandlingModeEnum.ExternalManaged, 3)]
    [Arguments(ParticipationHandlingModeEnum.PlatformManaged, 4)]
    public async Task ParticipationHandlingModeEnum_UsesStableIds(
        ParticipationHandlingModeEnum value,
        int expectedId)
    {
        await Assert.That((int)value).IsEqualTo(expectedId);
    }

    [Test]
    [Arguments(AdvanceRegistrationObligationEnum.NotApplicable, 1)]
    [Arguments(AdvanceRegistrationObligationEnum.Optional, 2)]
    [Arguments(AdvanceRegistrationObligationEnum.Required, 3)]
    public async Task AdvanceRegistrationObligationEnum_UsesStableIds(
        AdvanceRegistrationObligationEnum value,
        int expectedId)
    {
        await Assert.That((int)value).IsEqualTo(expectedId);
    }

    [Test]
    [Arguments(IdentityAccessModeEnum.AccountRequired, 1)]
    [Arguments(IdentityAccessModeEnum.GuestAllowed, 2)]
    [Arguments(IdentityAccessModeEnum.CapabilityTokenAllowed, 3)]
    public async Task IdentityAccessModeEnum_UsesStableIds(IdentityAccessModeEnum value, int expectedId)
    {
        await Assert.That((int)value).IsEqualTo(expectedId);
    }

    [Test]
    [Arguments(GuestRecoveryPolicyEnum.VerifiedEmailRequired, 1)]
    [Arguments(GuestRecoveryPolicyEnum.UnverifiedEmailAccepted, 2)]
    [Arguments(GuestRecoveryPolicyEnum.EmailOptional, 3)]
    [Arguments(GuestRecoveryPolicyEnum.CapabilityLinkOnly, 4)]
    [Arguments(GuestRecoveryPolicyEnum.NoRecovery, 5)]
    public async Task GuestRecoveryPolicyEnum_UsesStableIds(GuestRecoveryPolicyEnum value, int expectedId)
    {
        await Assert.That((int)value).IsEqualTo(expectedId);
    }

    [Test]
    public async Task GuestRecoveryPolicy_IsScalarEnum_NotLookupEntity()
    {
        var domainAssembly = typeof(EventParticipationConfiguration).Assembly;

        await Assert.That(typeof(GuestRecoveryPolicyEnum).IsEnum).IsTrue();
        await Assert.That(domainAssembly.GetType("Explore.Domain.GuestRecoveryPolicy")).IsNull();
    }

    [Test]
    [Arguments("Public information listing only", 1, 1, null, null)]
    [Arguments("Walk-in event", 2, 1, null, null)]
    [Arguments("Event managed on another platform", 3, 3, null, null)]
    [Arguments("Community-reported event linking to official source", 3, 2, null, null)]
    [Arguments("Native registration for members", 4, 3, 1, null)]
    [Arguments("Native public registration", 4, 3, 2, 1)]
    [Arguments("Registration asking only for a name", 4, 3, 3, 5)]
    [Arguments("Walk-in event with optional questionnaire", 2, 1, null, null)]
    [Arguments("Native ticket registration with optional external survey", 4, 2, 2, 3)]
    [Arguments("External form with no ISLAMU synchronization", 3, 2, null, null)]
    public async Task Create_AllConsultationScenarios_Construct(
        string scenario,
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        int? guestRecoveryPolicyId)
    {
        var configuration = Create(
            participationHandlingModeId,
            advanceRegistrationObligationId,
            identityAccessModeId,
            ToRecoveryPolicy(guestRecoveryPolicyId));

        await Assert.That(scenario).IsNotEmpty();
        await Assert.That(configuration.ParticipationHandlingModeId).IsEqualTo(participationHandlingModeId);
        await Assert.That(configuration.AdvanceRegistrationObligationId).IsEqualTo(advanceRegistrationObligationId);
        await Assert.That(configuration.IdentityAccessModeId).IsEqualTo(identityAccessModeId);
        await Assert.That((int?)configuration.GuestRecoveryPolicy).IsEqualTo(guestRecoveryPolicyId);
    }

    [Test]
    [Arguments(2, 1)]
    [Arguments(2, 2)]
    [Arguments(2, 3)]
    [Arguments(3, 4)]
    [Arguments(3, 5)]
    public async Task Create_EveryRecoveryPolicy_ConstructsWithItsLegalIdentityMode(
        int identityAccessModeId,
        int guestRecoveryPolicyId)
    {
        var configuration = Create(
            (int)ParticipationHandlingModeEnum.PlatformManaged,
            (int)AdvanceRegistrationObligationEnum.Required,
            identityAccessModeId,
            ToRecoveryPolicy(guestRecoveryPolicyId));

        await Assert.That(configuration.IdentityAccessModeId).IsEqualTo(identityAccessModeId);
        await Assert.That((int?)configuration.GuestRecoveryPolicy).IsEqualTo(guestRecoveryPolicyId);
    }

    [Test]
    [Arguments(1, 2, null, null, EventParticipationConfigurationErrorCode.AdvanceRegistrationObligationNotAllowed)]
    [Arguments(1, 1, 1, null, EventParticipationConfigurationErrorCode.IdentityAccessModeMustBeAbsent)]
    [Arguments(1, 1, null, 1, EventParticipationConfigurationErrorCode.GuestRecoveryPolicyMustBeAbsent)]
    [Arguments(2, 3, null, null, EventParticipationConfigurationErrorCode.AdvanceRegistrationObligationNotAllowed)]
    [Arguments(3, 1, null, null, EventParticipationConfigurationErrorCode.AdvanceRegistrationObligationNotAllowed)]
    [Arguments(3, 2, 1, null, EventParticipationConfigurationErrorCode.IdentityAccessModeMustBeAbsent)]
    [Arguments(3, 3, null, 1, EventParticipationConfigurationErrorCode.GuestRecoveryPolicyMustBeAbsent)]
    [Arguments(4, 1, 1, null, EventParticipationConfigurationErrorCode.AdvanceRegistrationObligationNotAllowed)]
    [Arguments(4, 3, null, null, EventParticipationConfigurationErrorCode.IdentityAccessModeRequired)]
    [Arguments(4, 3, 1, 1, EventParticipationConfigurationErrorCode.GuestRecoveryPolicyMustBeAbsent)]
    [Arguments(4, 3, 2, null, EventParticipationConfigurationErrorCode.GuestRecoveryPolicyRequired)]
    [Arguments(4, 3, 2, 4, EventParticipationConfigurationErrorCode.GuestRecoveryPolicyNotAllowed)]
    [Arguments(4, 3, 3, null, EventParticipationConfigurationErrorCode.GuestRecoveryPolicyRequired)]
    [Arguments(4, 3, 3, 1, EventParticipationConfigurationErrorCode.GuestRecoveryPolicyNotAllowed)]
    [Arguments(999, 1, null, null, EventParticipationConfigurationErrorCode.UnknownParticipationHandlingMode)]
    [Arguments(3, 999, null, null, EventParticipationConfigurationErrorCode.UnknownAdvanceRegistrationObligation)]
    [Arguments(4, 3, 999, null, EventParticipationConfigurationErrorCode.UnknownIdentityAccessMode)]
    [Arguments(4, 3, 2, 999, EventParticipationConfigurationErrorCode.UnknownGuestRecoveryPolicy)]
    public async Task Create_InvalidMatrix_ThrowsMachineReadableTypedError(
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        int? guestRecoveryPolicyId,
        EventParticipationConfigurationErrorCode expectedCode)
    {
        var exception = CaptureValidationException(() => Create(
            participationHandlingModeId,
            advanceRegistrationObligationId,
            identityAccessModeId,
            ToRecoveryPolicy(guestRecoveryPolicyId)));

        await Assert.That(exception.Errors.Select(error => error.Code)).Contains(expectedCode);
    }

    [Test]
    public async Task Create_EmptyEventId_ThrowsTypedError()
    {
        var exception = CaptureValidationException(() => EventParticipationConfiguration.Create(
            Guid.Empty,
            Guid.CreateVersion7(),
            (int)ParticipationHandlingModeEnum.InformationOnly,
            (int)AdvanceRegistrationObligationEnum.NotApplicable,
            identityAccessModeId: null,
            guestRecoveryPolicy: null,
            DomainTestClock.UtcNow));

        await Assert.That(exception.Errors.Select(error => error.Code))
            .Contains(EventParticipationConfigurationErrorCode.EventIdRequired);
    }

    [Test]
    public async Task Create_EmptyTenantId_ThrowsTypedError()
    {
        var exception = CaptureValidationException(() => EventParticipationConfiguration.Create(
            Guid.CreateVersion7(),
            Guid.Empty,
            (int)ParticipationHandlingModeEnum.InformationOnly,
            (int)AdvanceRegistrationObligationEnum.NotApplicable,
            identityAccessModeId: null,
            guestRecoveryPolicy: null,
            DomainTestClock.UtcNow));

        await Assert.That(exception.Errors.Select(error => error.Code))
            .Contains(EventParticipationConfigurationErrorCode.TenantIdRequired);
    }

    [Test]
    public async Task Create_UsesEventIdAsSharedPrimaryKey_AndImplementsCrossCuttingContracts()
    {
        var eventId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var now = DomainTestClock.UtcNow;
        var configuration = EventParticipationConfiguration.Create(
            eventId,
            tenantId,
            (int)ParticipationHandlingModeEnum.InformationOnly,
            (int)AdvanceRegistrationObligationEnum.NotApplicable,
            identityAccessModeId: null,
            guestRecoveryPolicy: null,
            now);
        var type = typeof(EventParticipationConfiguration);

        await Assert.That(configuration.Id).IsEqualTo(eventId);
        await Assert.That(configuration.TenantId).IsEqualTo(tenantId);
        await Assert.That(configuration.CreatedAt).IsEqualTo(now);
        await Assert.That(typeof(ITenantEntity).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(IAuditableEntity).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(ISoftDeletable).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(IConcurrencyAware).IsAssignableFrom(type)).IsTrue();
    }

    [Test]
    public async Task Reconfigure_InvalidCombination_IsAtomic()
    {
        var configuration = Create(
            (int)ParticipationHandlingModeEnum.PlatformManaged,
            (int)AdvanceRegistrationObligationEnum.Required,
            (int)IdentityAccessModeEnum.GuestAllowed,
            GuestRecoveryPolicyEnum.VerifiedEmailRequired);
        var originalConcurrencyStamp = configuration.ConcurrencyStamp;

        var exception = CaptureValidationException(() => configuration.Reconfigure(
            (int)ParticipationHandlingModeEnum.WalkIn,
            (int)AdvanceRegistrationObligationEnum.Required,
            identityAccessModeId: null,
            guestRecoveryPolicy: null));

        await Assert.That(exception.Errors.Select(error => error.Code))
            .Contains(EventParticipationConfigurationErrorCode.AdvanceRegistrationObligationNotAllowed);
        await Assert.That(configuration.ParticipationHandlingModeId)
            .IsEqualTo((int)ParticipationHandlingModeEnum.PlatformManaged);
        await Assert.That(configuration.AdvanceRegistrationObligationId)
            .IsEqualTo((int)AdvanceRegistrationObligationEnum.Required);
        await Assert.That(configuration.IdentityAccessModeId)
            .IsEqualTo((int)IdentityAccessModeEnum.GuestAllowed);
        await Assert.That(configuration.GuestRecoveryPolicy)
            .IsEqualTo(GuestRecoveryPolicyEnum.VerifiedEmailRequired);
        await Assert.That(configuration.ConcurrencyStamp).IsEqualTo(originalConcurrencyStamp);
    }

    private static EventParticipationConfiguration Create(
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy) => EventParticipationConfiguration.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            participationHandlingModeId,
            advanceRegistrationObligationId,
            identityAccessModeId,
            guestRecoveryPolicy,
            DomainTestClock.UtcNow);

    private static GuestRecoveryPolicyEnum? ToRecoveryPolicy(int? value) =>
        value.HasValue ? (GuestRecoveryPolicyEnum)value.Value : null;

    private static EventParticipationConfigurationValidationException CaptureValidationException(Action action)
    {
        try
        {
            action();
        }
        catch (EventParticipationConfigurationValidationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected participation configuration validation to fail.");
    }
}
