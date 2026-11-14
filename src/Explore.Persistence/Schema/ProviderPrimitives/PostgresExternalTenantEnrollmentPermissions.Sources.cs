using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Schema;

public static partial class PostgresExternalTenantEnrollmentPermissions
{
    private static string SourcePredicate(ExploreDbContext context, Source source,
        PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Type)!;
        string shape = string.Join(" AND ", source.Columns.Values.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attname={Text(column.Name)}
                AND a.attnum>0 AND NOT a.attisdropped AND a.attgenerated='' AND a.attidentity=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                AND a.attnotnull={(column.Required ? "true" : "false")})
            """));
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .SingleOrDefault(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        string tenant = metadata is null ? "NOT t.relrowsecurity AND NOT t.relforcerowsecurity" : $"""
            t.relrowsecurity AND t.relforcerowsecurity
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname='tenant_isolation'
                AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, source.Schema))}
                    || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            """;
        var unique = entity.GetKeys().Select(key => (key.Properties, Filter: (string?)null))
            .Concat(entity.GetIndexes().Where(index => index.IsUnique).Select(index => (index.Properties, Filter: index.GetFilter())));
        string keys = string.Join(" AND ", unique.Select(index => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid=t.oid
                AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                AND i.indexprs IS NULL AND ({UniqueFilter(source, index.Properties, index.Filter)})
                AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.id
                    WHERE k.ord<=i.indnkeyatts ORDER BY k.ord)
                    =ARRAY[{string.Join(',', index.Properties.Select(property => Text(source.Columns[property.Name].Name)))}]::name[])
            """));
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND t.relkind='r' AND t.relpersistence='p'
                    AND NOT t.relispartition AND t.relowner={Role(enrollment.MigratorLogin)}
                    AND n.nspname={Text(source.Schema)} AND n.nspowner=t.relowner
                    AND ({tenant}) AND ({shape}) AND ({keys})
                    AND (SELECT count(*)={source.Columns.Count} FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=t.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid
                        AND p.polname<>'tenant_isolation'
                        AND (0::oid=ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},role_id,'MEMBER'))))
                    {(complete ? $"AND pg_catalog.has_schema_privilege({Role(enrollment.RuntimeLogin)},n.oid,'USAGE')" : "")})
            AND ({PostgresTenantCreationPermissions.DeclaredForeignKeysPredicate(context, source.Type, source.Table)})
            """;
    }

    private static string UniqueFilter(Source source, IReadOnlyList<IProperty> properties, string? filter)
    {
        if (filter is null) return "i.indpred IS NULL";
        string property = properties[^1].Name;
        bool actorOwner = source.Type == typeof(Actor) && properties.Count == 1
            && property is "UserId" or "OrganizationId" or "GroupId" or "ExternalActorSubjectId" or "ServicePrincipalId";
        bool membershipActor = source.Type == typeof(TenantUser)
            && properties.Select(value => value.Name).SequenceEqual(["TenantId", "ActorId"]);
        string column = source.Columns[property].Name;
        if ((!actorOwner && !membershipActor) || filter != column + " IS NOT NULL")
            throw new InvalidOperationException("external_enrollment_unique_model_drift");
        return $"pg_catalog.pg_get_expr(i.indpred,i.indrelid)=('(' || pg_catalog.quote_ident({Text(column)}) || ' IS NOT NULL)')";
    }
}
