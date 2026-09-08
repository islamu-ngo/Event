namespace Explore.Domain.Enums;

public enum OrganizerPaymentProviderAccountOperationStatus
{
    ProviderCreateRequested = 1,
    ManualReconciliationRequired = 2,
    BoundToConnection = 3,
    NoProviderAccountConfirmed = 4,
    ProviderRejected = 5
}
