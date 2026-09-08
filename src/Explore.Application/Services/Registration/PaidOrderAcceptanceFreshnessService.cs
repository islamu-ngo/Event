using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public interface IPaidOrderAcceptanceFreshnessService
{
    Task<bool> IsCurrentAsync(PaymentAttempt attempt, CancellationToken cancellationToken);
}

public sealed class PaidOrderAcceptanceFreshnessService(
    IRegistrationInventoryRepository orders,
    IPaidOrderAcceptanceService acceptances) : IPaidOrderAcceptanceFreshnessService
{
    public async Task<bool> IsCurrentAsync(PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.AcceptanceSnapshot is not { } snapshot)
        {
            return false;
        }
        RegistrationOrder? order = await orders.GetOrderWithLinesAsync(
            attempt.RegistrationOrderId, attempt.TenantId, cancellationToken);
        if (order is null)
        {
            return false;
        }
        PaidOrderAcceptanceResult current = await acceptances.DescribeAsync(order, attempt.Id, cancellationToken);
        if (current.Disclosure is not { } disclosure ||
            current.Authority is not { } authority ||
            !Matches(snapshot, disclosure, authority, order, attempt))
        {
            return false;
        }
        try
        {
            return snapshot.MatchesLineFacts(disclosure.Lines.Select(line => PaidOrderAcceptanceLineFact.Create(
                line.OrderLineId, line.Name, line.Quantity, line.UnitAmountMinor,
                line.DiscountAmountMinor, line.LineTotalMinor)));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Matches(
        PaidOrderAcceptanceSnapshot snapshot,
        DTOs.RegistrationOrders.PaidOrderAcceptanceDisclosureDto disclosure,
        PaidOrderAcceptanceAuthorityFacts authority,
        RegistrationOrder order,
        PaymentAttempt attempt) =>
        snapshot.OrganizerActorId == authority.OrganizerActorId &&
        snapshot.OrganizerActorId == attempt.RecipientSnapshot.OrganizerActorId &&
        snapshot.OrganizerPaymentProviderConnectionId == attempt.RecipientSnapshot.OrganizerPaymentProviderConnectionId &&
        snapshot.ConnectPlatformId == attempt.RecipientSnapshot.ConnectPlatformId &&
        snapshot.ExternalAccountId == attempt.RecipientSnapshot.ExternalAccountId &&
        snapshot.MerchantCountryCode == attempt.RecipientSnapshot.MerchantCountryCode &&
        snapshot.InstancePolicyVersionId == authority.InstancePolicyVersionId &&
        snapshot.TenantPolicyVersionId == authority.TenantPolicyVersionId &&
        snapshot.OperatorId == authority.OperatorId &&
        snapshot.TenantDirectoryOperatorDocumentId == authority.TenantDirectoryOperatorDocumentId &&
        snapshot.TenantDirectoryOperatorRevisionId == authority.TenantDirectoryOperatorRevisionId &&
        snapshot.CurrencyCode == attempt.CurrencyCode &&
        snapshot.OrganizerAmountMinor == attempt.OrganizerAmountMinor &&
        snapshot.PlatformFeeMinor == attempt.PlatformFeeMinor &&
        snapshot.PlatformContributionMinor == attempt.PlatformContributionMinor &&
        snapshot.TotalMinor == attempt.TotalMinor &&
        snapshot.DisclosureRevision == disclosure.DisclosureRevision &&
        snapshot.CompositionRevision == order.ConcurrencyStamp.ToString("N") &&
        snapshot.AcceptanceTemplateIdentifier == disclosure.AcceptanceTemplateIdentifier &&
        snapshot.AcceptanceTemplateText == disclosure.AcceptanceTemplateText &&
        snapshot.MerchantDisclosureText == disclosure.OrganizerMerchant.MerchantDisclosureText &&
        snapshot.OrganizerActorId == disclosure.OrganizerMerchant.OrganizerActorId &&
        snapshot.OrganizerPaymentProviderConnectionId == disclosure.OrganizerMerchant.OrganizerPaymentProviderConnectionId &&
        snapshot.ConnectPlatformId == disclosure.OrganizerMerchant.ConnectPlatformId &&
        snapshot.ExternalAccountId == disclosure.OrganizerMerchant.ExternalAccountId &&
        snapshot.MerchantCountryCode == disclosure.OrganizerMerchant.MerchantCountryCode &&
        snapshot.TenantDirectoryOperatorDocumentId == disclosure.TenantDirectoryOperator.DocumentId &&
        snapshot.TenantDirectoryOperatorRevisionId == disclosure.TenantDirectoryOperator.RevisionId &&
        snapshot.TenantDirectoryOperatorPublicName == disclosure.TenantDirectoryOperator.PublicName &&
        snapshot.TenantDirectoryOperatorLegalName == disclosure.TenantDirectoryOperator.LegalName &&
        snapshot.TenantDirectoryOperatorKindCode == disclosure.TenantDirectoryOperator.OperatorKindCode &&
        snapshot.TenantDirectoryOperatorCountryCode == disclosure.TenantDirectoryOperator.JurisdictionCountryCode &&
        snapshot.TenantDirectoryOperatorRegistrationIdentifier == disclosure.TenantDirectoryOperator.RegistrationIdentifier &&
        snapshot.TenantDirectoryOperatorPublicContactEmail == disclosure.TenantDirectoryOperator.PublicContactEmail &&
        snapshot.TenantDirectoryOperatorLegalNoticeUrl == disclosure.TenantDirectoryOperator.LegalNoticeUrl &&
        snapshot.TenantDirectoryOperatorTermsUrl == disclosure.TenantDirectoryOperator.TermsUrl &&
        snapshot.TenantDirectoryOperatorPrivacyUrl == disclosure.TenantDirectoryOperator.PrivacyUrl &&
        snapshot.OperatorDisplayName == disclosure.InstanceOperator.PublicName &&
        snapshot.OperatorLegalName == disclosure.InstanceOperator.LegalName &&
        snapshot.OperatorKindCode == disclosure.InstanceOperator.OperatorKindCode &&
        snapshot.OperatorRegistrationIdentifier == disclosure.InstanceOperator.RegistrationIdentifier &&
        snapshot.IsOfficialInstance == disclosure.InstanceOperator.IsOfficialInstance &&
        snapshot.OfficialOrigin == disclosure.InstanceOperator.OfficialOrigin &&
        snapshot.OperatorRegionCode == disclosure.InstanceOperator.JurisdictionCountryCode &&
        snapshot.OperatorWebsiteUrl == disclosure.InstanceOperator.WebsiteUrl &&
        snapshot.OperatorLegalNoticeUrl == disclosure.InstanceOperator.LegalNoticeUrl &&
        snapshot.OperatorTermsUrl == disclosure.InstanceOperator.TermsUrl &&
        snapshot.OperatorPrivacyUrl == disclosure.InstanceOperator.PrivacyUrl &&
        snapshot.ActivationStatus == disclosure.PaymentOperations.ActivationStatus &&
        snapshot.DeliveryStartsAtUtc == disclosure.DeliveryStartsAtUtc &&
        snapshot.DeliveryEndsAtUtc == disclosure.DeliveryEndsAtUtc &&
        snapshot.EventTimeZoneId == disclosure.EventTimeZoneId &&
        snapshot.CurrencyCode == disclosure.CurrencyCode &&
        snapshot.OrganizerAmountMinor == disclosure.OrganizerAmountMinor &&
        snapshot.PlatformFeeMinor == disclosure.PlatformFeeMinor &&
        snapshot.PlatformContributionMinor == disclosure.PlatformContributionMinor &&
        snapshot.TotalMinor == disclosure.TotalMinor &&
        snapshot.RefundPolicyVersion == disclosure.RefundPolicyVersion &&
        snapshot.RefundPolicyText == disclosure.RefundPolicyText &&
        snapshot.RefundPolicyLanguageTag == disclosure.RefundPolicyLanguageTag &&
        snapshot.SupportContact == disclosure.SupportContact &&
        snapshot.ComplaintContact == disclosure.PaymentOperations.ComplaintContact &&
        snapshot.ComplaintOwner == disclosure.PaymentOperations.ComplaintOwner &&
        snapshot.RefundOwner == disclosure.PaymentOperations.RefundOwner &&
        snapshot.DisputeOwner == disclosure.PaymentOperations.DisputeOwner &&
        snapshot.ReconciliationOwner == disclosure.PaymentOperations.ReconciliationOwner &&
        snapshot.ProviderCode == attempt.RecipientSnapshot.ProviderCode &&
        snapshot.ProviderProfileCode == attempt.RecipientSnapshot.ProfileCode &&
        snapshot.ProviderCode == disclosure.OrganizerMerchant.ProviderCode &&
        snapshot.ProviderProfileCode == disclosure.OrganizerMerchant.ProviderProfileCode &&
        snapshot.ProviderEnvironment == disclosure.OrganizerMerchant.ProviderEnvironment &&
        snapshot.ProviderCredentialOwner == disclosure.OrganizerMerchant.ProviderCredentialOwner &&
        snapshot.ChargeType == disclosure.OrganizerMerchant.ChargeType &&
        snapshot.StatementDescriptor == disclosure.OrganizerMerchant.StatementDescriptor;
}
