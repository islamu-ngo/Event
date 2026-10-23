using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresPrivateEventManagementContract
{
    // These paths are the closed management graph, not caller-supplied includes. In particular
    // private actions and draft program rows are not filtered through public discovery.
    private static readonly string[] EventPaths =
    [
        "EventType", "EventProvenanceType", "ParticipationConfiguration.ParticipationHandlingMode",
        "ParticipationConfiguration.AdvanceRegistrationObligation", "ParticipationConfiguration.IdentityAccessMode",
        "ParticipationConfiguration.RequirementAttachments.RegistrationRequirement",
        "ParticipationConfiguration.RequirementAttachments.RegistrationFormVersion",
        "PublicActions.EventPublicActionKind", "PublicActions.HealthState", "AudienceGender", "AudienceAge",
        "Actor.ActorType", "Actor.Pii.ProfilePicture", "OrganizerActor.Pii.ProfilePicture", "FeaturedImage",
        "EventStatus", "VisibilityType", "EventFormat", "Madhab", "IslamicAspect.Madhab",
        "IslamicAspect.PrimaryLanguage", "TechAspect", "CapacityPools.CapacityHoldPolicy",
        "CapacityPools.CapacityOversellPolicy", "TicketCatalogVersions.TicketTypes", "RegistrationPolicy", "AtprotoRecord"
    ];
    private static readonly string[] SessionPaths =
    [
        "Event", "Location.Pii", "EventSessionKind", "RegistrationMode", "Room", "FeaturedImage",
        "IslamicAspect", "SessionGroups.EventSessionGroup"
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
                throw new InvalidOperationException($"private_management_unmapped_source:{type.Name}.{property}");
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
                    ?? throw new InvalidOperationException($"private_management_unmapped_navigation:{type.Name}.{group.Key}");
                Type child = navigation.TargetEntityType.ClrType;
                string childAlias = "g" + nextAlias++;
                var key = navigation.ForeignKey;
                string join = string.Join(" AND ", key.Properties.Select((property, index) =>
                    navigation.IsOnDependent
                        ? $"{Column(type, alias, property.Name)} = {Column(child, childAlias, key.PrincipalKey.Properties[index].Name)}"
                        : $"{Column(child, childAlias, property.Name)} = {Column(type, alias, key.PrincipalKey.Properties[index].Name)}"));
                string extra = child == typeof(EventTicketCatalogVersion)
                    ? $" AND {Column(child, childAlias, "TicketCatalogStatusId")} = {(int)TicketCatalogStatusEnum.Published}" : "";
                string nested = Entity(child, childAlias, group.Where(path => path.Contains('.', StringComparison.Ordinal))
                    .Select(path => path[(path.IndexOf('.', StringComparison.Ordinal) + 1)..]).ToArray());
                string value = navigation.IsCollection
                    ? $"COALESCE((SELECT pg_catalog.jsonb_agg({nested}) FROM {Names.Table(child)} {Names.Quote(childAlias)} WHERE {Live(child, childAlias)} AND {join}{extra}),'[]'::jsonb)"
                    : $"(SELECT {nested} FROM {Names.Table(child)} {Names.Quote(childAlias)} WHERE {Live(child, childAlias)} AND {join}{extra})";
                scalar += $" || pg_catalog.jsonb_build_object({Text(group.Key)},{value})";
            }
            return scalar;
        }
        internal string Rows<T>(string predicate, params string[] paths) =>
            $"COALESCE((SELECT pg_catalog.jsonb_agg({Entity<T>("r", paths)}) FROM {Table<T>()} r WHERE {Live<T>("r")} AND {predicate}),'[]'::jsonb)";
    }

    private static string Projection(GraphSql s, PrivateEventManagementReadKind kind)
    {
        string EventRows(string predicate, params string[] paths) =>
            s.Rows<Event>(predicate, paths);
        string Parent<T>() => s.Column<T>("r", "EventId") + " = p_event";
        string Main() => EventRows(s.Column<Event>("r", "Id") + " = p_event", EventPaths);
        string Pair(string key, string value) => Text(key) + "," + value;
        string Object(params string[] pairs) => "pg_catalog.jsonb_build_object(" + string.Join(",", pairs) + ")";
        return kind switch
        {
            PrivateEventManagementReadKind.Team => Object(
                Pair("Team", s.Rows<EventRoleAssignment>(
                    $"{Parent<EventRoleAssignment>()} AND EXISTS (SELECT 1 FROM {s.Table<User>()} member WHERE {s.Live<User>("member")} AND {s.Column<User>("member", "Id")} = {s.Column<EventRoleAssignment>("r", "UserId")})",
                    "User.Pii", "Role"))),
            PrivateEventManagementReadKind.SessionAuthorizationTarget => Object(Pair("Events",
                EventRows($"EXISTS (SELECT 1 FROM {s.Table<EventSession>()} selected_session WHERE {s.Live<EventSession>("selected_session")} AND {s.Column<EventSession>("selected_session", "Id")} = p_session AND {s.Column<EventSession>("selected_session", "EventId")} = {s.Column<Event>("r", "Id")})",
                    "Actor.Pii.ProfilePicture", "OrganizerActor.Pii.ProfilePicture", "ParticipationConfiguration", "EventProvenanceType"))),
            PrivateEventManagementReadKind.AuthorizationTarget => Object(Pair("Events",
                EventRows(s.Column<Event>("r", "Id") + " = p_event",
                    "Actor.Pii.ProfilePicture", "OrganizerActor.Pii.ProfilePicture", "ParticipationConfiguration", "EventProvenanceType"))),
            PrivateEventManagementReadKind.Details => Object(Pair("Events", Main()),
                Pair("Tags", s.Rows<Tag>($"EXISTS (SELECT 1 FROM {s.Table<EventTags>()} et WHERE {s.Live<EventTags>("et")} AND {s.Column<EventTags>("et", "EventId")} = p_event AND {s.Column<EventTags>("et", "TagId")} = {s.Column<Tag>("r", "Id")})")),
                Pair("Categories", s.Rows<Category>($"EXISTS (SELECT 1 FROM {s.Table<EventCategories>()} ec WHERE {s.Live<EventCategories>("ec")} AND {s.Column<EventCategories>("ec", "EventId")} = p_event AND {s.Column<EventCategories>("ec", "CategoryId")} = {s.Column<Category>("r", "Id")})")),
                Pair("ModerationRecords", s.Rows<EventModerationRecord>(Parent<EventModerationRecord>()))),
            PrivateEventManagementReadKind.Readiness => Object(Pair("Events", Main()),
                Pair("Locations", s.Rows<EventLocation>(Parent<EventLocation>(), "Location.Pii", "FullDetailsAudience"))),
            PrivateEventManagementReadKind.Program => Object(
                Pair("Sessions", s.Rows<EventSession>(Parent<EventSession>(), SessionPaths)),
                Pair("SessionGroups", s.Rows<EventSessionGroup>(Parent<EventSessionGroup>(), "Location.Pii", "Room")),
                Pair("Days", s.Rows<EventDay>(Parent<EventDay>())),
                Pair("AgendaItems", s.Rows<EventAgendaItem>(Parent<EventAgendaItem>()))),
            PrivateEventManagementReadKind.CustomProperties => Object(
                Pair("Definitions", s.Rows<EventCustomPropertyDefinition>(Parent<EventCustomPropertyDefinition>(), "Options")),
                Pair("Values", s.Rows<EventCustomPropertyValue>(Parent<EventCustomPropertyValue>()))),
            PrivateEventManagementReadKind.Registrations => Object(
                Pair("Registrations", s.Rows<RegistrationOrder>(Parent<RegistrationOrder>(), "Lines", "PlatformContribution"))),
            PrivateEventManagementReadKind.Templates => Object(
                Pair("Templates", s.Rows<EventTemplate>($"(p_type IS NULL OR {s.Column<EventTemplate>("r", "EventTypeId")} = p_type)", "Definitions"))),
            PrivateEventManagementReadKind.SessionTemplates => Object(
                Pair("SessionTemplates", s.Rows<EventSessionTemplate>("true", "Definitions"))),
            PrivateEventManagementReadKind.SessionSyncContext => Object(
                Pair("Sessions", s.Rows<EventSession>($"{Parent<EventSession>()} AND {s.Column<EventSession>("r", "Id")} = p_session")),
                Pair("History", s.Rows<AuditLog>($"{s.Column<AuditLog>("r", "EntityType")} = {Text(nameof(EventSession))} AND {s.Column<AuditLog>("r", "EntityId")} = p_session::text AND {s.Column<AuditLog>("r", "Action")} = 'TemplateSyncApplied'"))),
            PrivateEventManagementReadKind.EventSyncHistory or PrivateEventManagementReadKind.SessionSyncHistory => Object(
                Pair("History", s.Rows<AuditLog>($"{s.Column<AuditLog>("r", "EntityType")} = {Text(kind == PrivateEventManagementReadKind.EventSyncHistory ? nameof(Event) : nameof(EventSession))} AND {s.Column<AuditLog>("r", "EntityId")} = {(kind == PrivateEventManagementReadKind.EventSyncHistory ? "p_event" : "p_session")}::text AND {s.Column<AuditLog>("r", "Action")} = 'TemplateSyncApplied'"))),
            PrivateEventManagementReadKind.MyEvents => MyEvents(s),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static string MyEvents(GraphSql s)
    {
        // Match the original UNION: personal actor OR organization membership OR group
        // membership. EventCreate, public visibility and published status are not predicates.
        string membership = $"""
            EXISTS (SELECT 1 FROM {s.Table<Actor>()} a WHERE {s.Live<Actor>("a")}
                AND {s.Column<Actor>("a", "Id")} = {s.Column<Event>("e", "ActorId")}
                AND ({s.Column<Actor>("a", "UserId")} = p_principal
                    OR EXISTS (SELECT 1 FROM {s.Table<OrganizationMember>()} om
                        JOIN {s.Table<OrganizationTenant>()} ot ON {s.Column<OrganizationTenant>("ot", "Id")} = {s.Column<OrganizationMember>("om", "OrganizationTenantId")}
                        JOIN {s.Table<Organization>()} o ON {s.Column<Organization>("o", "Id")} = {s.Column<OrganizationTenant>("ot", "OrganizationId")}
                        WHERE {s.Live<OrganizationMember>("om")} AND {s.Live<OrganizationTenant>("ot")} AND {s.Live<Organization>("o")}
                            AND {s.Column<OrganizationMember>("om", "UserId")} = p_principal
                            AND {s.Column<Organization>("o", "Id")} = {s.Column<Actor>("a", "OrganizationId")})
                    OR EXISTS (SELECT 1 FROM {s.Table<GroupMember>()} gm
                        JOIN {s.Table<GroupTenant>()} gt ON {s.Column<GroupTenant>("gt", "Id")} = {s.Column<GroupMember>("gm", "GroupTenantId")}
                        JOIN {s.Table<Group>()} g ON {s.Column<Group>("g", "Id")} = {s.Column<GroupTenant>("gt", "GroupId")}
                        WHERE {s.Live<GroupMember>("gm")} AND {s.Live<GroupTenant>("gt")} AND {s.Live<Group>("g")}
                            AND {s.Column<GroupMember>("gm", "UserId")} = p_principal
                            AND {s.Column<Group>("g", "Id")} = {s.Column<Actor>("a", "GroupId")}))
            )
            """;
        string projection = s.Entity<Event>("e", EventPaths);
        string delivery = s.Entity<PdsSyncOutbox>("d");
        return $"""
            (WITH eligible AS (
                SELECT {s.Column<Event>("e", "Id")} AS id, {s.Column<Event>("e", "FirstSessionDate")} AS first_date
                FROM {s.Table<Event>()} e WHERE {s.Live<Event>("e")} AND {membership}),
            page AS (SELECT id FROM eligible ORDER BY first_date DESC,id
                OFFSET ((p_page::bigint - 1) * p_size) LIMIT p_size)
            SELECT pg_catalog.jsonb_build_object(
                'TotalCount',(SELECT count(*) FROM eligible),
                'Events',COALESCE((SELECT pg_catalog.jsonb_agg({projection} ORDER BY {s.Column<Event>("e", "FirstSessionDate")} DESC,{s.Column<Event>("e", "Id")})
                    FROM {s.Table<Event>()} e JOIN page ON page.id = {s.Column<Event>("e", "Id")}), '[]'::jsonb),
                'DeliveryStates',COALESCE((SELECT pg_catalog.jsonb_agg(row) FROM (
                    SELECT DISTINCT ON ({s.Column<PdsSyncOutbox>("d", "SourceEntityId")}) {delivery} AS row
                    FROM {s.Table<PdsSyncOutbox>()} d JOIN page ON page.id = {s.Column<PdsSyncOutbox>("d", "SourceEntityId")}
                    WHERE {s.Live<PdsSyncOutbox>("d")} AND {s.Column<PdsSyncOutbox>("d", "SourceEntityType")} = 'Event'
                        AND {s.Column<PdsSyncOutbox>("d", "SupersededAt")} IS NULL
                    ORDER BY {s.Column<PdsSyncOutbox>("d", "SourceEntityId")}, {s.Column<PdsSyncOutbox>("d", "CreatedAt")} DESC,
                        {s.Column<PdsSyncOutbox>("d", "Id")} DESC) delivery), '[]'::jsonb)))
            """;
    }

    private static PrivateEventManagementGraph Materialize(ExploreDbContext context, JsonElement document)
    {
        var result = new PrivateEventManagementGraph();
        Add(result.Events, "Events");
        Add(result.DeliveryStates, "DeliveryStates");
        Add(result.Tags, "Tags");
        Add(result.Categories, "Categories");
        Add(result.ModerationRecords, "ModerationRecords");
        Add(result.Locations, "Locations");
        Add(result.Sessions, "Sessions");
        Add(result.SessionGroups, "SessionGroups");
        Add(result.Days, "Days");
        Add(result.AgendaItems, "AgendaItems");
        Add(result.Definitions, "Definitions");
        Add(result.Values, "Values");
        Add(result.Registrations, "Registrations");
        Add(result.Team, "Team");
        Add(result.Templates, "Templates");
        Add(result.SessionTemplates, "SessionTemplates");
        Add(result.History, "History");
        if (document.TryGetProperty("TotalCount", out var count)) result.TotalCount = count.GetInt32();
        return result;

        void Add<T>(List<T> destination, string property) where T : class
        {
            if (document.TryGetProperty(property, out var rows))
                destination.AddRange(rows.EnumerateArray().Select(row => (T)ReadEntity(typeof(T), row)));
        }
        object ReadEntity(Type type, JsonElement row)
        {
            var model = context.Model.FindEntityType(type)!;
            var properties = model.GetProperties().ToArray();
            object?[] values = new object?[properties.Max(p => p.GetIndex()) + 1];
            foreach (var property in properties)
            {
                // Every selected entity carries every mapped scalar, including concurrency,
                // provenance, lifecycle and private fields. Missing fields fail, never default.
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
            var materializer = context.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(model);
            object entity = materializer(new MaterializationContext(new ValueBuffer(values), context));
            foreach (var navigation in model.GetNavigations())
            {
                if (!row.TryGetProperty(navigation.Name, out var nested) || nested.ValueKind == JsonValueKind.Null) continue;
                if (navigation.IsCollection)
                    foreach (var child in nested.EnumerateArray())
                        Connect(entity, navigation, ReadEntity(navigation.TargetEntityType.ClrType, child));
                else Connect(entity, navigation, ReadEntity(navigation.TargetEntityType.ClrType, nested));
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
