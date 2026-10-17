using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Persistence.Security;

public static partial class PostgresMixedSettingsContract
{
    private static readonly (string Producer, string Member)[] GovernanceFamilies =
    [
        ("governance-modules", "Modules"), ("governance-organization", "Organization"),
        ("governance-branding", "Branding"), ("governance-domains", "Domains"),
        ("governance-delegation", "Delegation"), ("governance-admin-portal", "AdminPortal"),
        ("governance-mcp", "Mcp"), ("governance-ai", "Ai"), ("governance-render-policy", "RenderPolicy")
    ];

    private sealed record GovernanceField(string Member, string Input, string Key,
        string Kind = "boolean", string? Lock = null, string? LockSource = null);

    private static readonly GovernanceField[] GovernanceFields =
    [
        new("Modules", "EnableIslamicModule", GovernanceSettingKeys.Modules.IslamicEnabled),
        new("Modules", "EnableTechModule", GovernanceSettingKeys.Modules.TechEnabled),
        new("Organization", "RequireOrganizationVerification", GovernanceSettingKeys.Organizations.VerificationRequired),
        new("Organization", "AllowTenantToOmitVerification", GovernanceSettingKeys.Organizations.TenantCanOmitVerification),
        new("Organization", "AllowOrganizationSelfRegistration", GovernanceSettingKeys.Organizations.SelfRegistrationEnabled),
        new("Organization", "AllowGroupSelfRegistration", GovernanceSettingKeys.Groups.SelfRegistrationEnabled),
        new("Branding", "DefaultBrandDisplayName", GovernanceSettingKeys.Branding.DisplayName, "trim", "LockTenantBrandDisplayName"),
        new("Branding", "DefaultBrandLogoUrl", GovernanceSettingKeys.Branding.LogoUrl, "trim", "LockTenantBrandLogoUrl"),
        new("Branding", "DefaultBrandFaviconUrl", GovernanceSettingKeys.Branding.FaviconUrl, "trim", "LockTenantBrandFaviconUrl"),
        new("Branding", "DefaultBrandCustomCssUrl", GovernanceSettingKeys.Branding.CustomCssUrl, "trim", "LockTenantBrandCustomCssUrl"),
        new("Domains", "InstanceBaseDomain", GovernanceSettingKeys.Domains.InstanceBaseDomain, "host"),
        new("Domains", "AdminHost", GovernanceSettingKeys.Domains.AdminHost, "host"),
        new("Domains", "AllowTenantCustomDomains", GovernanceSettingKeys.Domains.AllowTenantCustomDomain),
        new("Domains", "", GovernanceSettingKeys.Domains.TenantSubdomain, "empty", "LockTenantSubdomain"),
        new("Domains", "", GovernanceSettingKeys.Domains.TenantCustomDomain, "empty", "LockTenantCustomDomain"),
        new("Delegation", "AllowTenantSelfServiceRegistration", GovernanceSettingKeys.Tenants.SelfServiceRegistration, "multi"),
        new("Delegation", "AllowTenantWhiteLabeling", GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, "multi"),
        new("Delegation", "DefaultPublicHomePage", GovernanceSettingKeys.Routing.DefaultPublicHomePage, "home", "LockTenantHomePagePreference"),
        new("Delegation", "LockTenantSmtp", GovernanceSettingKeys.TenantDelegation.LockSmtp),
        new("Delegation", "LockTenantStorage", GovernanceSettingKeys.TenantDelegation.LockStorage),
        new("Delegation", "LockTenantAnalytics", GovernanceSettingKeys.TenantDelegation.LockAnalytics),
        new("Delegation", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("AdminPortal", "Enabled", GovernanceSettingKeys.AdminPortal.Enabled),
        new("AdminPortal", "PublicUrl", GovernanceSettingKeys.AdminPortal.PublicUrl, "trim"),
        new("AdminPortal", "AllowTenantAdminAccess", GovernanceSettingKeys.AdminPortal.AllowTenantAdminAccess),
        new("Mcp", "Enabled", GovernanceSettingKeys.Mcp.Enabled, "boolean", "LockTenantMcp", GovernanceSettingKeys.TenantDelegation.LockMcp),
        new("Mcp", "EnableLegacySse", GovernanceSettingKeys.Mcp.EnableLegacySse, "boolean", "LockTenantMcpLegacySse", GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse),
        new("Mcp", "LockTenantMcp", GovernanceSettingKeys.TenantDelegation.LockMcp),
        new("Mcp", "LockTenantMcpLegacySse", GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse),
        new("Ai", "Enabled", GovernanceSettingKeys.AiAssistant.Enabled, "boolean", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "ProviderConfiguration", GovernanceSettingKeys.AiAssistant.Provider, "ai-provider", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "ProviderConfiguration", GovernanceSettingKeys.AiAssistant.EndpointUrl, "ai-endpoint", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "ProviderConfiguration", GovernanceSettingKeys.AiAssistant.CredentialScope, "ai-credential", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "ProviderConfiguration", GovernanceSettingKeys.AiAssistant.ModelId, "ai-model", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "ProviderConfiguration", GovernanceSettingKeys.AiAssistant.AllowedModelIds, "ai-models", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "AllowAnonymousAccess", GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess, "boolean", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "ToolProposalsEnabled", GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled, "boolean", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("Ai", "LockTenantAiAssistant", GovernanceSettingKeys.TenantDelegation.LockAiAssistant),
        new("RenderPolicy", "RenderPolicyPreset", GovernanceSettingKeys.Routing.RenderPolicy.Preset, "render"),
        new("RenderPolicy", "EnableAdvancedRenderPolicyOverrides", GovernanceSettingKeys.Routing.RenderPolicy.AdvancedEnabled, "render"),
        new("RenderPolicy", "GlobalRenderMode", GovernanceSettingKeys.Routing.RenderPolicy.Fallback.RenderMode, "render"),
        new("RenderPolicy", "GlobalPrerenderEnabled", GovernanceSettingKeys.Routing.RenderPolicy.Fallback.PrerenderEnabled, "render"),
        new("RenderPolicy", "PublicSeoRenderMode", GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.RenderMode, "render"),
        new("RenderPolicy", "PublicSeoPrerenderEnabled", GovernanceSettingKeys.Routing.RenderPolicy.PublicSeo.PrerenderEnabled, "render"),
        new("RenderPolicy", "OperationalRenderMode", GovernanceSettingKeys.Routing.RenderPolicy.Operational.RenderMode, "render"),
        new("RenderPolicy", "OperationalPrerenderEnabled", GovernanceSettingKeys.Routing.RenderPolicy.Operational.PrerenderEnabled, "render"),
        new("RenderPolicy", "AdminRenderMode", GovernanceSettingKeys.Routing.RenderPolicy.Admin.RenderMode, "render"),
        new("RenderPolicy", "AdminPrerenderEnabled", GovernanceSettingKeys.Routing.RenderPolicy.Admin.PrerenderEnabled, "render"),
        new("RenderPolicy", "OnboardingRenderMode", GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.RenderMode, "render"),
        new("RenderPolicy", "OnboardingPrerenderEnabled", GovernanceSettingKeys.Routing.RenderPolicy.Onboarding.PrerenderEnabled, "render"),
        new("RenderPolicy", "AllowTenantRenderPolicyOverride", GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride),
        new("RenderPolicy", "LockTenantPublicSeoRenderPolicy", GovernanceSettingKeys.Routing.RenderPolicy.LockTenantPublicSeo),
        new("RenderPolicy", "LockTenantOperationalRenderPolicy", GovernanceSettingKeys.Routing.RenderPolicy.LockTenantOperational),
        new("RenderPolicy", "LockTenantAdminRenderPolicy", GovernanceSettingKeys.Routing.RenderPolicy.LockTenantAdmin)
    ];

    private static string GovernanceRequestInvalid => $"""
        p_tenant IS NOT NULL
        OR pg_catalog.jsonb_typeof(p_request->'Governance') IS DISTINCT FROM 'object'
        OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_request->'Governance')) <> 11
        OR p_request->'Governance'->>'Producer' IS DISTINCT FROM p_request->>'Producer'
        OR p_request->'Governance'->'Actor' IS DISTINCT FROM p_request->'Actor'
        OR p_request->'EventPolicy' IS DISTINCT FROM 'null'::jsonb
        OR p_request->'Onboarding' IS DISTINCT FROM 'null'::jsonb
        OR p_request->'Reporting' IS DISTINCT FROM 'null'::jsonb
        OR CASE p_request->>'Producer'
            {string.Join("\n", GovernanceFamilies.Select(family => $"""
                WHEN {Text(family.Producer)} THEN
                    pg_catalog.jsonb_typeof(p_request->'Governance'->{Text(family.Member)}) IS DISTINCT FROM 'object'
                    OR p_request->'Governance'->{Text(family.Member)}->'UserId' IS DISTINCT FROM p_request->'Actor'
                    OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_request->'Governance'->{Text(family.Member)})) <> 2
                    OR pg_catalog.jsonb_typeof(p_request->'Governance'->{Text(family.Member)}->'Patch') IS DISTINCT FROM 'object'
                    OR {string.Join(" OR ", GovernanceFamilies.Where(other => other != family).Select(other =>
                        $"p_request->'Governance'->{Text(other.Member)} IS DISTINCT FROM 'null'::jsonb"))}
                """))}
            ELSE true END
        """;

    private static string GovernanceStored(ExploreDbContext context, string key)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"(SELECT s.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.Value))} FROM {sql.Table(typeof(SystemSetting))} s"
            + $" WHERE s.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} = {Text(key)})";
    }

    private static string GovernanceDefault(ExploreDbContext context, string key) =>
        $"COALESCE({GovernanceStored(context, key)},{Text(SettingRegistry.Get(key)!.DefaultValue)})";
    private static string GovernanceTrim(string value) => $"pg_catalog.btrim({value},"
        + Text(" \t\r\n\v\f\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000") + ")";
    private static string GovernanceSupplied(string name) => string.IsNullOrEmpty(name) ? "false" : $"(patch->{Text(name)}->'HasValue' = 'true'::jsonb)";
    private static string GovernanceSuppliesValue(string request, string key) =>
        "(" + string.Join(" OR ", GovernanceFields.Where(field => field.Input.Length > 0).Select(field =>
            $"({key} = {Text(field.Key)} AND {request}->'Governance'->{Text(field.Member)}->'Patch'->{Text(field.Input)}->'HasValue' = 'true'::jsonb)")) + ")";

    private static string GovernanceMetadata(string request, string key)
    {
        string rows = string.Join(",", GovernanceFields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Select(name =>
        {
            var definition = SettingRegistry.Get(name)!;
            return $"({Text(name)},{(int)definition.ValueType},{(definition.Description is null ? "NULL" : Text(definition.Description))},{Text(definition.Category)})";
        }));
        return $"""
            IF COALESCE({GovernanceSuppliesValue(request, key)},false) AND NOT EXISTS (
                SELECT 1 FROM (VALUES {rows}) d(setting_key,kind,description,category)
                WHERE d.setting_key = {key} AND after_row->'SettingValueTypeId' = pg_catalog.to_jsonb(d.kind)
                    AND after_row->'AllowedValues' = 'null'::jsonb
                    AND after_row->'Description' IS NOT DISTINCT FROM COALESCE(pg_catalog.to_jsonb(d.description),'null'::jsonb)
                    AND after_row->'Category' = pg_catalog.to_jsonb(d.category) AND after_row->'DisplayOrder' = '0'::jsonb)
            THEN RAISE EXCEPTION 'governance_original_metadata_changed' USING ERRCODE = '42501'; END IF;
            """;
    }
    private static string GovernanceBool(ExploreDbContext context, string key) =>
        $"(pg_catalog.lower({GovernanceTrim(GovernanceDefault(context, key))}) = 'true')";

    private static string GovernanceCompile(ExploreDbContext context)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string OriginalValue(GovernanceField field)
        {
            string raw = field.Input.Length == 0 ? "''" : $"COALESCE(patch->{Text(field.Input)}->>'Value','')";
            string value = field.Kind switch
            {
                "boolean" => $"(patch->{Text(field.Input)}->'Value')::text",
                "trim" => $"pg_catalog.to_jsonb({GovernanceTrim(raw)})::text",
                "host" => $"pg_catalog.to_jsonb(pg_catalog.btrim({GovernanceTrim($"pg_catalog.replace(pg_catalog.replace(pg_catalog.lower({GovernanceTrim(raw)}),'https://',''),'http://','')")},'/'))::text",
                "empty" => Text("\"\""),
                "home" => $"pg_catalog.to_jsonb(CASE WHEN pg_catalog.lower({raw}) = 'landingpage' THEN 'LandingPage'::text ELSE 'EventList'::text END)::text",
                "multi" => $"pg_catalog.to_jsonb((patch->{Text(field.Input)}->>'Value')::boolean AND pg_catalog.lower(({GovernanceDefault(context, GovernanceSettingKeys.Deployment.Mode)})::jsonb #>> '{{}}') = 'multitenant')::text",
                "render" => GovernanceRenderValue(context, field),
                _ => GovernanceAiValue(context, field)
            };
            // An absent lock-only row uses its original resolved default. Existing rows
            // retain their exact bytes, not a normalized or caller-supplied replacement.
            string fallback = field.Kind.StartsWith("ai-", StringComparison.Ordinal) ? GovernanceAiValue(context, field)
                : field.Kind == "empty" ? Text("\"\"") : Text(SettingRegistry.Get(field.Key)!.DefaultValue);
            return $"CASE WHEN {GovernanceSupplied(field.Input)} THEN {value} ELSE COALESCE({GovernanceStored(context, field.Key)},{fallback}) END";
        }
        string branches = string.Join("\n", GovernanceFamilies.Select(family =>
        {
            var fields = GovernanceFields.Where(field => field.Member == family.Member).ToArray();
            string[] inputs = fields.SelectMany(field => new[] { field.Input, field.Lock }).OfType<string>()
                .Where(input => input.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            string inputTypes = string.Join("\n", inputs.Select(input =>
            {
                string kind = input == "ProviderConfiguration" ? "object"
                    : fields.Any(field => field.Lock == input) ? "boolean"
                    : SettingRegistry.Get(fields.First(field => field.Input == input).Key)!.ValueType == SettingValueType.Boolean ? "boolean" : "string";
                return $"""
                    IF {GovernanceSupplied(input)} AND pg_catalog.jsonb_typeof(patch->{Text(input)}->'Value') IS DISTINCT FROM '{kind}'
                        {(kind == "string" ? $"AND patch->{Text(input)}->'Value' IS DISTINCT FROM 'null'::jsonb" : "")}
                    THEN RAISE EXCEPTION 'governance_original_value_type' USING ERRCODE = '42501'; END IF;
                    """;
            }));
            string rows = string.Join(" UNION ALL ", fields.Select(field =>
            {
                string locked = field.Lock is null ? "false"
                    : $"CASE WHEN {GovernanceSupplied(field.Lock)} THEN (patch->{Text(field.Lock)}->>'Value')::boolean ELSE "
                        + (field.LockSource is not null ? GovernanceBool(context, field.LockSource)
                            : $"COALESCE((SELECT s.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.IsLocked))}"
                                + $" FROM {sql.Table(typeof(SystemSetting))} s WHERE s.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} = {Text(field.Key)}),false)")
                        + " END";
                return $"SELECT {Text(field.Key)} AS key,{OriginalValue(field)} AS value,{locked} AS locked,"
                    + $"(NOT {GovernanceSupplied(field.Input)} AND {GovernanceStored(context, field.Key)} IS NOT NULL) AS raw"
                    + $" WHERE {GovernanceSupplied(field.Input)}"
                    + (field.Lock is null ? "" : $" OR {GovernanceSupplied(field.Lock)}");
            }));
            return $"""
                WHEN {Text(family.Producer)} THEN
                    patch := p_request->'Governance'->{Text(family.Member)}->'Patch';
                    IF (SELECT count(*) FROM pg_catalog.jsonb_object_keys(patch)) <> {inputs.Length}
                        OR patch - ARRAY[{string.Join(",", inputs.Select(Text))}] <> pg_catalog.jsonb_build_object()
                        OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(patch) p
                            WHERE pg_catalog.jsonb_typeof(p.value) IS DISTINCT FROM 'object'
                                OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p.value)) <> 2
                                OR pg_catalog.jsonb_typeof(p.value->'HasValue') IS DISTINCT FROM 'boolean'
                                OR NOT (p.value ? 'Value'))
                    THEN RAISE EXCEPTION 'governance_original_patch_shape' USING ERRCODE = '42501'; END IF;
                    {inputTypes}
                    {(family.Member == "Ai" ? """
                        IF patch->'ProviderConfiguration'->'HasValue' = 'true'::jsonb AND (
                            (SELECT count(*) FROM pg_catalog.jsonb_object_keys(patch->'ProviderConfiguration'->'Value')) <> 5
                            OR (patch->'ProviderConfiguration'->'Value') - ARRAY['Provider','EndpointUrl','CredentialScope','ModelId','AllowedModelIds']
                                <> pg_catalog.jsonb_build_object()
                            OR pg_catalog.jsonb_typeof(patch->'ProviderConfiguration'->'Value'->'AllowedModelIds') IS DISTINCT FROM 'array'
                            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(patch->'ProviderConfiguration'->'Value') p
                                WHERE p.key <> 'AllowedModelIds' AND pg_catalog.jsonb_typeof(p.value) NOT IN ('string','null'))
                            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(patch->'ProviderConfiguration'->'Value'->'AllowedModelIds') id
                                WHERE pg_catalog.jsonb_typeof(id) NOT IN ('string','null')))
                        THEN RAISE EXCEPTION 'governance_original_provider_shape' USING ERRCODE = '42501'; END IF;
                        """ : "")}
                    SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                        'Key',e.key,'Value',e.value,'IsLocked',e.locked,'Remove',false,'Raw',e.raw)),'[]'::jsonb)
                    INTO original_effects FROM ({rows}) e;
                """;
        }));
        return $"""
            CASE p_request->>'Producer'
                {branches}
                ELSE RAISE EXCEPTION 'governance_original_required' USING ERRCODE = '42501';
            END CASE;
            IF pg_catalog.jsonb_array_length(original_effects) <> pg_catalog.jsonb_array_length(p_accepted)
                OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(original_effects) expected
                    LEFT JOIN pg_catalog.jsonb_array_elements(p_accepted) actual ON actual->>'Key' = expected->>'Key'
                    WHERE actual IS NULL OR actual->'IsLocked' IS DISTINCT FROM expected->'IsLocked'
                        OR actual->'Remove' IS DISTINCT FROM 'false'::jsonb
                        OR CASE WHEN expected->'Raw' = 'true'::jsonb
                            THEN actual->>'Value' IS DISTINCT FROM expected->>'Value'
                            ELSE (actual->>'Value')::jsonb IS DISTINCT FROM (expected->>'Value')::jsonb END)
            THEN RAISE EXCEPTION 'governance_original_effect_substituted' USING ERRCODE = '42501'; END IF;
            """;
    }

    private static string GovernanceAiValue(ExploreDbContext context, GovernanceField field)
    {
        string NormalizeProvider(string raw, string enabledValue)
        {
            string normalized = $"pg_catalog.lower({GovernanceTrim($"COALESCE({raw},'')")})";
            return $"CASE WHEN {normalized} IN ('fake','openai','openai-compatible','anthropic','anthropic-compatible') THEN {normalized}"
                + $" WHEN {enabledValue} THEN 'openai' ELSE 'none' END";
        }
        string baseProvider = NormalizeProvider($"({GovernanceDefault(context, GovernanceSettingKeys.AiAssistant.Provider)})::jsonb #>> '{{}}'",
            GovernanceBool(context, GovernanceSettingKeys.AiAssistant.Enabled));
        string capturedConfiguration = $"pg_catalog.jsonb_build_object('Provider',({baseProvider}),"
            + $"'EndpointUrl',({GovernanceDefault(context, GovernanceSettingKeys.AiAssistant.EndpointUrl)})::jsonb,"
            + $"'CredentialScope',({GovernanceDefault(context, GovernanceSettingKeys.AiAssistant.CredentialScope)})::jsonb,"
            + $"'ModelId',({GovernanceDefault(context, GovernanceSettingKeys.AiAssistant.ModelId)})::jsonb,"
            + $"'AllowedModelIds',({GovernanceDefault(context, GovernanceSettingKeys.AiAssistant.AllowedModelIds)})::jsonb)";
        string configured = $"(CASE WHEN {GovernanceSupplied("ProviderConfiguration")} THEN patch->'ProviderConfiguration'->'Value'"
            + $" ELSE {capturedConfiguration} END)";
        string enabled = $"CASE WHEN {GovernanceSupplied("Enabled")} THEN (patch->'Enabled'->>'Value')::boolean ELSE {GovernanceBool(context, GovernanceSettingKeys.AiAssistant.Enabled)} END";
        string provider = NormalizeProvider($"{configured}->>'Provider'", enabled);
        string external = $"({provider}) IN ('openai','openai-compatible','anthropic','anthropic-compatible')";
        string compatible = $"({provider}) IN ('openai-compatible','anthropic-compatible')";
        string model = GovernanceTrim($"COALESCE({configured}->>'ModelId','')");
        return field.Kind switch
        {
            "ai-provider" => $"pg_catalog.to_jsonb({provider})::text",
            "ai-endpoint" => $"pg_catalog.to_jsonb(CASE WHEN {compatible} THEN {GovernanceTrim($"COALESCE({configured}->>'EndpointUrl','')")} ELSE '' END)::text",
            "ai-credential" => $"pg_catalog.to_jsonb(CASE WHEN NOT {GovernanceSupplied("ProviderConfiguration")} OR {external} THEN {configured}->>'CredentialScope' ELSE 'none' END)::text",
            "ai-model" => $"pg_catalog.to_jsonb(CASE WHEN {external} THEN {model} ELSE '' END)::text",
            "ai-models" => $"""
                CASE WHEN {external} THEN (SELECT COALESCE(pg_catalog.jsonb_agg(v ORDER BY n),'[]'::jsonb)::text FROM (
                    SELECT DISTINCT ON (pg_catalog.lower({GovernanceTrim("value")})) {GovernanceTrim("value")} AS v,ordinality AS n
                    FROM pg_catalog.jsonb_array_elements_text(pg_catalog.jsonb_build_array({model}) || ({configured}->'AllowedModelIds')) WITH ORDINALITY
                    WHERE {GovernanceTrim("value")} <> '' ORDER BY pg_catalog.lower({GovernanceTrim("value")}),ordinality) models)
                    ELSE '[]' END
                """,
            _ => throw new InvalidOperationException("Unknown closed governance field.")
        };
    }

    private static string GovernanceRenderValue(ExploreDbContext context, GovernanceField field)
    {
        string Preset(string value) => "CASE pg_catalog.lower(" + GovernanceTrim(value) + ")"
            + " WHEN 'allprerendered' THEN 'AllPrerendered' WHEN '2' THEN 'AllPrerendered'"
            + " WHEN 'allinteractiveautonoprerender' THEN 'AllInteractiveAutoNoPrerender' WHEN '3' THEN 'AllInteractiveAutoNoPrerender'"
            + " WHEN 'seobalanced' THEN 'SeoBalanced' WHEN '1' THEN 'SeoBalanced'"
            + " WHEN 'customadvanced' THEN 'CustomAdvanced' WHEN '4' THEN 'CustomAdvanced' ELSE 'AllInteractiveServer' END";
        string Mode(string value) => "CASE pg_catalog.lower(" + GovernanceTrim(value) + ")"
            + " WHEN 'interactivewebassembly' THEN 'InteractiveWebAssembly' WHEN '2' THEN 'InteractiveWebAssembly'"
            + " WHEN 'interactiveauto' THEN 'InteractiveAuto' WHEN '1' THEN 'InteractiveAuto' ELSE 'InteractiveServer' END";
        Dictionary<string, string> Normalize(Dictionary<string, string> values)
        {
            string preset = Preset($"({values["RenderPolicyPreset"]}) #>> '{{}}'");
            string fallback = Mode($"({values["GlobalRenderMode"]}) #>> '{{}}'");
            string globalMode = $"CASE ({preset}) WHEN 'AllInteractiveAutoNoPrerender' THEN 'InteractiveAuto'"
                + $" WHEN 'SeoBalanced' THEN 'InteractiveAuto' WHEN 'AllInteractiveServer' THEN 'InteractiveServer' ELSE ({fallback}) END";
            string globalPrerender = $"CASE ({preset}) WHEN 'AllPrerendered' THEN true WHEN 'CustomAdvanced' THEN "
                + $"({values["GlobalPrerenderEnabled"]}) = 'true'::jsonb ELSE false END";
            var result = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RenderPolicyPreset"] = $"pg_catalog.to_jsonb({preset})",
                ["EnableAdvancedRenderPolicyOverrides"] = $"pg_catalog.to_jsonb(({preset}) = 'CustomAdvanced')",
                ["GlobalRenderMode"] = $"pg_catalog.to_jsonb({globalMode})",
                ["GlobalPrerenderEnabled"] = $"pg_catalog.to_jsonb({globalPrerender})"
            };
            foreach (string section in new[] { "PublicSeo", "Operational", "Admin", "Onboarding" })
            {
                string mode = $"CASE WHEN ({preset}) = 'CustomAdvanced' THEN ({Mode($"({values[section + "RenderMode"]}) #>> '{{}}'")}) ELSE ({globalMode}) END";
                if (section == "Onboarding")
                    mode = $"CASE WHEN ({mode}) = 'InteractiveServer' THEN 'InteractiveAuto' ELSE ({mode}) END";
                result[section + "RenderMode"] = $"pg_catalog.to_jsonb({mode})";
                result[section + "PrerenderEnabled"] = $"pg_catalog.to_jsonb(CASE WHEN ({preset}) = 'CustomAdvanced'"
                    + $" THEN ({values[section + "PrerenderEnabled"]}) = 'true'::jsonb"
                    + (section == "PublicSeo" ? $" WHEN ({preset}) = 'SeoBalanced' THEN true" : "")
                    + $" ELSE ({globalPrerender}) END)";
            }
            return result;
        }
        var original = Normalize(GovernanceFields.Where(item => item.Member == "RenderPolicy" && item.Kind == "render")
            .ToDictionary(item => item.Input, item => $"({GovernanceDefault(context, item.Key)})::jsonb", StringComparer.Ordinal));
        // The original handler normalized its read model before overlaying PATCH inputs.
        // Keep both passes: a newly selected preset must not revive previously ignored rows.
        var supplied = original.ToDictionary(pair => pair.Key,
            pair => $"CASE WHEN {GovernanceSupplied(pair.Key)} THEN patch->{Text(pair.Key)}->'Value' ELSE ({pair.Value}) END",
            StringComparer.Ordinal);
        return $"({Normalize(supplied)[field.Input]})::text";
    }
}
