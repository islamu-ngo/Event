using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Exceptions;

namespace Event.Api.IntegrationTests.Features;

internal sealed class AdmissionApiScenario
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<string, RecoveryCapabilityState> capabilities = new(StringComparer.Ordinal);
    private bool accountTicketRevoked;
    internal int? RecoveryRateLimitRetryAfterSeconds { get; set; }

    internal AdmissionApiScenario()
    {
        Clock = new FixedTimeProvider(UtcNow);
        TenantId = Guid.CreateVersion7();
        OtherTenantId = Guid.CreateVersion7();
        EventId = Guid.CreateVersion7();
        AccountTicketId = Guid.CreateVersion7();
        RecoveryTicketId = Guid.CreateVersion7();
        CrossTenantTicketId = Guid.CreateVersion7();
        AbsentTicketId = Guid.CreateVersion7();
        RecoveryRecordId = Guid.CreateVersion7();
        PresentIdentity = $"present-{Guid.CreateVersion7():N}@example.test";
        AbsentIdentity = $"absent-{Guid.CreateVersion7():N}@example.test";
        ManualCredential = OpaqueValue();
        ExpiredCapability = Register(TenantId, "ticket-recovery", UtcNow.AddSeconds(-1), false);
        ReplayedCapability = Register(TenantId, "ticket-recovery", UtcNow.AddMinutes(5), true);
        WrongPurposeCapability = Register(TenantId, "ticket-transfer", UtcNow.AddMinutes(5), false);
        WrongTenantCapability = Register(OtherTenantId, "ticket-recovery", UtcNow.AddMinutes(5), false);
    }

    internal FixedTimeProvider Clock { get; }
    internal Guid TenantId { get; }
    internal Guid OtherTenantId { get; }
    internal Guid EventId { get; }
    internal Guid AccountOrderId { get; } = Guid.CreateVersion7();
    internal string AccountHolderDisplayName { get; } = "Account ticket holder";
    internal string AccountTicketTypeName { get; } = "General admission";
    internal string AccountEventTitle { get; } = "Community gathering";
    internal string AccountSessionTitle { get; } = "Opening session";
    internal DateTime IssuedAtUtc { get; } = UtcNow.AddDays(-1);
    internal Guid AccountTicketId { get; }
    internal Guid RecoveryTicketId { get; }
    internal Guid CrossTenantTicketId { get; }
    internal Guid AbsentTicketId { get; }
    internal Guid RecoveryRecordId { get; }
    internal string PresentIdentity { get; }
    internal string AbsentIdentity { get; }
    internal string ManualCredential { get; }
    internal string AccountDisplayReference { get; } = "ACCOUNT-TICKET-DISPLAY-7H2K";
    internal string RecoveryDisplayReference { get; } = "RECOVERY-TICKET-DISPLAY-9Q4M";
    internal string ActiveStatusCode { get; } = "ACTIVE";
    internal string SensitiveClassification { get; } = "SENSITIVE_BEARER";
    internal string QrRepresentation { get; } = "QR_REPRESENTATION_V7";
    internal string PrintModel { get; } = "PRINT_MODEL_V4";
    internal string ExpiredCapability { get; }
    internal string ReplayedCapability { get; }
    internal string WrongPurposeCapability { get; }
    internal string WrongTenantCapability { get; }
    internal int PresentRecoveryRequests { get; private set; }
    internal int AbsentRecoveryRequests { get; private set; }

    internal void RevokeAccountTicket() => accountTicketRevoked = true;

    internal string IssueValidCapability() => Register(
        TenantId, "ticket-recovery", UtcNow.AddMinutes(5), false);

    internal string NewMalformedCapability() => OpaqueValue();

    internal string CapabilityFor(string state) => state switch
    {
        "malformed" => NewMalformedCapability(),
        "expired" => ExpiredCapability,
        "replayed" => ReplayedCapability,
        "wrong-purpose" => WrongPurposeCapability,
        "wrong-tenant" => WrongTenantCapability,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    internal object? RequestRecovery(string email, Type responseType)
    {
        if (RecoveryRateLimitRetryAfterSeconds is int retryAfterSeconds)
        {
            throw new AdmissionRecoveryRateLimitExceededException(retryAfterSeconds);
        }

        if (string.Equals(email, PresentIdentity, StringComparison.OrdinalIgnoreCase))
            PresentRecoveryRequests++;
        else if (string.Equals(email, AbsentIdentity, StringComparison.OrdinalIgnoreCase))
            AbsentRecoveryRequests++;
        return FromJson(responseType, JsonSerializer.Serialize(new { accepted = true, success = true }));
    }

    internal object? ConsumeRecovery(string capability, Type responseType)
    {
        if (!AuthorizeRecovery(capability)) return null;
        return FromJson(responseType, JsonSerializer.Serialize(new
        {
            recoveryRecordId = RecoveryRecordId,
            delivery = DeliveryDocument(RecoveryTicketId)
        }, TestJsonOptions.Default));
    }

    internal object? GetAccountTickets(Type responseType) => FromJson(
        responseType, $"[{TicketPayload(AccountTicketId, AccountStatus())}]");

    internal object? GetAccountTicket(Guid ticketId, Type responseType) => ticketId == AccountTicketId
        ? FromJson(responseType, TicketPayload(ticketId, AccountStatus()))
        : null;

    internal object? GetAccountQr(Guid ticketId, Type responseType) =>
        AccountDelivery(ticketId, responseType, "qr");

    internal object? GetAccountPrint(Guid ticketId, Type responseType) =>
        AccountDelivery(ticketId, responseType, "print");

    private object? AccountDelivery(Guid ticketId, Type responseType, string deliverySurface) =>
        ticketId == AccountTicketId && !accountTicketRevoked
            ? FromJson(responseType, JsonSerializer.Serialize(new
            {
                id = ticketId,
                ticketId,
                eventId = EventId,
                statusCode = ActiveStatusCode,
                displayReference = AccountDisplayReference,
                holderDisplayName = AccountHolderDisplayName,
                ticketTypeName = AccountTicketTypeName,
                entitlements = EntitlementPayload(),
                manualCode = ManualCredential,
                manualCodeClassificationCode = SensitiveClassification,
                qrRepresentation = QrRepresentation,
                printModel = PrintModel,
                deliverySurface
            }, TestJsonOptions.Default))
            : null;

    private bool AuthorizeRecovery(string capability)
    {
        if (!capabilities.TryGetValue(capability, out RecoveryCapabilityState? state)
            || state.TenantId != TenantId
            || state.Purpose != "ticket-recovery"
            || state.ExpiresAt <= Clock.GetUtcNow().UtcDateTime
            || state.Consumed)
            return false;
        state.Consumed = true;
        return true;
    }

    private string Register(Guid tenantId, string purpose, DateTime expiresAt, bool consumed)
    {
        string value = OpaqueValue();
        capabilities[value] = new RecoveryCapabilityState(tenantId, purpose, expiresAt, consumed);
        return value;
    }

    private string AccountStatus() => accountTicketRevoked ? "REVOKED" : ActiveStatusCode;

    private object DeliveryDocument(Guid ticketId) => new
    {
        id = ticketId,
        ticketId,
        eventId = EventId,
        statusCode = ActiveStatusCode,
        displayReference = RecoveryDisplayReference,
        holderDisplayName = AccountHolderDisplayName,
        ticketTypeName = AccountTicketTypeName,
        entitlements = EntitlementPayload(),
        qrRepresentation = QrRepresentation,
        printModel = PrintModel,
        manualCode = ManualCredential,
        manualCodeClassificationCode = SensitiveClassification
    };

    private string TicketPayload(Guid ticketId, string status) => JsonSerializer.Serialize(new
    {
        id = ticketId,
        ticketId,
        eventId = EventId,
        statusCode = status,
        displayReference = AccountDisplayReference,
        registrationOrderId = AccountOrderId,
        holderDisplayName = AccountHolderDisplayName,
        ticketTypeName = AccountTicketTypeName,
        issuedAtUtc = IssuedAtUtc,
        entitlements = EntitlementPayload()
    }, TestJsonOptions.Default);

    private object[] EntitlementPayload() =>
    [
        new
        {
            scopeCode = "EVENT_SESSION",
            eventTitle = AccountEventTitle,
            dayLabel = (string?)null,
            localDate = (DateOnly?)null,
            sessionTitle = AccountSessionTitle,
            includedQuantity = 1
        }
    ];

    private static object? FromJson(Type responseType, string json) =>
        JsonSerializer.Deserialize(json, responseType, TestJsonOptions.Default);

    private static string OpaqueValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private sealed class RecoveryCapabilityState(
        Guid tenantId,
        string purpose,
        DateTime expiresAt,
        bool consumed)
    {
        internal Guid TenantId { get; } = tenantId;
        internal string Purpose { get; } = purpose;
        internal DateTime ExpiresAt { get; } = expiresAt;
        internal bool Consumed { get; set; } = consumed;
    }
}
