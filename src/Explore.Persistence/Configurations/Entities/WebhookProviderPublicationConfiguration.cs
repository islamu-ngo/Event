using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class WebhookProviderPublicationConfiguration
    : IEntityTypeConfiguration<WebhookProviderPublication>
{
    public void Configure(EntityTypeBuilder<WebhookProviderPublication> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_webhook_provider_publications_fence", "publication_fence >= 0");
            table.HasCheckConstraint("ck_webhook_provider_publications_concurrency_version", "concurrency_version > 0");
            table.HasCheckConstraint("ck_webhook_provider_publications_request_hash", "request_hash ~ '^sha256:[0-9a-f]{64}$'");
        });

        builder.Property(publication => publication.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(publication => publication.ProviderKindId).IsRequired();
        builder.Property(publication => publication.ModeSnapshotId).IsRequired();
        builder.Property(publication => publication.StatusId).IsRequired();
        builder.Property(publication => publication.ProviderVersion).HasMaxLength(WebhookProviderPublication.MaxVersionLength).IsRequired();
        builder.Property(publication => publication.ProviderEventId).HasMaxLength(WebhookProviderPublication.MaxIdentityLength).IsRequired();
        builder.Property(publication => publication.IdempotencyKey).HasMaxLength(WebhookProviderPublication.MaxIdentityLength).IsRequired();
        builder.Property(publication => publication.RequestHash).HasMaxLength(71).IsRequired();
        builder.Property(publication => publication.ApplicationUid).HasMaxLength(WebhookProviderPublication.MaxIdentityLength).IsRequired();
        builder.Property(publication => publication.ProviderApplicationId).HasMaxLength(WebhookProviderPublication.MaxProviderApplicationIdLength);
        builder.Property(publication => publication.ProviderEnvironment).HasMaxLength(WebhookProviderPublication.MaxIdentityLength).IsRequired();
        builder.Property(publication => publication.CredentialReference).HasMaxLength(WebhookProviderPublication.MaxCredentialReferenceLength).IsRequired();
        builder.Property(publication => publication.CredentialVersion).HasMaxLength(WebhookProviderPublication.MaxVersionLength).IsRequired();
        builder.Property(publication => publication.ProviderConfigurationVersion).HasMaxLength(WebhookProviderPublication.MaxVersionLength).IsRequired();
        builder.Property(publication => publication.RetentionPolicyVersion).HasMaxLength(WebhookProviderPublication.MaxVersionLength).IsRequired();
        builder.Property(publication => publication.ExternalProviderMessageId).HasMaxLength(WebhookProviderPublication.MaxExternalProviderMessageIdLength);
        builder.Property(publication => publication.FailureCategory).HasMaxLength(WebhookProviderPublication.MaxFailureCategoryLength);
        builder.Property(publication => publication.SafeDetail).HasMaxLength(WebhookProviderPublication.MaxSafeDetailLength);
        builder.Property(publication => publication.ProcessingLeaseOwner).HasMaxLength(WebhookProviderPublication.MaxLeaseOwnerLength);
        builder.Property(publication => publication.PublicationFence).IsRequired();
        builder.Property(publication => publication.ConcurrencyVersion).IsRequired().IsConcurrencyToken();
        builder.Ignore(publication => publication.ProviderKind);
        builder.Ignore(publication => publication.ModeSnapshot);
        builder.Ignore(publication => publication.Status);

        builder.HasAlternateKey(publication => new { publication.TenantId, publication.Id });
        builder.HasOne(publication => publication.Tenant)
            .WithMany()
            .HasForeignKey(publication => publication.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(publication => publication.WebhookMessage)
            .WithMany()
            .HasPrincipalKey(message => new { message.TenantId, message.Id })
            .HasForeignKey(publication => new { publication.TenantId, publication.WebhookMessageId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(publication => publication.WebhookDeliveryPlanSnapshot)
            .WithMany()
            .HasPrincipalKey(plan => new { plan.TenantId, plan.Id })
            .HasForeignKey(publication => new { publication.TenantId, publication.WebhookDeliveryPlanSnapshotId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(publication => publication.ProviderBinding)
            .WithMany()
            .HasForeignKey(publication => publication.ProviderBindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(publication => publication.ProviderKindLookup)
            .WithMany()
            .HasForeignKey(publication => publication.ProviderKindId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(publication => publication.ModeSnapshotLookup)
            .WithMany()
            .HasForeignKey(publication => publication.ModeSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(publication => publication.StatusLookup)
            .WithMany()
            .HasForeignKey(publication => publication.StatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(publication => publication.Attempts)
            .WithOne(attempt => attempt.WebhookProviderPublication)
            .HasPrincipalKey(publication => new { publication.TenantId, publication.Id })
            .HasForeignKey(attempt => new { attempt.TenantId, attempt.WebhookProviderPublicationId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(publication => new { publication.TenantId, publication.WebhookMessageId, publication.ProviderKindId, publication.ProviderBindingId })
            .IsUnique();
        builder.HasIndex(publication => new { publication.TenantId, publication.ProviderKindId, publication.ProviderEventId })
            .IsUnique();
        builder.HasIndex(publication => new { publication.TenantId, publication.StatusId, publication.NextActionAt, publication.ProcessingLeaseExpiresAt });
        builder.HasIndex(publication => new { publication.TenantId, publication.PublicationRetentionUntil });
    }
}

public sealed class WebhookProviderPublicationAttemptConfiguration
    : IEntityTypeConfiguration<WebhookProviderPublicationAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookProviderPublicationAttempt> builder)
    {
        builder.Property(attempt => attempt.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(attempt => attempt.ExternalProviderMessageId).HasMaxLength(WebhookProviderPublication.MaxExternalProviderMessageIdLength);
        builder.Property(attempt => attempt.FailureCategory).HasMaxLength(WebhookProviderPublication.MaxFailureCategoryLength);
        builder.Property(attempt => attempt.SafeDetail).HasMaxLength(WebhookProviderPublication.MaxSafeDetailLength);
        builder.HasOne(attempt => attempt.OutcomeLookup)
            .WithMany()
            .HasForeignKey(attempt => attempt.OutcomeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(attempt => new { attempt.TenantId, attempt.Id });
        builder.HasOne(attempt => attempt.Tenant)
            .WithMany()
            .HasForeignKey(attempt => attempt.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.WebhookProviderPublicationId, attempt.AttemptNumber })
            .IsUnique();
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.RecordedAt, attempt.Id });
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.OutcomeId, attempt.RecordedAt });
    }
}
