namespace Explore.Domain;

public enum PrivacyErasureSubjectKind
{
    User = 1
}

public enum PrivacyErasureReasonCode
{
    AccountDeletion = 1,
    SubjectErasureRequest = 2,
    PrivacyIncidentRemediation = 3
}

public enum PrivacyErasureSagaStatus
{
    Fenced = 1,
    ProviderPending = 2,
    Completed = 3
}

public enum PrivacyErasureProviderKind
{
    Keycloak = 1,
    Atproto = 2,
    Listmonk = 3,
    Smtp = 4,
    WebPush = 5,
    ObjectStorage = 6,
    Svix = 7,
    AiProvider = 8,
    Webhook = 9,
    Osprey = 10,
    Coop = 11
}

public enum PrivacyErasureProviderAction
{
    DeletePlatformManagedIdentity = 1,
    RevokeOrUnlinkExternalIdentity = 2,
    DeleteOrAnonymizeProviderCopy = 3,
    ExpireLocalMetadataWithoutRecall = 4,
    InvalidateSubscription = 5,
    DeleteOwnedObject = 6,
    PurgeRetainedContext = 7,
    CorrectOrDeleteProviderCopy = 8
}

public enum PrivacyErasureProviderLocatorKind
{
    AccountIdentifier = 1,
    Did = 2,
    EmailAddress = 3,
    WebPushEndpoint = 4,
    ObjectKey = 5,
    ProviderResourceIdentifier = 6,
    AiContextIdentifier = 7
}

public enum PrivacyErasureProviderWorkStatus
{
    Pending = 1,
    Processing = 2,
    RetryScheduled = 3,
    Unknown = 4,
    Completed = 5,
    DeadLettered = 6
}

public enum PrivacyErasureProviderReconciliation
{
    Completed = 1,
    NotCompleted = 2
}
