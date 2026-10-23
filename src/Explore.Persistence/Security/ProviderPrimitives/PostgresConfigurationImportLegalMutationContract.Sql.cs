using Explore.Domain;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportLegalMutationContract
{
    private static string TargetPredicate(ExploreDbContext context, string alias)
    {
        string D(string property) => alias + Column(context, typeof(LegalDocument), property);
        return $"""
            {D("Scope")} = NULLIF(pg_catalog.current_setting('app.legal_import_scope', true), '')::integer
            AND {D("TenantId")} IS NOT DISTINCT FROM
                NULLIF(pg_catalog.current_setting('app.legal_import_tenant', true), '')::uuid
            AND {D("Kind")} = NULLIF(pg_catalog.current_setting('app.legal_import_kind', true), '')::integer
            AND {D("AuthorityKey")} = CASE WHEN {D("Scope")} = 1 THEN 'instance'
                ELSE 'tenant:' || pg_catalog.replace({D("TenantId")}::text, '-', '') END
            AND ({D("Scope")}, {D("Kind")}, {D("OwnerRole")}) IN ({CatalogSql()})
            """;
    }

    private static string CatalogSql() => string.Join(", ", LegalDocumentKindCatalog.Entries.Values
        .OrderBy(entry => entry.Kind).Select(entry => $"({(int)entry.Scope}, {(int)entry.Kind}, {(int)entry.OwnerRole})"));

    private static string TargetBody(ExploreDbContext context) => $"""
        BEGIN
            PERFORM {FunctionSql(context, "assert_authority")}();
            IF p_scope IS NULL OR p_kind IS NULL
                OR (p_scope, p_kind) NOT IN ({string.Join(", ", LegalDocumentKindCatalog.Entries.Values
                    .OrderBy(entry => entry.Kind).Select(entry => $"({(int)entry.Scope}, {(int)entry.Kind})"))})
                OR p_scope = 1 AND p_tenant IS NOT NULL
                OR p_scope = 2 AND (p_tenant IS NULL OR p_tenant = '{Guid.Empty}'::uuid) THEN
                RAISE EXCEPTION 'configuration_import_legal_target_invalid' USING ERRCODE = '22023';
            END IF;
            PERFORM pg_catalog.set_config('app.legal_import_scope', p_scope::text, true);
            PERFORM pg_catalog.set_config('app.legal_import_tenant', COALESCE(p_tenant::text, ''), true);
            PERFORM pg_catalog.set_config('app.legal_import_kind', p_kind::text, true);
            PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(
                'configuration_import_legal:' || p_scope::text || ':' ||
                COALESCE(p_tenant::text, '') || ':' || p_kind::text, 0));
            PERFORM d.{Column(context, typeof(LegalDocument), "Id")}
                FROM {Table(context, typeof(LegalDocument))} d
                WHERE {TargetPredicate(context, "d.")} FOR UPDATE;
            RETURN;
        END;
        """;

    private static string ReadBody(ExploreDbContext context, Type type)
    {
        string documents = Table(context, typeof(LegalDocument));
        string versions = Table(context, typeof(LegalDocumentVersion));
        string D(string name) => Column(context, typeof(LegalDocument), name);
        string V(string name) => Column(context, typeof(LegalDocumentVersion), name);
        string query;
        if (type == typeof(LegalDocument))
            query = $"SELECT d.* FROM {documents} d WHERE {TargetPredicate(context, "d.")}";
        else if (type == typeof(LegalDocumentVersion))
            query = $"""
                SELECT v.* FROM {versions} v JOIN {documents} d ON d.{D("Id")} = v.{V("LegalDocumentId")}
                WHERE {TargetPredicate(context, "d.")} ORDER BY v.{V("Version")}
                """;
        else if (type == typeof(LegalDocumentLocalizedSource))
            query = $"""
                SELECT l.* FROM {Table(context, type)} l
                JOIN {versions} v ON v.{V("Id")} = l.{Column(context, type, "LegalDocumentVersionId")}
                JOIN {documents} d ON d.{D("Id")} = v.{V("LegalDocumentId")}
                WHERE {TargetPredicate(context, "d.")}
                ORDER BY v.{V("Version")}, l.{Column(context, type, "LanguageTag")} COLLATE "C"
                """;
        else
            query = $"""
                SELECT p.* FROM {Table(context, type)} p
                JOIN {documents} d ON d.{D("Id")} = p.{Column(context, type, "LegalDocumentId")}
                WHERE {TargetPredicate(context, "d.")}
                ORDER BY p.{Column(context, type, "Version")}, p.{Column(context, type, "OccurredAt")}
                """;
        return $"""
            BEGIN
                PERFORM {FunctionSql(context, "set_target")}(p_scope, p_tenant, p_kind);
                RETURN QUERY {query};
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context)
    {
        Type documentType = typeof(LegalDocument);
        Type versionType = typeof(LegalDocumentVersion);
        Type sourceType = typeof(LegalDocumentLocalizedSource);
        string documents = Table(context, documentType);
        string versions = Table(context, versionType);
        string sources = Table(context, sourceType);
        string D(string name) => Column(context, documentType, name);
        string V(string name) => Column(context, versionType, name);
        string L(string name) => Column(context, sourceType, name);
        string RequiredShape(string json, Type type, string[] properties) => $"""
            pg_catalog.jsonb_typeof({json}) IS DISTINCT FROM 'object'
            OR NOT ({json} ?& ARRAY[{string.Join(", ", properties.Select(property => Text(ColumnName(context, type, property))))}]::text[])
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_object_keys({json}) field
                WHERE field <> ALL (ARRAY[{string.Join(", ", properties.Select(property => Text(ColumnName(context, type, property))))}]::text[]))
            """;
        string NonemptyUuid(string expression) => $"{expression} IS NULL OR {expression} = '{Guid.Empty}'::uuid";
        string InvalidTime(string expression) => $"""
            {expression} IS NULL OR NOT pg_catalog.isfinite({expression})
            OR {expression} <= '0001-01-01 00:00:00+00'::timestamptz
            OR {expression} >= '10000-01-01 00:00:00+00'::timestamptz
            """;
        string Utf16Length(string expression) =>
            $"(char_length({expression}) + char_length(regexp_replace({expression}, U&'[\\0001-\\FFFF]', '', 'g')))";
        string NonUtcJson(string json, Type type, string property) =>
            $"({json}->>{Text(ColumnName(context, type, property))}) !~ 'Z$'";
        // The Domain digest prefixes UTF-16 code-unit lengths, not PostgreSQL's
        // Unicode scalar lengths. Each supplementary code point contributes two.
        const string AppendDigest = """
            v_digest := v_digest ||
                (pg_catalog.char_length(v_piece) +
                    pg_catalog.char_length(pg_catalog.regexp_replace(v_piece, U&'[\0001-\FFFF]', '', 'g')))::text
                || ':' || v_piece || ';';
            """;
        string Insert(string table, Type type, string[] properties, string row) =>
            $"INSERT INTO {table} ({string.Join(", ", properties.Select(property => Column(context, type, property)))}) " +
            $"VALUES ({string.Join(", ", properties.Select(property => row + "." + Column(context, type, property)))});";
        return $$"""
            DECLARE
                r {{documents}}%ROWTYPE;
                v {{versions}}%ROWTYPE;
                l {{sources}}%ROWTYPE;
                v_source jsonb;
                v_piece text;
                v_digest text := '';
                v_count integer;
                v_language text;
            BEGIN
                PERFORM {{FunctionSql(context, "set_target")}}(p_scope, p_tenant, p_kind);
                IF {{RequiredShape("p_document", documentType, RootColumns)}}
                    OR {{RequiredShape("p_version", versionType, VersionColumns)}}
                    OR pg_catalog.jsonb_typeof(p_sources) IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_array_length(p_sources) NOT BETWEEN 1 AND {{LegalMarkdownContentLimits.MaximumLocalesPerDocument}} THEN
                    RAISE EXCEPTION 'configuration_import_legal_payload_invalid' USING ERRCODE = '22023';
                END IF;
                SELECT * INTO r FROM pg_catalog.jsonb_populate_record(NULL::{{documents}}, p_document);
                SELECT * INTO v FROM pg_catalog.jsonb_populate_record(NULL::{{versions}}, p_version);
                IF {{NonemptyUuid($"r.{D("Id")}")}} OR {{NonemptyUuid($"r.{D("ConcurrencyStamp")}")}}
                    OR {{NonemptyUuid($"v.{V("Id")}")}} OR {{InvalidTime($"r.{D("CreatedAt")}")}}
                    OR {{InvalidTime($"v.{V("CreatedAt")}")}}
                    OR {{NonUtcJson("p_document", documentType, "CreatedAt")}}
                    OR {{NonUtcJson("p_version", versionType, "CreatedAt")}}
                    OR r.{{D("UpdatedAt")}} IS NOT NULL AND {{NonUtcJson("p_document", documentType, "UpdatedAt")}}
                    OR r.{{D("CreatedBy")}} = '{{Guid.Empty}}'::uuid OR r.{{D("UpdatedBy")}} = '{{Guid.Empty}}'::uuid
                    OR r.{{D("Scope")}} IS DISTINCT FROM p_scope OR r.{{D("TenantId")}} IS DISTINCT FROM p_tenant
                    OR r.{{D("Kind")}} IS DISTINCT FROM p_kind
                    OR r.{{D("OwnerRole")}} IS NULL
                    OR (p_scope, p_kind, r.{{D("OwnerRole")}}) NOT IN ({{CatalogSql()}})
                    OR r.{{D("AuthorityKey")}} IS DISTINCT FROM (CASE WHEN p_scope = 1 THEN 'instance'
                        ELSE 'tenant:' || pg_catalog.replace(p_tenant::text, '-', '') END)
                    OR r.{{D("State")}} IS DISTINCT FROM 2 OR r.{{D("AccountableIdentityReference")}} IS NOT NULL
                    OR v.{{V("LegalDocumentId")}} IS DISTINCT FROM r.{{D("Id")}}
                    OR v.{{V("Version")}} IS DISTINCT FROM r.{{D("CurrentVersion")}}
                    OR v.{{V("Version")}} IS NULL OR v.{{V("Version")}} < 1
                    OR v.{{V("State")}} IS DISTINCT FROM 2 OR v.{{V("Audience")}} IS NULL
                    OR v.{{V("Audience")}} NOT IN ({{string.Join(", ", Enum.GetValues<LegalDocumentAudience>().Select(value => (int)value))}})
                    OR v.{{V("SourceOrigin")}} IS NULL OR length(v.{{V("SourceOrigin")}}) <> 71
                    OR v.{{V("SourceOrigin")}} !~ '^sha256:[0-9a-f]{64}$'
                    OR v.{{V("ContentDigest")}} IS NULL OR length(v.{{V("ContentDigest")}}) <> 64
                    OR v.{{V("ContentDigest")}} !~ '^[0-9a-f]{64}$'
                    OR v.{{V("RequiresFreshAcceptance")}} IS NULL
                    OR v.{{V("ReviewerId")}} IS NOT NULL OR v.{{V("ReviewEvidenceReference")}} IS NOT NULL
                    OR v.{{V("AccountableIdentityReference")}} IS NOT NULL OR v.{{V("ApprovedAt")}} IS NOT NULL
                    OR v.{{V("ProposedEffectiveAt")}} IS NOT NULL OR v.{{V("PublishedAt")}} IS NOT NULL
                    OR v.{{V("RetiredAt")}} IS NOT NULL THEN
                    RAISE EXCEPTION 'configuration_import_legal_revision_invalid' USING ERRCODE = '22023';
                END IF;
                IF NOT ((v.{{V("TemplateId")}} IS NULL AND v.{{V("TemplateVersion")}} IS NULL
                        AND v.{{V("TemplateSourceKind")}} IS NULL AND v.{{V("TemplateLicenseExpression")}} IS NULL
                        AND v.{{V("TemplateReviewReference")}} IS NULL)
                    OR (v.{{V("TemplateId")}} IS NOT NULL AND v.{{V("TemplateVersion")}} IS NOT NULL
                        AND v.{{V("TemplateSourceKind")}} IS NOT NULL AND v.{{V("TemplateLicenseExpression")}} IS NOT NULL
                        AND v.{{V("TemplateReviewReference")}} IS NOT NULL
                        AND v.{{V("TemplateSourceKind")}} IN (1, 2)
                        AND {{Utf16Length($"v.{V("TemplateId")}")}} BETWEEN 1 AND 100
                        AND {{Utf16Length($"v.{V("TemplateVersion")}")}} BETWEEN 1 AND 50
                        AND {{Utf16Length($"v.{V("TemplateLicenseExpression")}")}} BETWEEN 1 AND 100
                        AND {{Utf16Length($"v.{V("TemplateReviewReference")}")}} BETWEEN 1 AND 200
                        AND v.{{V("TemplateId")}} ~ '[^[:space:]]' AND v.{{V("TemplateVersion")}} ~ '[^[:space:]]'
                        AND v.{{V("TemplateLicenseExpression")}} ~ '[^[:space:]]' AND v.{{V("TemplateReviewReference")}} ~ '[^[:space:]]'
                        AND v.{{V("TemplateId")}} = btrim(v.{{V("TemplateId")}})
                        AND v.{{V("TemplateVersion")}} = btrim(v.{{V("TemplateVersion")}})
                        AND v.{{V("TemplateLicenseExpression")}} = btrim(v.{{V("TemplateLicenseExpression")}})
                        AND v.{{V("TemplateReviewReference")}} = btrim(v.{{V("TemplateReviewReference")}}))) THEN
                    RAISE EXCEPTION 'configuration_import_legal_provenance_invalid' USING ERRCODE = '22023';
                END IF;
                FOR v_piece IN SELECT unnest(ARRAY[
                    v.{{V("Audience")}}::text, v.{{V("SourceOrigin")}},
                    CASE WHEN v.{{V("RequiresFreshAcceptance")}} THEN '1' ELSE '0' END,
                    COALESCE(v.{{V("TemplateId")}}, ''), COALESCE(v.{{V("TemplateVersion")}}, ''),
                    CASE v.{{V("TemplateSourceKind")}} WHEN 1 THEN 'ProjectOwned' WHEN 2 THEN 'ApprovedFoss' ELSE '' END,
                    COALESCE(v.{{V("TemplateLicenseExpression")}}, ''), COALESCE(v.{{V("TemplateReviewReference")}}, '')]) LOOP
                    {{AppendDigest}}
                END LOOP;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_sources) item
                    GROUP BY lower((item->>{{Text(ColumnName(context, sourceType, "LanguageTag"))}}) COLLATE "C") HAVING count(*) > 1)
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_sources) item
                        GROUP BY item->>{{Text(ColumnName(context, sourceType, "Id"))}} HAVING count(*) > 1) THEN
                    RAISE EXCEPTION 'configuration_import_legal_duplicate_locale' USING ERRCODE = '22023';
                END IF;
                FOR v_source IN SELECT item FROM pg_catalog.jsonb_array_elements(p_sources) item
                    ORDER BY item->>{{Text(ColumnName(context, sourceType, "LanguageTag"))}} COLLATE "C" LOOP
                    IF {{RequiredShape("v_source", sourceType, SourceColumns)}} THEN
                        RAISE EXCEPTION 'configuration_import_legal_source_invalid' USING ERRCODE = '22023';
                    END IF;
                    SELECT * INTO l FROM pg_catalog.jsonb_populate_record(NULL::{{sources}}, v_source);
                    IF {{NonemptyUuid($"l.{L("Id")}")}} OR l.{{L("LegalDocumentVersionId")}} IS DISTINCT FROM v.{{V("Id")}}
                        OR l.{{L("LanguageTag")}} IS NULL OR length(l.{{L("LanguageTag")}}) NOT BETWEEN 1 AND {{LegalMarkdownContentLimits.MaximumLanguageTagLength}}
                        OR l.{{L("LanguageTag")}} !~ '^([a-z]{2,3}(-[A-Za-z0-9]{1,8})*|x(-[A-Za-z0-9]{1,8})+)$'
                        OR l.{{L("Title")}} IS NULL OR {{Utf16Length($"l.{L("Title")}")}} NOT BETWEEN 1 AND {{LegalMarkdownContentLimits.MaximumTitleLength}}
                        OR l.{{L("Summary")}} IS NULL OR {{Utf16Length($"l.{L("Summary")}")}} NOT BETWEEN 1 AND {{LegalMarkdownContentLimits.MaximumSummaryLength}}
                        OR l.{{L("Title")}} <> btrim(l.{{L("Title")}}) OR l.{{L("Summary")}} <> btrim(l.{{L("Summary")}})
                        OR l.{{L("Title")}} !~ '[^[:space:]]' OR l.{{L("Summary")}} !~ '[^[:space:]]'
                        OR l.{{L("Markdown")}} IS NULL OR octet_length(l.{{L("Markdown")}}) NOT BETWEEN 1 AND {{LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale}}
                        OR position(chr(13) IN l.{{L("Markdown")}}) > 0
                        OR l.{{L("Utf8ByteCount")}} IS DISTINCT FROM octet_length(l.{{L("Markdown")}})
                        OR l.{{L("LinkCount")}} IS NULL OR l.{{L("LinkCount")}} NOT BETWEEN 0 AND {{LegalMarkdownContentLimits.MaximumLinksPerLocale}}
                        OR l.{{L("PlaceholderCount")}} IS NULL OR l.{{L("PlaceholderCount")}} NOT BETWEEN 0 AND {{LegalMarkdownContentLimits.MaximumPlaceholdersPerLocale}} THEN
                        RAISE EXCEPTION 'configuration_import_legal_source_invalid' USING ERRCODE = '22023';
                    END IF;
                    SELECT string_agg(CASE WHEN ordinal = 1 THEN lower(segment COLLATE "C")
                        WHEN length(segment) = 2 AND segment ~ '^[A-Za-z]+$' THEN upper(segment COLLATE "C")
                        ELSE lower(segment COLLATE "C") END, '-' ORDER BY ordinal) INTO v_language
                        FROM unnest(string_to_array(l.{{L("LanguageTag")}}, '-')) WITH ORDINALITY parts(segment, ordinal);
                    IF v_language IS DISTINCT FROM l.{{L("LanguageTag")}} THEN
                        RAISE EXCEPTION 'configuration_import_legal_locale_invalid' USING ERRCODE = '22023';
                    END IF;
                    FOR v_piece IN SELECT unnest(ARRAY[l.{{L("LanguageTag")}}, l.{{L("Title")}},
                        l.{{L("Summary")}}, l.{{L("Markdown")}}]) LOOP
                        {{AppendDigest}}
                    END LOOP;
                END LOOP;
                IF encode(sha256(convert_to(v_digest, 'UTF8')), 'hex') IS DISTINCT FROM v.{{V("ContentDigest")}} THEN
                    RAISE EXCEPTION 'configuration_import_legal_digest_invalid' USING ERRCODE = '22023';
                END IF;
                IF p_expected_stamp IS NULL THEN
                    IF p_expected_version IS NOT NULL OR p_expected_state IS NOT NULL
                        OR r.{{D("CurrentVersion")}} <> 1 OR r.{{D("UpdatedAt")}} IS NOT NULL
                        OR r.{{D("UpdatedBy")}} IS NOT NULL
                        OR r.{{D("CreatedAt")}} IS DISTINCT FROM v.{{V("CreatedAt")}} THEN
                        RAISE EXCEPTION 'configuration_import_legal_new_target_invalid' USING ERRCODE = '22023';
                    END IF;
                    -- No upsert: the unique authority/kind constraint arbitrates a
                    -- competing ordinary insert even when it does not take our lock.
                    {{Insert(documents, documentType, RootInserts, "r")}}
                ELSE
                    IF p_expected_stamp = '{{Guid.Empty}}'::uuid OR p_expected_version IS NULL
                        OR p_expected_version < 1 OR p_expected_state IS NULL OR p_expected_state NOT IN (5, 6)
                        OR r.{{D("CurrentVersion")}} <> p_expected_version + 1
                        OR r.{{D("ConcurrencyStamp")}} = p_expected_stamp
                        OR r.{{D("UpdatedAt")}} IS DISTINCT FROM v.{{V("CreatedAt")}} THEN
                        RAISE EXCEPTION 'configuration_import_legal_revision_invalid' USING ERRCODE = '22023';
                    END IF;
                    UPDATE {{documents}} d SET
                        {{string.Join(",\n                        ", RootUpdates.Select(property => $"{D(property)} = r.{D(property)}"))}}
                        WHERE d.{{D("Id")}} = r.{{D("Id")}} AND {{TargetPredicate(context, "d.")}}
                            AND d.{{D("ConcurrencyStamp")}} = p_expected_stamp
                            AND d.{{D("CurrentVersion")}} = p_expected_version AND d.{{D("State")}} = p_expected_state
                            AND d.{{D("CreatedAt")}} = r.{{D("CreatedAt")}}
                            AND d.{{D("CreatedBy")}} IS NOT DISTINCT FROM r.{{D("CreatedBy")}}
                            AND EXISTS (SELECT 1 FROM {{versions}} old
                                WHERE old.{{V("LegalDocumentId")}} = d.{{D("Id")}}
                                    AND old.{{V("Version")}} = p_expected_version AND old.{{V("State")}} = p_expected_state);
                    GET DIAGNOSTICS v_count = ROW_COUNT;
                    IF v_count <> 1 THEN
                        RAISE EXCEPTION 'configuration_import_legal_concurrency_conflict' USING ERRCODE = '40001';
                    END IF;
                END IF;
                {{Insert(versions, versionType, VersionInserts, "v")}}
                FOR v_source IN SELECT item FROM pg_catalog.jsonb_array_elements(p_sources) item LOOP
                    SELECT * INTO l FROM pg_catalog.jsonb_populate_record(NULL::{{sources}}, v_source);
                    {{Insert(sources, sourceType, SourceColumns, "l")}}
                END LOOP;
                RETURN;
            END;
            """;
    }
}
