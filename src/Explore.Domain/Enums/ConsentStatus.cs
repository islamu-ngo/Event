namespace Explore.Domain.Enums;

public enum ConsentStatus
{
    Granted = 1,
    Withdrawn = 2
}

public enum ContactShareConsentSubjectTypeEnum
{
    User = 1,
    RegistrationPurchaser = 2,
    RegistrationParticipant = 3,
    GuestContact = 4
}

public enum EventContactShareConsentHistoryOperationEnum
{
    Grant = 1,
    Withdraw = 2,
    Regrant = 3
}

public enum EventContactShareExportStatusEnum
{
    Requested = 1,
    Completed = 2,
    Failed = 3
}

public enum EventContactShareExportFailureCategoryEnum
{
    None = 0,
    Authorization = 1,
    Validation = 2,
    EmptyResult = 3,
    Storage = 4,
    Unexpected = 5
}
