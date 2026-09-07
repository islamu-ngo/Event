using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services.Registration;

public sealed record PaidOrderAcceptanceAuthorityFacts(
    Guid OrganizerActorId,
    Guid OperatorId,
    Guid TenantDirectoryOperatorDocumentId,
    Guid TenantDirectoryOperatorRevisionId,
    Guid InstancePolicyVersionId,
    Guid? TenantPolicyVersionId);

public sealed record PaidOrderAcceptanceResult(
    PaidOrderAcceptanceDisclosureDto? Disclosure,
    PaidOrderAcceptanceSnapshot? Snapshot,
    string? FailureCode,
    string? Message,
    PaidOrderAcceptanceAuthorityFacts? Authority = null)
{
    public bool Success => Disclosure is not null && FailureCode is null;
}

public interface IPaidOrderAcceptanceService
{
    Task<PaidOrderAcceptanceResult> DescribeAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken);

    Task<PaidOrderAcceptanceResult> DescribeAsync(
        RegistrationOrder order,
        Guid? reservedPaymentAttemptId,
        CancellationToken cancellationToken);

    Task<PaidOrderAcceptanceResult> AcceptAsync(
        RegistrationOrder order,
        PaidOrderAcceptanceAcknowledgementDto? acknowledgement,
        DateTime acceptedAt,
        CancellationToken cancellationToken);
}

public sealed class PaidOrderAcceptanceService(
    IEventTicketCatalogRepository catalogs,
    IEventRepository events,
    IPaidEventPolicyRepository policies,
    IInstanceOperatorIdentity instanceOperatorIdentity,
    IPaidCheckoutGovernance governance,
    ITenantDirectoryOperatorReadinessEvaluator directoryOperatorReadiness,
    IOrganizerPaymentProviderConnectionRepository connections,
    IOrganizerPaymentCommerceConfiguration commerceConfiguration,
    IPaidCheckoutActivationService activation,
    IPaymentProviderDescriptor providerDescriptor,
    TimeProvider timeProvider) : IPaidOrderAcceptanceService
{
    private readonly ITenantDirectoryOperatorReadinessEvaluator _directoryOperatorReadiness =
        directoryOperatorReadiness;

    public Task<PaidOrderAcceptanceResult> DescribeAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken) => DescribeAsync(order, null, cancellationToken);

    public async Task<PaidOrderAcceptanceResult> DescribeAsync(
        RegistrationOrder order,
        Guid? reservedPaymentAttemptId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        TenantDirectoryOperatorReadinessAssessment directoryReadiness =
            await _directoryOperatorReadiness.EvaluateAsync(
                order.TenantId,
                TenantDirectoryOperatorIdentityCapability.PaidCommerce,
                cancellationToken);
        if (!directoryReadiness.IsReady || directoryReadiness.Identity is not { } directoryIdentity ||
            directoryReadiness.DocumentId is not Guid directoryDocumentId || directoryDocumentId == Guid.Empty ||
            directoryReadiness.DocumentRevision is not { } directoryRevision)
        {
            return Failure(
                "tenant_directory_operator_identity_unavailable",
                "Tenant directory operator identity is unavailable for paid commerce.");
        }

        PaidCheckoutActivationResult activationResult = await activation.EvaluateAsync(
            new(order.TenantId, order.EventId, order.CurrencyCode, order.TotalDueMinorSnapshot,
                timeProvider.GetUtcNow().UtcDateTime, reservedPaymentAttemptId), cancellationToken);
        if (!activationResult.IsActive)
        {
            return Failure(activationResult.FailureCode ?? "payment_activation_unavailable", activationResult.Message);
        }

        EventTicketCatalogVersion? catalog = await catalogs.GetOrderCatalogAsync(
            order.TicketCatalogVersionId, order.EventId, order.TenantId, cancellationToken);
        Event? eventTarget = await events.GetEventWithDetailsAsync(
            order.EventId,
            order.TenantId,
            cancellationToken);
        PaidEventPolicyVersion? instancePolicy = await policies.GetActiveInstanceAsync(cancellationToken);
        PaidEventPolicyVersion? tenantPolicy = await policies.GetActiveTenantAsync(order.TenantId, cancellationToken);
        PaymentProviderDescriptor provider = providerDescriptor.Describe();
        if (catalog is null || eventTarget?.TenantId != order.TenantId ||
            eventTarget.OrganizerActorId is not Guid organizerActorId || instancePolicy is null ||
            !instancePolicy.IsActive || !instancePolicy.IsPaymentsEnabled || instancePolicy.TenantId is not null ||
            !governance.IsConfigured || !governance.IsActivated || string.IsNullOrWhiteSpace(catalog.MerchantDisclosureText) ||
            string.IsNullOrWhiteSpace(catalog.RefundPolicyDisclosureText) || string.IsNullOrWhiteSpace(catalog.SupportContactDisclosureText) ||
            eventTarget.FirstSessionStartUtc is not { } deliveryStart || eventTarget.LastSessionEndUtc is not { } deliveryEnd ||
            string.IsNullOrWhiteSpace(eventTarget.EventTimeZoneId) || order.Lines.Count == 0)
        {
            return Failure("payment_acceptance_unavailable", "Complete current payment disclosures are unavailable.");
        }

        OrganizerPaymentProviderConnection? connection = await connections.GetActiveByScopeAsync(
            order.TenantId, organizerActorId, commerceConfiguration.ProviderCode,
            commerceConfiguration.ConnectPlatformId, cancellationToken);
        if (connection?.MerchantCountryCode is not { } merchantCountryCode ||
            !string.Equals(connection.ProviderCode, provider.ProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("payment_connection_unavailable", "The organizer payment connection is not ready.");
        }

        if (tenantPolicy is not null)
        {
            try
            {
                PaidEventPolicyRules.ValidateTenantPolicy(instancePolicy, tenantPolicy);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Failure("payment_policy_invalid", "The effective paid-event policy is invalid.");
            }

            if (!tenantPolicy.IsActive || !tenantPolicy.IsPaymentsEnabled)
            {
                return Failure("payment_policy_unavailable", "Paid events are not enabled by the current policy.");
            }
        }

        PaidEventPolicyVersion effectivePolicy = tenantPolicy ?? instancePolicy;
        if (!effectivePolicy.AllowedCurrencyCodes.Contains(order.CurrencyCode, StringComparer.Ordinal))
        {
            return Failure("payment_currency_unsupported", "The order currency is not enabled by the current policy.");
        }

        PaidOrderAcceptanceLineDto[] lines = order.Lines.OrderBy(line => line.Id).Select(line => new PaidOrderAcceptanceLineDto
        {
            OrderLineId = line.Id,
            Name = line.TicketTypeNameSnapshot,
            Quantity = line.Quantity,
            UnitAmountMinor = line.UnitPriceAmountSnapshot,
            DiscountAmountMinor = line.PromotionDiscountAmountMinorSnapshot,
            LineTotalMinor = line.PostDiscountLineSubtotalMinorSnapshot
        }).ToArray();
        if (lines.Sum(line => line.LineTotalMinor) != order.OrganizerDirectedTotalMinorSnapshot)
        {
            return Failure("payment_acceptance_unavailable", "Complete current payment disclosures are unavailable.");
        }

        PaidCheckoutTenantDirectoryOperatorDisclosure directoryDisclosure;
        PaidCheckoutOperatorDisclosure operatorDisclosure;
        PaidOrderDeliverySnapshot delivery;
        PaidCheckoutProviderDisclosure providerDisclosure;
        try
        {
            directoryDisclosure = PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                directoryDocumentId,
                directoryRevision,
                directoryIdentity.PublicName,
                directoryIdentity.LegalName,
                directoryIdentity.OperatorKindCode,
                directoryIdentity.JurisdictionCountryCode,
                directoryIdentity.RegistrationIdentifier,
                directoryIdentity.PublicContactEmail,
                directoryIdentity.LegalNoticeUrl,
                directoryIdentity.TermsUrl!,
                directoryIdentity.PrivacyUrl);
            operatorDisclosure = PaidCheckoutOperatorDisclosure.Create(
                instanceOperatorIdentity.OperatorId, instanceOperatorIdentity.PublicName,
                instanceOperatorIdentity.IsOfficialInstance, instanceOperatorIdentity.OfficialOrigin,
                instanceOperatorIdentity.JurisdictionCountryCode, instanceOperatorIdentity.WebsiteUrl,
                instanceOperatorIdentity.LegalNoticeUrl, instanceOperatorIdentity.TermsUrl,
                instanceOperatorIdentity.PrivacyUrl, instanceOperatorIdentity.PublicContactEmail,
                governance.ComplaintOwner, governance.RefundOwner, governance.DisputeOwner,
                governance.ReconciliationOwner, governance.ActivationStatus,
                instanceOperatorIdentity.LegalName, instanceOperatorIdentity.OperatorKindCode,
                instanceOperatorIdentity.RegistrationIdentifier);
            delivery = PaidOrderDeliverySnapshot.Create(deliveryStart, deliveryEnd, eventTarget.EventTimeZoneId);
            providerDisclosure = PaidCheckoutProviderDisclosure.Create(
                provider.ProviderCode, provider.ProfileCode, governance.ChargeType, governance.StatementDescriptor,
                provider.Environment, provider.CredentialOwner);
        }
        catch (ArgumentException)
        {
            return Failure("payment_acceptance_unavailable", "Complete current payment disclosures are unavailable.");
        }

        string compositionRevision = order.ConcurrencyStamp.ToString("N");
        string revision = Revision(
            compositionRevision,
            catalog.Id.ToString("N"),
            catalog.ConcurrencyStamp.ToString("N"),
            instancePolicy.Id.ToString("N"),
            tenantPolicy?.Id.ToString("N") ?? "none",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            organizerActorId.ToString("N"),
            connection.Id.ToString("N"), connection.ConnectPlatformId,
            connection.ExternalAccountId, merchantCountryCode,
            catalog.MerchantDisclosureText,
            directoryDisclosure.DocumentId.ToString("N"),
            directoryDisclosure.DocumentRevisionId.ToString("N"),
            directoryDisclosure.PublicName, directoryDisclosure.LegalName,
            directoryDisclosure.OperatorKindCode, directoryDisclosure.JurisdictionCountryCode,
            directoryDisclosure.RegistrationIdentifier ?? "none", directoryDisclosure.PublicContactEmail,
            directoryDisclosure.LegalNoticeUrl, directoryDisclosure.TermsUrl, directoryDisclosure.PrivacyUrl,
            operatorDisclosure.OperatorId.ToString("N"), operatorDisclosure.OperatorDisplayName,
            operatorDisclosure.LegalName, operatorDisclosure.OperatorKindCode,
            operatorDisclosure.RegistrationIdentifier ?? "none",
            operatorDisclosure.IsOfficialInstance.ToString(), operatorDisclosure.OfficialOrigin,
            operatorDisclosure.RegionCode, operatorDisclosure.WebsiteUrl, operatorDisclosure.LegalNoticeUrl,
            operatorDisclosure.TermsUrl, operatorDisclosure.PrivacyUrl, operatorDisclosure.ComplaintContact,
            operatorDisclosure.ComplaintOwner, operatorDisclosure.RefundOwner, operatorDisclosure.DisputeOwner,
            operatorDisclosure.ReconciliationOwner, operatorDisclosure.ActivationStatus,
            delivery.StartsAtUtc.ToString("O", CultureInfo.InvariantCulture),
            delivery.EndsAtUtc.ToString("O", CultureInfo.InvariantCulture), delivery.TimeZoneId,
            order.CurrencyCode,
            order.OrganizerDirectedTotalMinorSnapshot.ToString(CultureInfo.InvariantCulture),
            order.PlatformFeeTotalMinorSnapshot.ToString(CultureInfo.InvariantCulture),
            order.PlatformContributionTotalMinorSnapshot.ToString(CultureInfo.InvariantCulture),
            order.TotalDueMinorSnapshot.ToString(CultureInfo.InvariantCulture),
            catalog.RefundPolicyDisclosureText, governance.RefundPolicyLanguageTag,
            catalog.SupportContactDisclosureText,
            providerDisclosure.ProviderCode, providerDisclosure.ProfileCode, providerDisclosure.ChargeType,
            providerDisclosure.StatementDescriptor, providerDisclosure.Environment, providerDisclosure.CredentialOwner,
            string.Join(';', lines.Select(line =>
                $"{line.OrderLineId:N}:{line.Name}:{line.Quantity}:{line.UnitAmountMinor}:{line.DiscountAmountMinor}:{line.LineTotalMinor}")));

        return new(new PaidOrderAcceptanceDisclosureDto
        {
            DisclosureRevision = revision,
            AcceptanceTemplateIdentifier = PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            AcceptanceTemplateText = PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            OrganizerMerchant = new PaidOrderAcceptanceOrganizerMerchantDto
            {
                OrganizerActorId = organizerActorId,
                MerchantDisclosureText = catalog.MerchantDisclosureText,
                ProviderCode = providerDisclosure.ProviderCode,
                ProviderProfileCode = providerDisclosure.ProfileCode,
                ProviderEnvironment = providerDisclosure.Environment,
                ProviderCredentialOwner = providerDisclosure.CredentialOwner,
                ChargeType = providerDisclosure.ChargeType,
                StatementDescriptor = providerDisclosure.StatementDescriptor,
                OrganizerPaymentProviderConnectionId = connection.Id,
                ConnectPlatformId = connection.ConnectPlatformId,
                ExternalAccountId = connection.ExternalAccountId,
                MerchantCountryCode = merchantCountryCode
            },
            TenantDirectoryOperator = new PaidOrderAcceptanceTenantDirectoryOperatorDto
            {
                DocumentId = directoryDisclosure.DocumentId,
                RevisionId = directoryDisclosure.DocumentRevisionId,
                PublicName = directoryDisclosure.PublicName,
                LegalName = directoryDisclosure.LegalName,
                OperatorKindCode = directoryDisclosure.OperatorKindCode,
                JurisdictionCountryCode = directoryDisclosure.JurisdictionCountryCode,
                RegistrationIdentifier = directoryDisclosure.RegistrationIdentifier,
                PublicContactEmail = directoryDisclosure.PublicContactEmail,
                LegalNoticeUrl = directoryDisclosure.LegalNoticeUrl,
                TermsUrl = directoryDisclosure.TermsUrl,
                PrivacyUrl = directoryDisclosure.PrivacyUrl
            },
            InstanceOperator = new PaidOrderAcceptanceInstanceOperatorDto
            {
                OperatorId = operatorDisclosure.OperatorId,
                PublicName = operatorDisclosure.OperatorDisplayName,
                LegalName = operatorDisclosure.LegalName,
                OperatorKindCode = operatorDisclosure.OperatorKindCode,
                RegistrationIdentifier = operatorDisclosure.RegistrationIdentifier,
                IsOfficialInstance = operatorDisclosure.IsOfficialInstance,
                OfficialOrigin = operatorDisclosure.OfficialOrigin,
                JurisdictionCountryCode = operatorDisclosure.RegionCode,
                WebsiteUrl = operatorDisclosure.WebsiteUrl,
                LegalNoticeUrl = operatorDisclosure.LegalNoticeUrl,
                TermsUrl = operatorDisclosure.TermsUrl,
                PrivacyUrl = operatorDisclosure.PrivacyUrl
            },
            PaymentOperations = new PaidOrderAcceptancePaymentOperationsDto
            {
                ComplaintContact = operatorDisclosure.ComplaintContact,
                ComplaintOwner = operatorDisclosure.ComplaintOwner,
                RefundOwner = operatorDisclosure.RefundOwner,
                DisputeOwner = operatorDisclosure.DisputeOwner,
                ReconciliationOwner = operatorDisclosure.ReconciliationOwner,
                ActivationStatus = operatorDisclosure.ActivationStatus
            },
            DeliveryStartsAtUtc = delivery.StartsAtUtc,
            DeliveryEndsAtUtc = delivery.EndsAtUtc,
            EventTimeZoneId = delivery.TimeZoneId,
            CurrencyCode = order.CurrencyCode,
            CurrencyMinorUnitDigits = CurrencyMetadata.Get(order.CurrencyCode).MinorUnitDigits,
            OrganizerAmountMinor = order.OrganizerDirectedTotalMinorSnapshot,
            PlatformFeeMinor = order.PlatformFeeTotalMinorSnapshot,
            PlatformContributionMinor = order.PlatformContributionTotalMinorSnapshot,
            TotalMinor = order.TotalDueMinorSnapshot,
            RefundPolicyVersion = catalog.VersionNumber,
            RefundPolicyText = catalog.RefundPolicyDisclosureText,
            RefundPolicyLanguageTag = governance.RefundPolicyLanguageTag,
            SupportContact = catalog.SupportContactDisclosureText,
            Lines = lines
        }, null, null, null, new(
            organizerActorId,
            instanceOperatorIdentity.OperatorId,
            directoryDisclosure.DocumentId,
            directoryDisclosure.DocumentRevisionId,
            instancePolicy.Id,
            tenantPolicy?.Id));
    }

    public async Task<PaidOrderAcceptanceResult> AcceptAsync(
        RegistrationOrder order,
        PaidOrderAcceptanceAcknowledgementDto? acknowledgement,
        DateTime acceptedAt,
        CancellationToken cancellationToken)
    {
        if (acknowledgement is not { Acknowledged: true } || string.IsNullOrWhiteSpace(acknowledgement.DisclosureRevision))
        {
            return Failure("payment_acceptance_required", "Explicit acknowledgement of the current payment disclosures is required.");
        }

        PaidOrderAcceptanceResult current = await DescribeAsync(order, cancellationToken);
        string suppliedRevision = acknowledgement.DisclosureRevision;
        if (current.Disclosure is not { } disclosure || suppliedRevision.Length != disclosure.DisclosureRevision.Length ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(disclosure.DisclosureRevision),
                Encoding.ASCII.GetBytes(suppliedRevision)))
        {
            return Failure("payment_acceptance_stale", "Payment disclosures changed. Review and acknowledge the current facts.");
        }

        if (current.Authority is not { } authority)
        {
            return Failure("payment_acceptance_stale", "Payment disclosures changed. Review and acknowledge the current facts.");
        }

        PaidOrderAcceptanceOrganizerMerchantDto organizerMerchant = disclosure.OrganizerMerchant;
        PaidOrderAcceptanceTenantDirectoryOperatorDto directoryOperator = disclosure.TenantDirectoryOperator;
        PaidOrderAcceptanceInstanceOperatorDto instanceOperator = disclosure.InstanceOperator;
        PaidOrderAcceptanceSnapshot snapshot = PaidOrderAcceptanceSnapshot.Create(
            Guid.CreateVersion7(), order.TenantId, order.TenantId, order.Id, order.EventId,
            order.ConcurrencyStamp.ToString("N"), disclosure.DisclosureRevision,
            disclosure.AcceptanceTemplateIdentifier, disclosure.AcceptanceTemplateText,
            authority.OrganizerActorId, organizerMerchant.MerchantDisclosureText,
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                authority.TenantDirectoryOperatorDocumentId,
                authority.TenantDirectoryOperatorRevisionId,
                directoryOperator.PublicName,
                directoryOperator.LegalName,
                directoryOperator.OperatorKindCode,
                directoryOperator.JurisdictionCountryCode,
                directoryOperator.RegistrationIdentifier,
                directoryOperator.PublicContactEmail,
                directoryOperator.LegalNoticeUrl,
                directoryOperator.TermsUrl,
                directoryOperator.PrivacyUrl),
            PaidCheckoutOperatorDisclosure.Create(
                authority.OperatorId, instanceOperator.PublicName, instanceOperator.IsOfficialInstance,
                instanceOperator.OfficialOrigin, instanceOperator.JurisdictionCountryCode,
                instanceOperator.WebsiteUrl, instanceOperator.LegalNoticeUrl,
                instanceOperator.TermsUrl, instanceOperator.PrivacyUrl, disclosure.PaymentOperations.ComplaintContact,
                disclosure.PaymentOperations.ComplaintOwner, disclosure.PaymentOperations.RefundOwner,
                disclosure.PaymentOperations.DisputeOwner, disclosure.PaymentOperations.ReconciliationOwner,
                disclosure.PaymentOperations.ActivationStatus, instanceOperator.LegalName,
                instanceOperator.OperatorKindCode, instanceOperator.RegistrationIdentifier),
            PaidOrderDeliverySnapshot.Create(disclosure.DeliveryStartsAtUtc, disclosure.DeliveryEndsAtUtc, disclosure.EventTimeZoneId),
            disclosure.CurrencyCode, disclosure.OrganizerAmountMinor, disclosure.PlatformFeeMinor,
            disclosure.PlatformContributionMinor, disclosure.TotalMinor, authority.InstancePolicyVersionId, disclosure.RefundPolicyVersion,
            disclosure.RefundPolicyText, disclosure.RefundPolicyLanguageTag, disclosure.SupportContact,
            PaidCheckoutProviderDisclosure.Create(
                organizerMerchant.ProviderCode, organizerMerchant.ProviderProfileCode, organizerMerchant.ChargeType,
                organizerMerchant.StatementDescriptor, organizerMerchant.ProviderEnvironment,
                organizerMerchant.ProviderCredentialOwner),
            disclosure.Lines.Select(line => PaidOrderAcceptanceLineFact.Create(
                line.OrderLineId, line.Name, line.Quantity, line.UnitAmountMinor,
                line.DiscountAmountMinor, line.LineTotalMinor)).ToArray(),
            acceptedAt, authority.TenantPolicyVersionId,
            organizerMerchant.OrganizerPaymentProviderConnectionId,
            organizerMerchant.ConnectPlatformId,
            organizerMerchant.ExternalAccountId,
            organizerMerchant.MerchantCountryCode);
        return current with { Snapshot = snapshot };
    }

    private static PaidOrderAcceptanceResult Failure(string code, string message) => new(null, null, code, message);

    private static string Revision(params string[] facts) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', facts))));
}
