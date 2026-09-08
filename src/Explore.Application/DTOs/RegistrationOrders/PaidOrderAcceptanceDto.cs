namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record PaidOrderAcceptanceDisclosureDto
{
    public required string DisclosureRevision { get; init; }
    public required string AcceptanceTemplateIdentifier { get; init; }
    public required string AcceptanceTemplateText { get; init; }
    public required PaidOrderAcceptanceOrganizerMerchantDto OrganizerMerchant { get; init; }
    public required PaidOrderAcceptanceTenantDirectoryOperatorDto TenantDirectoryOperator { get; init; }
    public required PaidOrderAcceptanceInstanceOperatorDto InstanceOperator { get; init; }
    public required PaidOrderAcceptancePaymentOperationsDto PaymentOperations { get; init; }
    public required DateTimeOffset DeliveryStartsAtUtc { get; init; }
    public required DateTimeOffset DeliveryEndsAtUtc { get; init; }
    public required string EventTimeZoneId { get; init; }
    public required string CurrencyCode { get; init; }
    public required int CurrencyMinorUnitDigits { get; init; }
    public required long OrganizerAmountMinor { get; init; }
    public required long PlatformFeeMinor { get; init; }
    public required long PlatformContributionMinor { get; init; }
    public required long TotalMinor { get; init; }
    public required int RefundPolicyVersion { get; init; }
    public required string RefundPolicyText { get; init; }
    public required string RefundPolicyLanguageTag { get; init; }
    public required string SupportContact { get; init; }
    public required IReadOnlyList<PaidOrderAcceptanceLineDto> Lines { get; init; }
}

public sealed record PaidOrderAcceptanceOrganizerMerchantDto
{
    public required Guid OrganizerActorId { get; init; }
    public required string MerchantDisclosureText { get; init; }
    public required string ProviderCode { get; init; }
    public required string ProviderProfileCode { get; init; }
    public required string ProviderEnvironment { get; init; }
    public required string ProviderCredentialOwner { get; init; }
    public required string ChargeType { get; init; }
    public required string StatementDescriptor { get; init; }
    public required Guid OrganizerPaymentProviderConnectionId { get; init; }
    public required string ConnectPlatformId { get; init; }
    public required string ExternalAccountId { get; init; }
    public required string MerchantCountryCode { get; init; }
}

public sealed record PaidOrderAcceptanceTenantDirectoryOperatorDto
{
    public required Guid DocumentId { get; init; }
    public required Guid RevisionId { get; init; }
    public required string PublicName { get; init; }
    public required string LegalName { get; init; }
    public required string OperatorKindCode { get; init; }
    public required string JurisdictionCountryCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public required string PublicContactEmail { get; init; }
    public required string LegalNoticeUrl { get; init; }
    public required string TermsUrl { get; init; }
    public required string PrivacyUrl { get; init; }
}

public sealed record PaidOrderAcceptanceInstanceOperatorDto
{
    public required Guid OperatorId { get; init; }
    public required string PublicName { get; init; }
    public required string LegalName { get; init; }
    public required string OperatorKindCode { get; init; }
    public string? RegistrationIdentifier { get; init; }
    public required bool IsOfficialInstance { get; init; }
    public required string OfficialOrigin { get; init; }
    public required string JurisdictionCountryCode { get; init; }
    public required string WebsiteUrl { get; init; }
    public required string LegalNoticeUrl { get; init; }
    public required string TermsUrl { get; init; }
    public required string PrivacyUrl { get; init; }
}

public sealed record PaidOrderAcceptancePaymentOperationsDto
{
    public required string ComplaintContact { get; init; }
    public required string ComplaintOwner { get; init; }
    public required string RefundOwner { get; init; }
    public required string DisputeOwner { get; init; }
    public required string ReconciliationOwner { get; init; }
    public required string ActivationStatus { get; init; }
}

public sealed record PaidOrderAcceptanceLineDto
{
    public required Guid OrderLineId { get; init; }
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public required long UnitAmountMinor { get; init; }
    public required long DiscountAmountMinor { get; init; }
    public required long LineTotalMinor { get; init; }
}

public sealed record PaidOrderAcceptanceAcknowledgementDto
{
    public string? DisclosureRevision { get; init; }
    public required bool Acknowledged { get; init; }
}
