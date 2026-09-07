using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ManagedTenantProvisioningOperationConfiguration
    : IEntityTypeConfiguration<ManagedTenantProvisioningOperation>
{
    public void Configure(EntityTypeBuilder<ManagedTenantProvisioningOperation> builder)
    {
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(operation => operation.ExternalRequestId).HasMaxLength(100).IsRequired();
        builder.Property(operation => operation.ExternalCustomerReference).HasMaxLength(200).IsRequired();
        builder.Property(operation => operation.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(operation => operation.RequestJson).HasColumnType("jsonb");
        builder.Property(operation => operation.TenantSlug).HasMaxLength(100).IsRequired();
        builder.Property(operation => operation.CurrentOutboxMessageId).IsRequired();
        builder.Property(operation => operation.CorrelationId).HasMaxLength(100);
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.FailureCode).HasMaxLength(100);
        builder.Property(operation => operation.RowVersion).HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(operation => new { operation.ManagedInstanceId, operation.ExternalRequestId })
            .HasDatabaseName("ux_managed_tenant_provisioning_instance_request")
            .IsUnique();
        builder.HasIndex(operation => new { operation.ManagedInstanceId, operation.ExternalCustomerReference })
            .HasDatabaseName("ux_managed_tenant_provisioning_instance_customer")
            .IsUnique();
        builder.HasIndex(operation => new { operation.Status, operation.CreatedAt });
        builder.HasIndex(operation => operation.TenantId);
        builder.HasIndex(operation => new { operation.TenantId, operation.Id })
            .HasDatabaseName("ux_managed_tenant_provisioning_operations_tenant_id")
            .IsUnique();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_managed_tenant_provisioning_terminal_result",
                "(status = 'Succeeded') = (tenant_id IS NOT NULL AND tenant_administrator_user_id IS NOT NULL AND completed_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_managed_tenant_provisioning_failed",
                "(status = 'Failed') = (failure_code IS NOT NULL AND failed_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_managed_tenant_provisioning_cancelled",
                "(status = 'Cancelled') = (cancelled_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_managed_tenant_provisioning_request_snapshot",
                "(status IN ('Pending', 'Processing')) = (request_json IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_managed_tenant_provisioning_outbox_pointer",
                "current_outbox_message_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
    }
}
