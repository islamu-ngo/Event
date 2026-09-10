using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class TicketPurchasePolicyVersionConfiguration :
    IEntityTypeConfiguration<TicketPurchasePolicyVersion>
{
    public void Configure(
        EntityTypeBuilder<TicketPurchasePolicyVersion> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ticket_purchase_policy_versions_ceilings",
                    "instance_ceiling > 0 AND tenant_ceiling > 0 AND event_ceiling > 0 AND effective_ceiling > 0");
                table.HasCheckConstraint(
                    "ck_ticket_purchase_policy_versions_effective",
                    "effective_ceiling <= instance_ceiling AND effective_ceiling <= tenant_ceiling AND effective_ceiling <= event_ceiling");
            });
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).ValueGeneratedNever();
        builder.Property(policy => policy.ConcurrencyStamp)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.HasIndex(policy => new
        {
            policy.TenantId,
            policy.EventId,
            policy.Id,
        })
            .IsUnique();
        builder.HasIndex(policy => new
        {
            policy.TenantId,
            policy.EventId,
            policy.InstancePolicyVersionId,
            policy.TenantPolicyVersionId,
            policy.EventPolicyVersionId,
        })
            .IsUnique();
    }
}

public sealed class TicketPurchaseAuthorityUsageConfiguration :
    IEntityTypeConfiguration<TicketPurchaseAuthorityUsage>
{
    public void Configure(
        EntityTypeBuilder<TicketPurchaseAuthorityUsage> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ticket_purchase_authority_usages_quantity",
                    "consumed_quantity >= 0");
                table.HasCheckConstraint(
                    "ck_ticket_purchase_authority_usages_mode",
                    "access_mode IN (1, 2, 3)");
            });
        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.Id).ValueGeneratedNever();
        builder.Property(usage => usage.AccessMode).HasConversion<int>();
        builder.Property(usage => usage.EnforcementKey)
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(usage => usage.ConcurrencyStamp)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.HasIndex(usage => new
        {
            usage.TenantId,
            usage.EventId,
            usage.EnforcementKey,
        })
            .IsUnique();
        builder.HasAlternateKey(usage => new
        {
            usage.TenantId,
            usage.EventId,
            usage.Id,
        });
    }
}

public sealed class TicketPurchaseOperationConfiguration :
    IEntityTypeConfiguration<TicketPurchaseOperation>
{
    public void Configure(
        EntityTypeBuilder<TicketPurchaseOperation> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_ticket_purchase_operations_quantities",
                    "requested_quantity > 0 AND effective_ceiling > 0 AND consumed_quantity >= 0");
                table.HasCheckConstraint(
                    "ck_ticket_purchase_operations_disposition",
                    "disposition IN (1, 3)");
            });
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.Disposition)
            .HasConversion<int>();
        builder.Property(operation => operation.KeyHash)
            .HasMaxLength(44)
            .IsRequired();
        builder.Property(operation => operation.FingerprintHash)
            .HasMaxLength(44)
            .IsRequired();
        builder.Property(operation => operation.ConcurrencyStamp)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        builder.HasIndex(operation => new
        {
            operation.TenantId,
            operation.KeyHash,
        })
            .IsUnique();
        builder.HasIndex(operation => new
        {
            operation.TenantId,
            operation.EventId,
            operation.OrderId,
        });
        builder.HasOne<TicketPurchasePolicyVersion>()
            .WithMany()
            .HasForeignKey(operation => new
            {
                operation.TenantId,
                operation.EventId,
                operation.PolicyVersionId,
            })
            .HasPrincipalKey(policy => new
            {
                policy.TenantId,
                policy.EventId,
                policy.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketPurchaseAuthorityUsage>()
            .WithMany()
            .HasForeignKey(operation => new
            {
                operation.TenantId,
                operation.EventId,
                operation.AuthorityUsageId,
            })
            .HasPrincipalKey(usage => new
            {
                usage.TenantId,
                usage.EventId,
                usage.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
