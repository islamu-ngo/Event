using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_webhook_endpoints_configuration_version",
                "configuration_version > 0");
            table.HasCheckConstraint(
                "ck_webhook_endpoints_configuration_scope",
                "(tenant_id IS NOT NULL AND instance_id IS NULL) OR (tenant_id IS NULL AND instance_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_webhook_endpoints_configuration_scope_key",
                "configuration_scope_id = COALESCE(tenant_id, instance_id)");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.Url).HasMaxLength(2048).IsRequired();
        var configurationScope = builder.Property(e => e.ConfigurationScopeId);
        configurationScope
            .HasComputedColumnSql("COALESCE(tenant_id, instance_id)", stored: true)
            .ValueGeneratedOnAdd()
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);
        configurationScope.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.StatusId).IsRequired();
        builder.Ignore(e => e.Status);
        builder.Property(e => e.SecretRef).HasMaxLength(500).IsRequired();
        builder.Property(e => e.SecretVersion).HasDefaultValue(1);
        builder.Property(e => e.SecretActivatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
        builder.Property(e => e.ConfigurationVersion).HasDefaultValue(1).IsRequired().IsConcurrencyToken();
        builder.Property(e => e.PreviousSecretRef).HasMaxLength(500);
        builder.Property(e => e.ProviderEndpointId).HasMaxLength(500);
        builder.Property(e => e.MaxAttempts).HasDefaultValue(8);
        builder.Property(e => e.TimeoutSeconds).HasDefaultValue(15);
        builder.Property(e => e.ConsecutiveFailureCount).HasDefaultValue(0);
        builder.Property(e => e.AutoPauseReason).HasMaxLength(100);
        builder.Property(e => e.DeliveryStateVersion).HasDefaultValue(0).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Instance)
            .WithMany()
            .HasForeignKey(e => e.InstanceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Consumer)
            .WithMany()
            .HasForeignKey(e => new { e.ConfigurationScopeId, e.ConsumerId })
            .HasPrincipalKey(e => new { e.ConfigurationScopeId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasAlternateKey(e => new { e.ConfigurationScopeId, e.Id });

        builder.HasOne(e => e.StatusLookup)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ConsumerId, e.StatusId });

        builder.HasIndex(e => new { e.InstanceId, e.ConsumerId, e.StatusId })
            .HasFilter("instance_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.StatusId, e.Id });

        builder.HasIndex(e => new { e.TenantId, e.ProviderEndpointId })
            .IsUnique()
            .HasFilter("tenant_id IS NOT NULL AND provider_endpoint_id IS NOT NULL");

        builder.HasIndex(e => new { e.InstanceId, e.ProviderEndpointId })
            .IsUnique()
            .HasFilter("instance_id IS NOT NULL AND provider_endpoint_id IS NOT NULL");
    }
}
