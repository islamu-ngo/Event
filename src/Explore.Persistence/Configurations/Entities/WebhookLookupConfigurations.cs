using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public abstract class WebhookLookupConfiguration<TLookup>
    : IEntityTypeConfiguration<TLookup>
    where TLookup : class
{
    public void Configure(EntityTypeBuilder<TLookup> builder)
    {
        builder.Property<int>("Id").ValueGeneratedNever();
        builder.Property<string>("MasterCode").HasMaxLength(100).IsRequired();
        builder.Property<string>("FullName").HasMaxLength(200).IsRequired();
        builder.Property<string?>("Description").HasMaxLength(500);
        builder.HasIndex("MasterCode").IsUnique();
    }
}

public sealed class WebhookConsumerKindLookupConfiguration
    : WebhookLookupConfiguration<WebhookConsumerKindLookup>;

public sealed class WebhookConsumerStatusLookupConfiguration
    : WebhookLookupConfiguration<WebhookConsumerStatusLookup>;

public sealed class WebhookProviderModeLookupConfiguration
    : WebhookLookupConfiguration<WebhookProviderModeLookup>;

public sealed class WebhookProviderKindLookupConfiguration
    : WebhookLookupConfiguration<WebhookProviderKindLookup>;

public sealed class WebhookProviderCapabilityLookupConfiguration
    : WebhookLookupConfiguration<WebhookProviderCapabilityLookup>;

public sealed class WebhookEndpointStatusLookupConfiguration
    : WebhookLookupConfiguration<WebhookEndpointStatusLookup>;

public sealed class WebhookLocalDeliveryStatusLookupConfiguration
    : WebhookLookupConfiguration<WebhookLocalDeliveryStatusLookup>;

public sealed class WebhookBulkReplayStatusLookupConfiguration
    : WebhookLookupConfiguration<WebhookBulkReplayStatusLookup>;

public sealed class WebhookPendingWorkDecisionLookupConfiguration
    : WebhookLookupConfiguration<WebhookPendingWorkDecisionLookup>;

public sealed class WebhookRetentionSubjectKindLookupConfiguration
    : WebhookLookupConfiguration<WebhookRetentionSubjectKindLookup>;

public sealed class WebhookAuditActionLookupConfiguration
    : WebhookLookupConfiguration<WebhookAuditActionLookup>;

public sealed class WebhookAuditOutcomeLookupConfiguration
    : WebhookLookupConfiguration<WebhookAuditOutcomeLookup>;

public sealed class WebhookAuditPrincipalKindLookupConfiguration
    : WebhookLookupConfiguration<WebhookAuditPrincipalKindLookup>;

public sealed class WebhookAuditScopeKindLookupConfiguration
    : WebhookLookupConfiguration<WebhookAuditScopeKindLookup>;

public sealed class WebhookAuditTargetKindLookupConfiguration
    : WebhookLookupConfiguration<WebhookAuditTargetKindLookup>;

public sealed class WebhookDeliveryAttemptOutcomeLookupConfiguration
    : WebhookLookupConfiguration<WebhookDeliveryAttemptOutcomeLookup>;

public sealed class IncomingWebhookMessageStatusLookupConfiguration
    : WebhookLookupConfiguration<IncomingWebhookMessageStatusLookup>;

public sealed class IncomingWebhookProcessingAttemptOutcomeLookupConfiguration
    : WebhookLookupConfiguration<IncomingWebhookProcessingAttemptOutcomeLookup>;

public sealed class IncomingWebhookSettlementSourceLookupConfiguration
    : WebhookLookupConfiguration<IncomingWebhookSettlementSourceLookup>;

public sealed class IncomingWebhookRedriveResultLookupConfiguration
    : WebhookLookupConfiguration<IncomingWebhookRedriveResultLookup>;

public sealed class WebhookProviderPublicationStatusLookupConfiguration
    : WebhookLookupConfiguration<WebhookProviderPublicationStatusLookup>;

public sealed class WebhookProviderPublicationAttemptOutcomeLookupConfiguration
    : WebhookLookupConfiguration<WebhookProviderPublicationAttemptOutcomeLookup>;

public sealed class WebhookPayloadProvenanceLookupConfiguration
    : WebhookLookupConfiguration<WebhookPayloadProvenanceLookup>;
