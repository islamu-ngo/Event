using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class WebhookEndpointSubscriptionConfiguration : IEntityTypeConfiguration<WebhookEndpointSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookEndpointSubscription> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_webhook_endpoint_subscriptions_configuration_scope",
                "(tenant_id IS NOT NULL AND instance_id IS NULL) OR (tenant_id IS NULL AND instance_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_webhook_endpoint_subscriptions_configuration_scope_key",
                "configuration_scope_id = COALESCE(tenant_id, instance_id)");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        var configurationScope = builder.Property(e => e.ConfigurationScopeId);
        configurationScope
            .HasComputedColumnSql("COALESCE(tenant_id, instance_id)", stored: true)
            .ValueGeneratedOnAdd()
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);
        configurationScope.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(e => e.IsEnabled).HasDefaultValue(true);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Instance)
            .WithMany()
            .HasForeignKey(e => e.InstanceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Endpoint)
            .WithMany(e => e.Subscriptions)
            .HasForeignKey(e => new { e.ConfigurationScopeId, e.EndpointId })
            .HasPrincipalKey(e => new { e.ConfigurationScopeId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventType)
            .WithMany()
            .HasForeignKey(e => e.EventTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EndpointId, e.EventTypeId })
            .IsUnique()
            .HasFilter("tenant_id IS NOT NULL");

        builder.HasIndex(e => new { e.InstanceId, e.EndpointId, e.EventTypeId })
            .IsUnique()
            .HasFilter("instance_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.EventTypeId, e.IsEnabled });

        builder.HasIndex(e => new { e.InstanceId, e.EventTypeId, e.IsEnabled })
            .HasFilter("instance_id IS NOT NULL");
    }
}
