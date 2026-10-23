using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Waitlist;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresRegistrationPrivateReadContract
{
    private static readonly string[] WaitlistEquivalenceProperties =
    [
        "EventId", "EventTicketTypeId", "TicketCatalogVersionId", "PurchasePolicySnapshotId",
        "CurrencyCode", "CommercialTermsDigest", "AdmissionEntitlementDigest", "GrossMinorUnits", "RefundFundingModeId"
    ];

    private sealed class GraphSql(ExploreDbContext context)
    {
        internal HumanPrincipalSourceSql Names { get; } = new(context);
        internal Dictionary<Type, HashSet<string>> Sources { get; } = [];
        private int nextAlias;
        internal string Table<T>() => Names.Table(typeof(T));
        internal string Column<T>(string alias, string property) => Column(typeof(T), alias, property);
        internal string Column(Type type, string alias, string property)
        {
            if (context.Model.FindEntityType(type)?.FindProperty(property) is null)
                throw new InvalidOperationException($"registration_private_read_unmapped_source:{type.Name}.{property}");
            if (!Sources.TryGetValue(type, out var columns)) Sources.Add(type, columns = []);
            columns.Add(property);
            return Names.Quote(alias) + "." + Names.Column(type, property);
        }
        internal string Live<T>(string alias) => Live(typeof(T), alias);
        private string Live(Type type, string alias)
        {
            var entity = context.Model.FindEntityType(type)!;
            var predicates = new List<string>();
            if (entity.FindProperty("TenantId") is { } tenant)
                predicates.Add(tenant.IsNullable
                    ? $"({Column(type, alias, tenant.Name)} IS NULL OR {Column(type, alias, tenant.Name)} = p_tenant)"
                    : $"{Column(type, alias, tenant.Name)} = p_tenant");
            if (entity.FindProperty("IsDeleted") is not null) predicates.Add("NOT " + Column(type, alias, "IsDeleted"));
            return predicates.Count == 0 ? "true" : string.Join(" AND ", predicates);
        }
        internal string Entity<T>(string alias, params string[] paths) => Entity(typeof(T), alias, paths);
        private string Entity(Type type, string alias, string[] paths)
        {
            var entity = context.Model.FindEntityType(type)!;
            string scalar = string.Join(" || ", entity.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal)
                .Chunk(30).Select(chunk => "pg_catalog.jsonb_build_object(" + string.Join(",",
                    chunk.Select(p => Text(p.Name) + "," + Column(type, alias, p.Name)
                        + ((p.GetTypeMapping().Converter?.ProviderClrType ?? p.ClrType) == typeof(string)
                            && p.GetRelationalTypeMapping().StoreType is "json" or "jsonb" ? "::text" : ""))) + ")"));
            foreach (var group in paths.GroupBy(path => path.Split('.')[0]))
            {
                var navigation = entity.FindNavigation(group.Key)
                    ?? throw new InvalidOperationException($"registration_private_read_unmapped_navigation:{type.Name}.{group.Key}");
                Type child = navigation.TargetEntityType.ClrType;
                string childAlias = "g" + nextAlias++;
                var key = navigation.ForeignKey;
                string join = string.Join(" AND ", key.Properties.Select((property, index) =>
                    navigation.IsOnDependent
                        ? $"{Column(type, alias, property.Name)} = {Column(child, childAlias, key.PrincipalKey.Properties[index].Name)}"
                        : $"{Column(child, childAlias, property.Name)} = {Column(type, alias, key.PrincipalKey.Properties[index].Name)}"));
                string nested = Entity(child, childAlias, group.Where(path => path.Contains('.', StringComparison.Ordinal))
                    .Select(path => path[(path.IndexOf('.', StringComparison.Ordinal) + 1)..]).ToArray());
                string value = navigation.IsCollection
                    ? $"COALESCE((SELECT pg_catalog.jsonb_agg({nested}) FROM {Names.Table(child)} {Names.Quote(childAlias)} WHERE {Live(child, childAlias)} AND {join}),'[]'::jsonb)"
                    : $"(SELECT {nested} FROM {Names.Table(child)} {Names.Quote(childAlias)} WHERE {Live(child, childAlias)} AND {join})";
                scalar += $" || pg_catalog.jsonb_build_object({Text(group.Key)},{value})";
            }
            return scalar;
        }
        internal string One<T>(string predicate, params string[] paths) =>
            $"(SELECT {Entity<T>("r", paths)} FROM {Table<T>()} r WHERE {Live<T>("r")} AND {predicate} LIMIT 1)";
        internal string Rows<T>(string predicate) =>
            $"COALESCE((SELECT pg_catalog.jsonb_agg({Entity<T>("r")}) FROM {Table<T>()} r WHERE {Live<T>("r")} AND {predicate}),'[]'::jsonb)";
    }

    private static string Projection(GraphSql s, RegistrationPrivateReadKind kind)
    {
        string C<T>(string property) => s.Column<T>("r", property);
        string Id<T>(string id) => C<T>("Id") + " = " + id;
        string OrderRows<T>() => C<T>("EventId") + " = p_event AND " + C<T>("RegistrationOrderId") + " = v_order";
        string Pair(string key, string value) => Text(key) + "," + value;
        string[] paths = kind switch
        {
            RegistrationPrivateReadKind.AddOns => ["AddOnLines"],
            RegistrationPrivateReadKind.Waitlist => ["Lines.Assignments"],
            RegistrationPrivateReadKind.Transfer => [],
            _ => ["Lines", "PlatformContribution"]
        };
        var values = new List<string> { Pair("Order", s.One<RegistrationOrder>(Id<RegistrationOrder>("v_order"), paths)) };
        if (kind is RegistrationPrivateReadKind.AddOns or RegistrationPrivateReadKind.Readiness)
            values.Add(Pair("Event", s.One<Event>(Id<Event>("p_event"), "Actor", "OrganizerActor")));
        switch (kind)
        {
            case RegistrationPrivateReadKind.AddOns:
                values.Add(Pair("Catalog", s.One<EventAddOnCatalogVersion>(
                    $"{C<EventAddOnCatalogVersion>("EventId")} = p_event AND {C<EventAddOnCatalogVersion>("PublishedAt")} <= p_at AND {C<EventAddOnCatalogVersion>("RetiredAt")} IS NULL", "Items")));
                values.Add(Pair("Fulfillments", s.Rows<EventAddOnFulfillment>(OrderRows<EventAddOnFulfillment>())));
                values.Add(Pair("Refunds", s.Rows<EventAddOnRefundAllocation>(OrderRows<EventAddOnRefundAllocation>())));
                break;
            case RegistrationPrivateReadKind.Readiness:
                values.Add(Pair("Participant", s.One<RegistrationParticipant>(
                    $"{Id<RegistrationParticipant>("p_participant")} AND {C<RegistrationParticipant>("RegistrationOrderId")} = v_order")));
                values.Add(Pair("Eligibility", s.One<ParticipantAdmissionEligibility>(
                    $"{C<ParticipantAdmissionEligibility>("RegistrationTicketAssignmentId")} = p_assignment AND {OrderRows<ParticipantAdmissionEligibility>()} AND {C<ParticipantAdmissionEligibility>("ParticipantId")} = p_participant")));
                values.Add(Pair("Ticket", s.One<AdmissionTicket>(
                    $"{C<AdmissionTicket>("RegistrationTicketAssignmentId")} = p_assignment AND {OrderRows<AdmissionTicket>()}")));
                break;
            case RegistrationPrivateReadKind.Transfer:
                values.Add(Pair("Transfer", s.One<AdmissionTicketTransfer>(Id<AdmissionTicketTransfer>("p_transfer"))));
                values.Add(Pair("Ticket", s.One<AdmissionTicket>(Id<AdmissionTicket>("p_ticket"))));
                foreach (string end in new[] { "From", "To" })
                    values.Add(Pair(end + "Participant", s.One<RegistrationParticipant>(
                        $"{C<RegistrationParticipant>("RegistrationOrderId")} = v_order AND {C<RegistrationParticipant>("Id")} = (SELECT {s.Column<AdmissionTicketTransfer>("x", end + "ParticipantId")} FROM {s.Table<AdmissionTicketTransfer>()} x WHERE {s.Live<AdmissionTicketTransfer>("x")} AND {s.Column<AdmissionTicketTransfer>("x", "Id")} = p_transfer)")));
                break;
            case RegistrationPrivateReadKind.Waitlist:
                values.Add(Pair("Entry", s.One<EventWaitlistEntry>(Id<EventWaitlistEntry>("v_entry"))));
                values.Add(Pair("Offer", s.One<EventWaitlistOffer>(Id<EventWaitlistOffer>("v_offer"))));
                values.Add(Pair("Supply", s.One<FairReturnSupplyUnit>(Id<FairReturnSupplyUnit>("v_supply"))));
                values.Add(Pair("Binding", s.One<FairReturnSourceBinding>(Id<FairReturnSourceBinding>("v_binding"))));
                values.Add(Pair("Policy", s.One<FairReturnSupplyPolicy>(
                    $"{C<FairReturnSupplyPolicy>("EventId")} = p_event AND EXISTS (SELECT 1 FROM {s.Table<RegistrationOrderLine>()} line WHERE {s.Live<RegistrationOrderLine>("line")} AND {s.Column<RegistrationOrderLine>("line", "Id")} = p_line AND {s.Column<RegistrationOrderLine>("line", "TicketCatalogVersionId")} = {C<FairReturnSupplyPolicy>("TicketCatalogVersionId")} AND {s.Column<RegistrationOrderLine>("line", "TicketTypeId")} = {C<FairReturnSupplyPolicy>("EventTicketTypeId")})")));
                values.Add(Pair("PurchaseOperation", $"""
                    (SELECT {s.Entity<TicketPurchaseOperation>("r")} FROM {s.Table<TicketPurchaseOperation>()} r
                        WHERE {s.Live<TicketPurchaseOperation>("r")} AND {C<TicketPurchaseOperation>("EventId")} = p_event
                            AND {C<TicketPurchaseOperation>("OrderId")} = v_order
                            AND {C<TicketPurchaseOperation>("Disposition")} = {(int)TicketPurchaseReservationDisposition.Reserved}
                        ORDER BY {C<TicketPurchaseOperation>("CreatedAt")} DESC LIMIT 1)
                    """));
                values.Add(Pair("Position", Position(s)));
                values.Add(Pair("ReplacementSettled", $"EXISTS (SELECT 1 FROM {s.Table<WaitlistPaymentIntent>()} r WHERE {s.Live<WaitlistPaymentIntent>("r")} AND {C<WaitlistPaymentIntent>("FairReturnSourceBindingId")} = v_binding AND {C<WaitlistPaymentIntent>("ReplacementPaymentSettledAt")} IS NOT NULL)"));
                values.Add(Pair("TenantSaleControl", s.One<PaidCheckoutSaleControl>(C<PaidCheckoutSaleControl>("EventId") + " IS NULL")));
                values.Add(Pair("EventSaleControl", s.One<PaidCheckoutSaleControl>(C<PaidCheckoutSaleControl>("EventId") + " = p_event")));
                break;
        }
        return "pg_catalog.jsonb_build_object(" + string.Join(",", values) + ")";
    }

    private static string WaitlistSelection(GraphSql s)
    {
        string C<T>(string property) => s.Column<T>("r", property);
        return $"""
            IF p_kind = {(int)RegistrationPrivateReadKind.Waitlist} THEN
                IF NOT EXISTS (SELECT 1 FROM {s.Table<RegistrationOrderLine>()} r WHERE {s.Live<RegistrationOrderLine>("r")}
                    AND {C<RegistrationOrderLine>("Id")} = p_line AND {C<RegistrationOrderLine>("RegistrationOrderId")} = v_order)
                THEN RETURN NULL; END IF;
                SELECT {C<EventWaitlistEntry>("Id")} INTO v_entry FROM {s.Table<EventWaitlistEntry>()} r
                    WHERE {s.Live<EventWaitlistEntry>("r")} AND {C<EventWaitlistEntry>("EventId")} = p_event
                        AND {C<EventWaitlistEntry>("RegistrationOrderId")} = v_order AND {C<EventWaitlistEntry>("RegistrationOrderLineId")} = p_line
                    ORDER BY ({C<EventWaitlistEntry>("OpenRegistrationOrderLineId")} IS NOT NULL) DESC,
                        {C<EventWaitlistEntry>("CreatedAt")} DESC LIMIT 1;
                SELECT {C<EventWaitlistOffer>("Id")} INTO v_offer FROM {s.Table<EventWaitlistOffer>()} r
                    WHERE {s.Live<EventWaitlistOffer>("r")} AND {C<EventWaitlistOffer>("EventId")} = p_event
                        AND {C<EventWaitlistOffer>("EventWaitlistEntryId")} = v_entry
                    ORDER BY ({C<EventWaitlistOffer>("OpenEventWaitlistEntryId")} IS NOT NULL) DESC,
                        {C<EventWaitlistOffer>("CreatedAt")} DESC LIMIT 1;
                IF v_offer IS NOT NULL THEN
                    SELECT {C<FairReturnSupplyUnit>("Id")} INTO v_supply FROM {s.Table<FairReturnSupplyUnit>()} r
                        WHERE {s.Live<FairReturnSupplyUnit>("r")} AND {C<FairReturnSupplyUnit>("Id")} =
                            (SELECT {s.Column<EventWaitlistOffer>("o", "FairReturnSupplyUnitId")} FROM {s.Table<EventWaitlistOffer>()} o
                                WHERE {s.Live<EventWaitlistOffer>("o")} AND {s.Column<EventWaitlistOffer>("o", "Id")} = v_offer);
                ELSE
                    SELECT {C<FairReturnSupplyUnit>("Id")} INTO v_supply FROM {s.Table<FairReturnSupplyUnit>()} r
                        WHERE {s.Live<FairReturnSupplyUnit>("r")} AND {C<FairReturnSupplyUnit>("EventId")} = p_event
                            AND {C<FairReturnSupplyUnit>("SellerRegistrationOrderLineId")} = p_line
                        ORDER BY {C<FairReturnSupplyUnit>("CreatedAt")} DESC LIMIT 1;
                END IF;
                SELECT {C<FairReturnSourceBinding>("Id")} INTO v_binding FROM {s.Table<FairReturnSourceBinding>()} r
                    WHERE {s.Live<FairReturnSourceBinding>("r")} AND {C<FairReturnSourceBinding>("EventId")} = p_event
                        AND ({C<FairReturnSourceBinding>("BuyerRegistrationOrderLineId")} = p_line OR {C<FairReturnSourceBinding>("FairReturnSupplyUnitId")} = v_supply)
                    ORDER BY {C<FairReturnSourceBinding>("CreatedAt")} DESC LIMIT 1;
            END IF;
            """;
    }

    private static string Position(GraphSql s)
    {
        string C(string alias, string property) => s.Column<EventWaitlistEntry>(alias, property);
        string same = string.Join(" AND ", WaitlistEquivalenceProperties.Select(property =>
            $"{C("r", property)} = {C("selected", property)}"));
        return $"""
            COALESCE((SELECT CASE WHEN {C("selected", "StatusId")} = {(int)EventWaitlistEntryStatus.Queued}
                THEN COALESCE((SELECT position FROM (
                    SELECT {C("r", "Id")} AS id, row_number() OVER (ORDER BY {C("r", "Priority")} DESC,
                        {C("r", "EnqueuedAt")},{C("r", "Id")}) AS position
                    FROM {s.Table<EventWaitlistEntry>()} r WHERE {s.Live<EventWaitlistEntry>("r")}
                        AND {same} AND {C("r", "StatusId")} = {(int)EventWaitlistEntryStatus.Queued}
                    ORDER BY {C("r", "Priority")} DESC,{C("r", "EnqueuedAt")},{C("r", "Id")} LIMIT 1000
                ) ordered WHERE id = v_entry),999) ELSE 0 END
                FROM {s.Table<EventWaitlistEntry>()} selected WHERE {s.Live<EventWaitlistEntry>("selected")}
                    AND {C("selected", "Id")} = v_entry),0)
            """;
    }

    private static RegistrationPrivateGraph Materialize(ExploreDbContext context,
        RegistrationPrivateRead request, JsonElement document)
    {
        T? One<T>(string key) where T : class => document.TryGetProperty(key, out var value)
            && value.ValueKind != JsonValueKind.Null ? (T)Entity(typeof(T), value) : null;
        IReadOnlyList<T> Rows<T>(string key) where T : class => document.TryGetProperty(key, out var rows)
            ? rows.EnumerateArray().Select(row => (T)Entity(typeof(T), row)).ToArray() : [];
        var order = One<RegistrationOrder>("Order")
            ?? throw new InvalidOperationException("registration_private_read_order_missing");
        var ticket = One<AdmissionTicket>("Ticket");
        var transfer = One<AdmissionTicketTransfer>("Transfer");
        return new RegistrationPrivateGraph
        {
            Order = order,
            Event = One<Event>("Event"),
            AddOnCatalog = One<EventAddOnCatalogVersion>("Catalog"),
            Fulfillments = Rows<EventAddOnFulfillment>("Fulfillments"),
            Refunds = Rows<EventAddOnRefundAllocation>("Refunds"),
            Participant = One<RegistrationParticipant>("Participant"),
            Eligibility = One<ParticipantAdmissionEligibility>("Eligibility"),
            Ticket = ticket,
            Transfer = transfer is null ? null : new AdmissionTicketTransferAccessContext(transfer,
                ticket ?? throw new InvalidOperationException("registration_private_read_ticket_missing"), order,
                One<RegistrationParticipant>("FromParticipant")
                    ?? throw new InvalidOperationException("registration_private_read_source_missing"),
                One<RegistrationParticipant>("ToParticipant")),
            Waitlist = request.Kind != RegistrationPrivateReadKind.Waitlist ? null : new FairReturnWaitlistAccessContext(
                order, order.Lines.Single(row => row.Id == request.LineId), One<EventWaitlistEntry>("Entry"),
                One<EventWaitlistOffer>("Offer"), One<FairReturnSupplyUnit>("Supply"), One<FairReturnSourceBinding>("Binding"),
                One<FairReturnSupplyPolicy>("Policy"), One<TicketPurchaseOperation>("PurchaseOperation"),
                document.GetProperty("Position").GetInt64()),
            ReplacementSettled = document.TryGetProperty("ReplacementSettled", out var settled) && settled.GetBoolean(),
            TenantSaleControl = One<PaidCheckoutSaleControl>("TenantSaleControl"),
            EventSaleControl = One<PaidCheckoutSaleControl>("EventSaleControl")
        };

        object Entity(Type type, JsonElement row)
        {
            var model = context.Model.FindEntityType(type)!;
            var properties = model.GetProperties().ToArray();
            object?[] values = new object?[properties.Max(p => p.GetIndex()) + 1];
            foreach (var property in properties)
            {
                var value = row.GetProperty(property.Name);
                var converter = property.GetTypeMapping().Converter;
                Type scalarType = converter?.ProviderClrType ?? property.ClrType;
                object? scalar = value.ValueKind == JsonValueKind.Null ? null
                    : (Nullable.GetUnderlyingType(scalarType) ?? scalarType) == typeof(DateTime)
                        ? value.GetDateTimeOffset().UtcDateTime
                        : scalarType == typeof(byte[]) ? Convert.FromHexString(value.GetString()![2..])
                        : value.Deserialize(scalarType);
                values[property.GetIndex()] = converter is null ? scalar : converter.ConvertFromProvider(scalar);
            }
            object entity = context.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(model)(
                new MaterializationContext(new ValueBuffer(values), context));
            foreach (var navigation in model.GetNavigations())
            {
                if (!row.TryGetProperty(navigation.Name, out var nested) || nested.ValueKind == JsonValueKind.Null) continue;
                if (navigation.IsCollection)
                    foreach (var child in nested.EnumerateArray())
                        Connect(entity, navigation, Entity(navigation.TargetEntityType.ClrType, child));
                else Connect(entity, navigation, Entity(navigation.TargetEntityType.ClrType, nested));
            }
            return entity;
        }
        static void Connect(object owner, INavigation navigation, object child)
        {
            if (navigation.IsCollection) navigation.GetCollectionAccessor()!.Add(owner, child, true);
            else navigation.PropertyInfo!.SetValue(owner, child);
            if (navigation.Inverse is not { } inverse) return;
            if (inverse.IsCollection) inverse.GetCollectionAccessor()!.Add(child, owner, true);
            else inverse.PropertyInfo!.SetValue(child, owner);
        }
    }
}
