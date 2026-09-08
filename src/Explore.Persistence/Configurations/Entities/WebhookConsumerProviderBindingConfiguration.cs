using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class WebhookConsumerProviderBindingConfiguration
    : IEntityTypeConfiguration<WebhookConsumerProviderBinding>
{
    public void Configure(EntityTypeBuilder<WebhookConsumerProviderBinding> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_webhook_consumer_provider_bindings_concurrency_version_positive",
                "concurrency_version > 0");
            table.HasCheckConstraint(
                "ck_webhook_consumer_provider_bindings_verification_fence_positive",
                "verification_fence > 0");
            table.HasCheckConstraint(
                "ck_webhook_consumer_provider_bindings_capabilities_known",
                "capabilities >= 0 AND capabilities <= 4095");
            table.HasCheckConstraint(
                "ck_webhook_consumer_provider_bindings_governance_capabilities_known",
                "governance_allowed_capabilities >= 0 AND governance_allowed_capabilities <= 4095");
            table.HasCheckConstraint(
                "ck_webhook_consumer_provider_bindings_verified_scope",
                "verification_state_id <> 3 OR " +
                "(verified_tenant_id IS NOT DISTINCT FROM tenant_id AND " +
                "verified_webhook_consumer_id = webhook_consumer_id)");
            table.HasCheckConstraint(
                "ck_webhook_consumer_provider_bindings_configuration_scope",
                "configuration_scope_id = COALESCE(tenant_id, instance_id)");
        });

        builder.HasKey(binding => binding.Id);
        builder.Property(binding => binding.Id).HasDefaultValueSql("uuidv7()");
        var configurationScope = builder.Property(binding => binding.ConfigurationScopeId);
        configurationScope
            .HasComputedColumnSql("COALESCE(tenant_id, instance_id)", stored: true)
            .ValueGeneratedOnAdd()
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);
        configurationScope.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(binding => binding.ProviderKindId).IsRequired();
        builder.Property(binding => binding.ProviderVersion).HasMaxLength(100).IsRequired();
        builder.Property(binding => binding.ProviderEnvironment).HasMaxLength(500).IsRequired();
        builder.Property(binding => binding.NormalizedEnvironment).HasMaxLength(500).IsRequired();
        builder.Property(binding => binding.ApplicationUid).HasMaxLength(500).IsRequired();
        builder.Property(binding => binding.NormalizedApplicationUid).HasMaxLength(500).IsRequired();
        builder.Property(binding => binding.ExternalApplicationId).HasMaxLength(500);
        builder.Property(binding => binding.NormalizedExternalApplicationId).HasMaxLength(500);
        builder.Property(binding => binding.VerificationStateId).IsRequired();
        builder.Property(binding => binding.Capabilities).IsRequired();
        builder.Property(binding => binding.GovernanceAllowedCapabilities).IsRequired();
        builder.Property(binding => binding.CapabilityResolutionVersion).HasMaxLength(100).IsRequired();
        builder.Property(binding => binding.ConcurrencyVersion).IsConcurrencyToken();
        builder.Property(binding => binding.VerificationFence).IsRequired();
        builder.Ignore(binding => binding.ProviderKind);
        builder.Ignore(binding => binding.VerificationState);
        builder.Ignore(binding => binding.EffectiveGovernedCapabilities);

        builder.HasOne(binding => binding.Tenant)
            .WithMany()
            .HasForeignKey(binding => binding.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(binding => binding.WebhookConsumer)
            .WithMany(consumer => consumer.ProviderBindings)
            .HasForeignKey(binding => new
            {
                binding.ConfigurationScopeId,
                binding.WebhookConsumerId
            })
            .HasPrincipalKey(consumer => new
            {
                consumer.ConfigurationScopeId,
                consumer.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(binding => binding.VerificationStateLookup)
            .WithMany()
            .HasForeignKey(binding => binding.VerificationStateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(binding => binding.ProviderKindLookup)
            .WithMany()
            .HasForeignKey(binding => binding.ProviderKindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(binding => new
        {
            binding.WebhookConsumerId,
            binding.ProviderKindId,
            binding.NormalizedEnvironment
        }).IsUnique();

        builder.HasIndex(binding => new
        {
            binding.ProviderKindId,
            binding.NormalizedEnvironment,
            binding.NormalizedExternalApplicationId
        })
            .IsUnique()
            .HasFilter("normalized_external_application_id IS NOT NULL");

        builder.HasIndex(binding => new
        {
            binding.ProviderKindId,
            binding.NormalizedEnvironment,
            binding.NormalizedExternalApplicationId,
            binding.NormalizedApplicationUid
        })
            .IsUnique()
            .HasFilter("normalized_external_application_id IS NOT NULL");

        builder.HasIndex(binding => new
        {
            binding.ProviderKindId,
            binding.NormalizedEnvironment,
            binding.NormalizedApplicationUid
        }).IsUnique();
    }
}
