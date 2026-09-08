using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class OrganizerPaymentProviderAccountOperationConfiguration : IEntityTypeConfiguration<OrganizerPaymentProviderAccountOperation>
{
    public void Configure(EntityTypeBuilder<OrganizerPaymentProviderAccountOperation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_organizer_payment_provider_account_operations_status", "status_id BETWEEN 1 AND 5");
        });

        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.ProviderCode).IsRequired().HasMaxLength(40);
        builder.Property(operation => operation.ConnectPlatformId).IsRequired().HasMaxLength(120);
        builder.Property(operation => operation.ProviderIdempotencyKey).IsRequired().HasMaxLength(80);
        builder.Property(operation => operation.ActiveScopeKey).IsRequired().HasMaxLength(232);
        builder.Property(operation => operation.ActiveUniquenessSlot).IsRequired().HasMaxLength(80);
        builder.Property(operation => operation.ExternalAccountId).HasMaxLength(200);
        builder.Property(operation => operation.FailureCode).HasMaxLength(120);
        builder.Property(operation => operation.ProviderRequestId).HasMaxLength(120);
        builder.Property(operation => operation.ResolutionReason).HasMaxLength(160);
        builder.Property(operation => operation.RequestedAt).IsRequired();
        builder.Property(operation => operation.CreatedAt).IsRequired();
        builder.Property(operation => operation.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasAlternateKey(operation => new { operation.TenantId, operation.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(operation => operation.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(operation => operation.OrganizerActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrganizerPaymentProviderConnection>()
            .WithMany()
            .HasForeignKey(nameof(OrganizerPaymentProviderAccountOperation.TenantId), nameof(OrganizerPaymentProviderAccountOperation.ConnectionId))
            .HasPrincipalKey(nameof(OrganizerPaymentProviderConnection.TenantId), nameof(OrganizerPaymentProviderConnection.Id))
            .HasConstraintName("fk_organizer_payment_account_operations_connection")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(operation => new { operation.ActiveScopeKey, operation.ActiveUniquenessSlot }).IsUnique();
        builder.HasIndex(operation => operation.ProviderIdempotencyKey).IsUnique();
        builder.HasIndex(operation => new { operation.TenantId, operation.OrganizerActorId, operation.ProviderCode, operation.ConnectPlatformId, operation.StatusId });
    }
}
