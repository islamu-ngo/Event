using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PromotionDefinitionConfiguration : IEntityTypeConfiguration<PromotionDefinition>
{
    public void Configure(EntityTypeBuilder<PromotionDefinition> builder)
    {
        builder.Property(definition => definition.Id).ValueGeneratedNever();
        builder.Property(definition => definition.DisplayLabel).IsRequired().HasMaxLength(200);
        builder.Property(definition => definition.ScopeMetadata)
            .HasConversion(scope => PromotionConverters.ScopeToString(scope), value => PromotionConverters.ScopeFromString(value))
            .Metadata.SetValueComparer(PromotionConverters.ScopeComparer);
        builder.Property(definition => definition.Eligibility)
            .HasConversion(eligibility => PromotionConverters.EligibilityToString(eligibility), value => PromotionConverters.EligibilityFromString(value))
            .Metadata.SetValueComparer(PromotionConverters.EligibilityComparer);
        builder.Property(definition => definition.DiscountRule)
            .HasConversion(rule => PromotionConverters.DiscountToString(rule), value => PromotionConverters.DiscountFromString(value))
            .Metadata.SetValueComparer(PromotionConverters.DiscountComparer);
        builder.Property<Guid>("ScopeEventId").IsRequired();
        builder.Property<Guid>("ScopeTicketCatalogVersionId").IsRequired();
        builder.Property<int>("ScopeTicketCatalogVersionNumber").IsRequired();
        builder.Property<string>("ScopeCurrencyCode").IsRequired().HasMaxLength(3);
        builder.Property(definition => definition.CreatedAt).IsRequired();
        builder.HasAlternateKey(definition => new { definition.TenantId, definition.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(definition => definition.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromotionDefinitionStatus>().WithMany().HasForeignKey(definition => definition.PromotionDefinitionStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(definition => new { definition.TenantId, definition.DefinitionGroupId, definition.VersionNumber }).IsUnique();
        builder.HasIndex("TenantId", "ScopeEventId", "ScopeTicketCatalogVersionId", nameof(PromotionDefinition.PromotionDefinitionStatusId));
    }
}

public sealed class PromotionCodeConfiguration : IEntityTypeConfiguration<PromotionCode>
{
    public void Configure(EntityTypeBuilder<PromotionCode> builder)
    {
        builder.Property(code => code.Id).ValueGeneratedNever();
        builder.Property(code => code.DisplayLabel).IsRequired().HasMaxLength(16);
        builder.Property(code => code.ScopeMetadata)
            .HasConversion(scope => PromotionConverters.ScopeToString(scope), value => PromotionConverters.ScopeFromString(value))
            .Metadata.SetValueComparer(PromotionConverters.ScopeComparer);
        builder.Property<Guid>("ScopeEventId").IsRequired();
        builder.Property<Guid>("ScopeTicketCatalogVersionId").IsRequired();
        builder.Property<int>("ScopeTicketCatalogVersionNumber").IsRequired();
        builder.Property<string>("ScopeCurrencyCode").IsRequired().HasMaxLength(3);
        builder.Property<int>("LookupKeyVersion").IsRequired();
        builder.Property<string>("LookupDigest").IsRequired().HasMaxLength(128);
        builder.Property<bool>("IsActive").HasDefaultValue(true);
        builder.Property<DateTime?>("RetiredAtUtc");
        builder.Property(code => code.CreatedAt).IsRequired();
        builder.HasAlternateKey(code => new { code.TenantId, code.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(code => code.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromotionDefinition>().WithMany().HasForeignKey(code => new { code.TenantId, code.PromotionDefinitionVersionId })
            .HasPrincipalKey(definition => new { definition.TenantId, definition.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex("TenantId", "ScopeEventId", "ScopeTicketCatalogVersionId", "LookupKeyVersion");
        builder.HasIndex("TenantId", "ScopeEventId", "ScopeTicketCatalogVersionId", "LookupKeyVersion", "LookupDigest").IsUnique();
        builder.HasIndex("TenantId", nameof(PromotionCode.PromotionDefinitionVersionId), "IsActive");
    }
}

public sealed class PromotionReservationConfiguration : IEntityTypeConfiguration<PromotionReservation>
{
    public void Configure(EntityTypeBuilder<PromotionReservation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_promotion_reservation_active_slot", "(promotion_reservation_status_id = 1 AND order_reservation_slot = '00000000-0000-0000-0000-000000000000') OR (promotion_reservation_status_id <> 1 AND order_reservation_slot = id)");
            table.HasCheckConstraint("ck_promotion_reservation_status_timestamps", "(promotion_reservation_status_id = 1 AND consumed_at_utc IS NULL AND released_at_utc IS NULL AND expired_at_utc IS NULL) OR (promotion_reservation_status_id = 2 AND consumed_at_utc IS NOT NULL AND released_at_utc IS NULL AND expired_at_utc IS NULL) OR (promotion_reservation_status_id = 3 AND consumed_at_utc IS NULL AND released_at_utc IS NOT NULL AND expired_at_utc IS NULL) OR (promotion_reservation_status_id = 4 AND consumed_at_utc IS NULL AND released_at_utc IS NULL AND expired_at_utc IS NOT NULL)");
        });
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();
        builder.Property(reservation => reservation.OrderReservationSlot).IsRequired();
        builder.Property(reservation => reservation.ReservedAtUtc).IsRequired();
        builder.Property(reservation => reservation.CreatedAt).IsRequired();
        builder.Property(reservation => reservation.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(reservation => new { reservation.TenantId, reservation.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(reservation => reservation.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany().HasForeignKey(reservation => new { reservation.TenantId, reservation.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromotionDefinition>().WithMany().HasForeignKey(reservation => new { reservation.TenantId, reservation.PromotionDefinitionVersionId })
            .HasPrincipalKey(definition => new { definition.TenantId, definition.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromotionCode>().WithMany().HasForeignKey(reservation => new { reservation.TenantId, reservation.PromotionCodeId })
            .HasPrincipalKey(code => new { code.TenantId, code.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromotionReservationStatus>().WithMany().HasForeignKey(reservation => reservation.PromotionReservationStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(reservation => new { reservation.RegistrationOrderId, reservation.OrderReservationSlot }).IsUnique();
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.PromotionDefinitionVersionId, reservation.PromotionReservationStatusId });
    }
}

public sealed class PromotionDefinitionStatusConfiguration : IEntityTypeConfiguration<PromotionDefinitionStatus>
{
    public void Configure(EntityTypeBuilder<PromotionDefinitionStatus> builder) => PromotionLookupConfiguration.Configure(builder, "promotion_definition_statuses");
}

public sealed class PromotionReservationStatusConfiguration : IEntityTypeConfiguration<PromotionReservationStatus>
{
    public void Configure(EntityTypeBuilder<PromotionReservationStatus> builder) => PromotionLookupConfiguration.Configure(builder, "promotion_reservation_statuses");
}

file static class PromotionLookupConfiguration
{
    public static void Configure<TLookup>(EntityTypeBuilder<TLookup> builder, string tableName) where TLookup : class
    {
        builder.ToTable(tableName);
        builder.HasKey("Id");
        builder.Property<int>("Id").ValueGeneratedNever();
        builder.Property<string>("MasterCode").IsRequired().HasMaxLength(100);
        builder.Property<string>("FullName").IsRequired().HasMaxLength(200);
        builder.Property<string>("Description").HasMaxLength(500);
        builder.HasIndex("MasterCode").IsUnique();
    }
}

file static class PromotionConverters
{
    private const char Separator = '|';
    private const string AllTickets = "*";
    private const string Fixed = "fixed";
    private const string BasisPoints = "basis_points";

    public static readonly ValueComparer<PromotionScopeMetadata> ScopeComparer = new(
        (left, right) => left == right,
        value => value.GetHashCode(),
        value => PromotionScopeMetadata.Create(value.TenantId, value.EventId, value.TicketCatalogVersionId, value.TicketCatalogVersionNumber, value.CurrencyCode));

    public static readonly ValueComparer<PromotionEligibility> EligibilityComparer = new(
        (left, right) => EligibilityToString(left!) == EligibilityToString(right!),
        value => EligibilityToString(value).GetHashCode(StringComparison.Ordinal),
        value => EligibilityFromString(EligibilityToString(value)));

    public static readonly ValueComparer<PromotionDiscountRule> DiscountComparer = new(
        (left, right) => DiscountToString(left!) == DiscountToString(right!),
        value => DiscountToString(value).GetHashCode(StringComparison.Ordinal),
        value => DiscountFromString(DiscountToString(value)));

    public static string ScopeToString(PromotionScopeMetadata scope) => string.Join(Separator, scope.TenantId, scope.EventId, scope.TicketCatalogVersionId, scope.TicketCatalogVersionNumber, scope.CurrencyCode);

    public static PromotionScopeMetadata ScopeFromString(string value)
    {
        string[] parts = value.Split(Separator);
        return PromotionScopeMetadata.Create(Guid.Parse(parts[0]), Guid.Parse(parts[1]), Guid.Parse(parts[2]), int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), parts[4]);
    }

    public static string EligibilityToString(PromotionEligibility eligibility) => eligibility.IncludesAllTickets
        ? AllTickets
        : string.Join(',', eligibility.EligibleTicketTypeIds.Order());

    public static PromotionEligibility EligibilityFromString(string value) => value == AllTickets
        ? PromotionEligibility.AllTickets()
        : PromotionEligibility.ForTicketTypes(value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse));

    public static string DiscountToString(PromotionDiscountRule rule) => rule.FixedDiscountMinor.HasValue
        ? string.Join(Separator, Fixed, rule.CurrencyCode, rule.FixedDiscountMinor.Value, rule.MaximumDiscountMinor?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
        : string.Join(Separator, BasisPoints, rule.CurrencyCode, rule.BasisPointDiscount!.Value, rule.MaximumDiscountMinor?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);

    public static PromotionDiscountRule DiscountFromString(string value)
    {
        string[] parts = value.Split(Separator);
        long? maximum = string.IsNullOrEmpty(parts[3]) ? null : long.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
        return parts[0] == Fixed
            ? PromotionDiscountRule.FixedMinor(parts[1], long.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), maximum)
            : PromotionDiscountRule.BasisPoints(parts[1], int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), maximum);
    }
}
