using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class OrganizerPaymentProviderConnectionConfiguration : IEntityTypeConfiguration<OrganizerPaymentProviderConnection>
{
    public void Configure(EntityTypeBuilder<OrganizerPaymentProviderConnection> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_organizer_payment_provider_connections_status", "status_id BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_organizer_payment_provider_connections_charge_capability", "charge_capability_state_id BETWEEN 0 AND 3");
            table.HasCheckConstraint("ck_organizer_payment_provider_connections_requirements", "requirements_state_id BETWEEN 0 AND 4");
        });

        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.ProviderCode).IsRequired().HasMaxLength(40);
        builder.Property(connection => connection.ConnectPlatformId).IsRequired().HasMaxLength(120);
        builder.Property(connection => connection.ExternalAccountId).IsRequired().HasMaxLength(200);
        builder.Property(connection => connection.ActiveScopeKey).IsRequired().HasMaxLength(232);
        builder.Property(connection => connection.ActiveUniquenessSlot).IsRequired().HasMaxLength(48);
        builder.Property(connection => connection.MerchantCountryCode).HasMaxLength(2);
        builder.Property(connection => connection.LastReadinessEvidenceRevision).HasMaxLength(120);
        builder.Property(connection => connection.DisabledReasonCode).HasMaxLength(80);
        builder.Property(connection => connection.CreatedAt).IsRequired();
        builder.Property(connection => connection.IsDeleted).HasDefaultValue(false);
        builder.Property(connection => connection.ConcurrencyStamp).IsConcurrencyToken();

        builder.Ignore(connection => connection.SupportedCurrencyCodes);
        builder.HasAlternateKey(connection => new { connection.TenantId, connection.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(connection => connection.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Actor>().WithMany().HasForeignKey(connection => connection.OrganizerActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrganizerPaymentProviderConnection>()
            .WithMany()
            .HasForeignKey(nameof(OrganizerPaymentProviderConnection.TenantId), nameof(OrganizerPaymentProviderConnection.ReplacesConnectionId))
            .HasPrincipalKey(nameof(OrganizerPaymentProviderConnection.TenantId), nameof(OrganizerPaymentProviderConnection.Id))
            .HasConstraintName("fk_organizer_payment_connections_replaces")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrganizerPaymentProviderConnection>()
            .WithMany()
            .HasForeignKey(nameof(OrganizerPaymentProviderConnection.TenantId), nameof(OrganizerPaymentProviderConnection.ReplacedByConnectionId))
            .HasPrincipalKey(nameof(OrganizerPaymentProviderConnection.TenantId), nameof(OrganizerPaymentProviderConnection.Id))
            .HasConstraintName("fk_organizer_payment_connections_replaced_by")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<OrganizerPaymentProviderConnectionSupportedCurrency>("SupportedCurrencyRows")
            .WithOne(row => row.Connection)
            .HasForeignKey(row => new { row.TenantId, row.OrganizerPaymentProviderConnectionId })
            .HasPrincipalKey(connection => new { connection.TenantId, connection.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation("SupportedCurrencyRows")
            .HasField("_supportedCurrencyCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.HasIndex(connection => new { connection.ActiveScopeKey, connection.ActiveUniquenessSlot }).IsUnique();
        builder.HasIndex(connection => new { connection.TenantId, connection.OrganizerActorId, connection.ProviderCode, connection.ConnectPlatformId, connection.StatusId });
        builder.HasIndex(connection => new { connection.ProviderCode, connection.ConnectPlatformId, connection.ExternalAccountId }).IsUnique();
    }
}

public sealed class OrganizerPaymentProviderConnectionSupportedCurrencyConfiguration : IEntityTypeConfiguration<OrganizerPaymentProviderConnectionSupportedCurrency>
{
    public void Configure(EntityTypeBuilder<OrganizerPaymentProviderConnectionSupportedCurrency> builder)
    {
        builder.HasKey(row => new { row.TenantId, row.OrganizerPaymentProviderConnectionId, row.Ordinal });
        builder.Property(row => row.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.HasIndex(row => new { row.TenantId, row.OrganizerPaymentProviderConnectionId, row.CurrencyCode }).IsUnique();
    }
}
