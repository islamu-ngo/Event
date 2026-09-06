// ABOUTME: Locks semantic Money, GeoCoordinate, and range owners to their existing scalar EF leaves.
// ABOUTME: Requires four portable value checks without changing provider storage, indexes, tenancy, or privacy metadata.

using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class SemanticValueScalarPersistenceModelTests
{
    private static readonly Type[] SemanticValueTypes =
        [typeof(Money), typeof(GeoCoordinate), typeof(LocalDateRange), typeof(UtcInstantRange)];

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderModelsKeepSemanticValueOwnersOnDirectScalarLeaves(PrimaryDatabaseProvider provider)
    {
        using var context = CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType ticket = RequiredEntity<EventTicketType>(model);
        IEntityType payment = RequiredEntity<PaymentAttempt>(model);
        IEntityType locationPii = RequiredEntity<LocationPii>(model);
        IEntityType agenda = RequiredEntity<EventAgendaItem>(model);
        IEntityType session = RequiredEntity<EventSession>(model);
        string prefix = provider is PrimaryDatabaseProvider.PostgreSql or PrimaryDatabaseProvider.SqlServer ? "" : "ie_";

        await Assert.That(ticket.GetTableName()).IsEqualTo(prefix + "event_ticket_types");
        await Assert.That(payment.GetTableName()).IsEqualTo(prefix + "payment_attempts");
        await Assert.That(locationPii.GetTableName()).IsEqualTo(prefix + "location_pii");
        await Assert.That(agenda.GetTableName()).IsEqualTo(prefix + "event_agenda_items");
        await Assert.That(session.GetTableName()).IsEqualTo(prefix + "event_sessions");

        string currencyType = provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => "character varying(3)",
            PrimaryDatabaseProvider.Sqlite => "TEXT",
            PrimaryDatabaseProvider.SqlServer => "nvarchar(3)",
            PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => "varchar(3)",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        string paymentMinorType = provider == PrimaryDatabaseProvider.Sqlite ? "INTEGER" : "bigint";
        string coordinateType = provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => "double precision",
            PrimaryDatabaseProvider.Sqlite => "REAL",
            PrimaryDatabaseProvider.SqlServer => "float",
            PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => "double",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        string localDateType = provider == PrimaryDatabaseProvider.Sqlite ? "TEXT" : "date";
        string instantType = provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => "timestamp with time zone",
            PrimaryDatabaseProvider.Sqlite => "TEXT",
            PrimaryDatabaseProvider.SqlServer => "datetimeoffset",
            PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => "datetime(6)",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        await AssertLeaf(ticket, nameof(EventTicketType.CurrencyCode), typeof(string), "currency_code", currencyType, false);
        await AssertLeaf(ticket, nameof(EventTicketType.FixedPriceMinor), typeof(long?), "fixed_price_minor", "bigint", true);
        await AssertLeaf(ticket, nameof(EventTicketType.MinimumPriceMinor), typeof(long?), "minimum_price_minor", "bigint", true);
        await AssertLeaf(ticket, nameof(EventTicketType.SuggestedPriceMinor), typeof(long?), "suggested_price_minor", "bigint", true);
        await AssertLeaf(payment, nameof(PaymentAttempt.CurrencyCode), typeof(string), "currency_code", currencyType, false);
        await AssertLeaf(payment, nameof(PaymentAttempt.OrganizerAmountMinor), typeof(long), "organizer_amount_minor", paymentMinorType, false);
        await AssertLeaf(payment, nameof(PaymentAttempt.PlatformFeeMinor), typeof(long), "platform_fee_minor", paymentMinorType, false);
        await AssertLeaf(payment, nameof(PaymentAttempt.PlatformContributionMinor), typeof(long), "platform_contribution_minor", paymentMinorType, false);
        await AssertLeaf(payment, nameof(PaymentAttempt.TotalMinor), typeof(long), "total_minor", paymentMinorType, false);
        await AssertLeaf(locationPii, nameof(LocationPii.Latitude), typeof(double?), "latitude", coordinateType, true);
        await AssertLeaf(locationPii, nameof(LocationPii.Longitude), typeof(double?), "longitude", coordinateType, true);
        await AssertTemporalLeaves(agenda, false, instantType, localDateType);
        await AssertTemporalLeaves(session, true, instantType, localDateType);

        await Assert.That(SemanticValueTypes.All(type => model.FindEntityType(type) is null)).IsTrue();
        await Assert.That(model.GetEntityTypes().Any(type => type.IsOwned() && SemanticValueTypes.Contains(type.ClrType))).IsFalse();
        await Assert.That(model.GetEntityTypes().SelectMany(type => type.GetComplexProperties())
            .Any(property => SemanticValueTypes.Contains(property.ComplexType.ClrType))).IsFalse();
        await Assert.That(model.GetEntityTypes().SelectMany(type => type.GetProperties()).Any(property =>
            SemanticValueTypes.Contains(property.ClrType) ||
            property.GetValueConverter() is { } converter &&
            (SemanticValueTypes.Contains(converter.ModelClrType) || SemanticValueTypes.Contains(converter.ProviderClrType)))).IsFalse();

        await AssertOwnerMetadata(ticket, payment, locationPii, agenda, session);
        await AssertPaymentCheck(payment);

        if (provider != PrimaryDatabaseProvider.PostgreSql)
        {
            string[] postgreSqlTokens =
                ["::", "btrim(", "jsonb_", "num_nonnulls(", "octet_length(", "extract(", "tstzrange(", "daterange(", "infinity", "interval ", "~"];
            string[] sql = model.GetEntityTypes().SelectMany(type => type.GetCheckConstraints()).Select(check => check.Sql).ToArray();
            await Assert.That(sql.Any(text => postgreSqlTokens.Any(token =>
                text.Contains(token, StringComparison.OrdinalIgnoreCase)))).IsFalse();
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderModelsRequireNonnegativeTicketMoney(PrimaryDatabaseProvider provider)
    {
        using var context = CreateContext(provider);
        await AssertConstraint(RequiredEntity<EventTicketType>(context.GetService<IDesignTimeModel>().Model),
            "ck_event_ticket_type_money_nonnegative",
            "fixed_price_minor >= 0", "minimum_price_minor >= 0", "suggested_price_minor >= 0");
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderModelsRequireCompleteBoundedCoordinates(PrimaryDatabaseProvider provider)
    {
        using var context = CreateContext(provider);
        await AssertConstraint(RequiredEntity<LocationPii>(context.GetService<IDesignTimeModel>().Model),
            "ck_location_pii_coordinate_shape",
            "latitude IS NULL AND longitude IS NULL", "latitude BETWEEN -90 AND 90", "longitude BETWEEN -180 AND 180");
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderModelsRequireOrderedAgendaLocalDates(PrimaryDatabaseProvider provider)
    {
        using var context = CreateContext(provider);
        await AssertConstraint(RequiredEntity<EventAgendaItem>(context.GetService<IDesignTimeModel>().Model),
            "ck_event_agenda_item_local_date_range", "local_end_date >= local_start_date");
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderModelsRequireOrderedSessionLocalDates(PrimaryDatabaseProvider provider)
    {
        using var context = CreateContext(provider);
        await AssertConstraint(RequiredEntity<EventSession>(context.GetService<IDesignTimeModel>().Model),
            "ck_event_session_local_date_range", "local_end_date IS NULL", "local_end_date >= local_start_date");
    }

    private static async Task AssertLeaf(
        IEntityType entity, string name, Type clrType, string column, string storeType, bool nullable)
    {
        IProperty? property = entity.FindProperty(name);
        await Assert.That(property).IsNotNull();
        await Assert.That(property!.ClrType).IsEqualTo(clrType);
        await Assert.That(property.GetColumnName()).IsEqualTo(column);
        await Assert.That(property.GetColumnType()).IsEqualTo(storeType);
        await Assert.That(property.IsNullable).IsEqualTo(nullable);
        await Assert.That(property.GetValueConverter()).IsNull();
    }

    private static async Task AssertTemporalLeaves(IEntityType entity, bool nullable, string instantType, string localDateType)
    {
        Type instantClrType = nullable ? typeof(DateTimeOffset?) : typeof(DateTimeOffset);
        Type localDateClrType = nullable ? typeof(DateOnly?) : typeof(DateOnly);
        await AssertLeaf(entity, nameof(EventSession.StartTime), instantClrType, "start_time", instantType, nullable);
        await AssertLeaf(entity, nameof(EventSession.EndTime), instantClrType, "end_time", instantType, nullable);
        await AssertLeaf(entity, nameof(EventSession.LocalStartDate), localDateClrType, "local_start_date", localDateType, nullable);
        await AssertLeaf(entity, nameof(EventSession.LocalEndDate), localDateClrType, "local_end_date", localDateType, nullable);
    }

    private static async Task AssertOwnerMetadata(
        IEntityType ticket, IEntityType payment, IEntityType locationPii, IEntityType agenda, IEntityType session)
    {
        await Assert.That(ticket.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(ticket.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(payment.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(locationPii.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(agenda.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(agenda.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(session.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(session.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(ticket.FindProperty(nameof(EventTicketType.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
        await Assert.That(payment.FindProperty(nameof(PaymentAttempt.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
        await Assert.That(agenda.FindProperty(nameof(EventAgendaItem.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
        await Assert.That(session.FindProperty(nameof(EventSession.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();

        await Assert.That(HasIndex(ticket, nameof(EventTicketType.TenantId), nameof(EventTicketType.CatalogId))).IsTrue();
        await Assert.That(HasIndex(payment, nameof(PaymentAttempt.TenantId), nameof(PaymentAttempt.RegistrationOrderId), nameof(PaymentAttempt.PaymentAttemptStatusId))).IsTrue();
        await Assert.That(HasIndex(agenda, nameof(EventAgendaItem.TenantId), nameof(EventAgendaItem.EventId), nameof(EventAgendaItem.LocalStartDate), nameof(EventAgendaItem.LocalStartMinuteOfDay))).IsTrue();
        await Assert.That(HasIndex(session, nameof(EventSession.TenantId), nameof(EventSession.EventId), nameof(EventSession.LocalStartDate), nameof(EventSession.LocalStartMinuteOfDay))).IsTrue();
        await Assert.That(HasIndex(session, nameof(EventSession.TenantId), nameof(EventSession.LocationId), nameof(EventSession.RoomId), nameof(EventSession.StartTime), nameof(EventSession.EndTime))).IsTrue();
        await Assert.That(agenda.FindAnnotation("EventLocationPrivacy:ConsistencyTrigger")?.Value).IsNotNull();
        await Assert.That(session.FindAnnotation("EventLocationPrivacy:ConsistencyTrigger")?.Value).IsNotNull();
        await Assert.That(HasIndex(agenda, nameof(EventAgendaItem.TenantId), nameof(EventAgendaItem.EventId), nameof(EventAgendaItem.EventLocationId), nameof(EventAgendaItem.LocationId))).IsTrue();
        await Assert.That(HasIndex(session, nameof(EventSession.TenantId), nameof(EventSession.EventId), nameof(EventSession.EventLocationId), nameof(EventSession.LocationId))).IsTrue();
        await Assert.That(locationPii.FindPrimaryKey()!.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(LocationPii.LocationId)]);
        await Assert.That(locationPii.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(Location) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual([nameof(LocationPii.LocationId)]) &&
            foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.NoAction)).IsTrue();
    }

    private static async Task AssertPaymentCheck(IEntityType payment)
    {
        await AssertConstraint(payment, "ck_payment_attempts_amounts",
            "organizer_amount_minor >= 0", "platform_fee_minor >= 0", "platform_contribution_minor >= 0",
            "total_minor >= 0", "platform_fee_minor <= organizer_amount_minor",
            "total_minor = organizer_amount_minor + platform_contribution_minor");
    }

    private static async Task AssertConstraint(IEntityType entity, string name, params string[] fragments)
    {
        ICheckConstraint? constraint = entity.GetCheckConstraints().SingleOrDefault(check => check.Name == name);
        await Assert.That(constraint).IsNotNull();
        foreach (string fragment in fragments)
        {
            await Assert.That(constraint?.Sql ?? "").Contains(fragment);
        }
    }

    private static bool HasIndex(IEntityType entity, params string[] names) =>
        entity.GetIndexes().Any(index => index.Properties.Select(property => property.Name).SequenceEqual(names));

    private static IEntityType RequiredEntity<TEntity>(IModel model) =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped.");

    private static ExploreDbContext CreateContext(PrimaryDatabaseProvider provider)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateOptions(provider));
        return new ExploreDbContext(builder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(PrimaryDatabaseProvider provider) =>
        provider == PrimaryDatabaseProvider.Sqlite
            ? new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = Path.Combine(Path.GetTempPath(), "semantic-value-scalar-model.db"),
            }
            : new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Host = "localhost",
                Database = "semantic_value_scalar_model",
                Username = "model",
                Password = Guid.CreateVersion7().ToString("N"),
                TlsMode = PrimaryDatabaseTlsMode.Disabled,
                ServerFlavor = provider switch
                {
                    PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                    PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                    _ => null,
                },
                ServerVersion = provider switch
                {
                    PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                    PrimaryDatabaseProvider.MySql => new Version(8, 4),
                    _ => null,
                },
            };
}
