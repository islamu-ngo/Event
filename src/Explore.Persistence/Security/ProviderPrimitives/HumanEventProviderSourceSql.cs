using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed source input shared by physical reads, compulsory writers, privilege admission and
/// installation. Event predicates include deleted rows and sibling rows: absence and unchanged
/// siblings are evidence, not authorization to mutate them.
/// </summary>
internal sealed class HumanEventProviderSourceSql(ExploreDbContext context, bool current = false)
{
    internal static readonly (HumanEventProviderSource Source, Type Type)[] Sources =
    [
        (HumanEventProviderSource.Binding, typeof(RegistrationProviderBinding)),
        (HumanEventProviderSource.Connection, typeof(RegistrationProviderConnection)),
        (HumanEventProviderSource.Capability, typeof(RegistrationProviderCapability)),
        (HumanEventProviderSource.ApprovedOrigin, typeof(RegistrationProviderApprovedOrigin)),
        (HumanEventProviderSource.Form, typeof(RegistrationForm)),
        (HumanEventProviderSource.Version, typeof(RegistrationFormVersion)),
        (HumanEventProviderSource.Section, typeof(RegistrationFormSection)),
        (HumanEventProviderSource.Field, typeof(RegistrationFormField)),
        (HumanEventProviderSource.Option, typeof(RegistrationFormFieldOption)),
        (HumanEventProviderSource.Rule, typeof(RegistrationFormRule)),
        (HumanEventProviderSource.FieldMapping, typeof(RegistrationProviderFieldMapping)),
        (HumanEventProviderSource.OptionMapping, typeof(RegistrationProviderOptionMapping)),
        (HumanEventProviderSource.Schema, typeof(RegistrationProviderSchemaRevision)),
        (HumanEventProviderSource.Subscription, typeof(RegistrationProviderSubscriptionState)),
        (HumanEventProviderSource.Submission, typeof(RegistrationSubmission)),
        (HumanEventProviderSource.Issue, typeof(RegistrationSubmissionIssue)),
        (HumanEventProviderSource.Effect, typeof(IncomingWebhookEffectOutbox)),
        (HumanEventProviderSource.Message, typeof(IncomingWebhookMessage)),
        (HumanEventProviderSource.Receipt, typeof(IncomingWebhookEffectReceipt)),
        (HumanEventProviderSource.Workflow, typeof(RegistrationWorkflow)),
        (HumanEventProviderSource.Requirement, typeof(RegistrationRequirement)),
        (HumanEventProviderSource.Channel, typeof(RegistrationChannel)),
        (HumanEventProviderSource.SecretBinding, typeof(SecretBinding))
    ];

    internal HumanPrincipalSourceSql Names { get; } = new(context);
    internal IProperty[] Properties(Type type) => context.Model.FindEntityType(type)!.GetProperties()
        .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
    internal string C(Type type, string alias, string property) => $"{alias}.{Names.Column(type, property)}";
    private string C<T>(string alias, string property) => C(typeof(T), alias, property);
    private string Current => current && context.Database.ProviderName == RelationalNamedLock.MySqlProvider ? " FOR UPDATE" : "";
    private string Exists<T>(string alias, string predicate) =>
        $"EXISTS (SELECT 1 FROM {Names.Table(typeof(T))} {alias}{(current && context.Database.IsSqlServer() ? " WITH (UPDLOCK,HOLDLOCK)" : "")} WHERE {predicate}{Current})";
    private string Tenant<T>(string alias) => $"{C<T>(alias, "TenantId")} = @tenant";
    private string Form(string alias) => $"{Tenant<RegistrationForm>(alias)} AND {C<RegistrationForm>(alias, "EventId")} = @event";
    private string Binding(string alias) => $"{Tenant<RegistrationProviderBinding>(alias)} AND ("
        + $"{C<RegistrationProviderBinding>(alias, "Id")} = @binding OR "
        + Exists<RegistrationForm>("bf", $"{Form("bf")} AND {C<RegistrationForm>("bf", "Id")} = {C<RegistrationProviderBinding>(alias, "RegistrationFormId")}") + ")";
    private string Connection(string alias) => $"{Tenant<RegistrationProviderConnection>(alias)} AND ("
        + $"{C<RegistrationProviderConnection>(alias, "Id")} = @connection OR "
        + Exists<RegistrationProviderBinding>("cb", $"{Binding("cb")} AND {C<RegistrationProviderBinding>("cb", "RegistrationProviderConnectionId")} = {C<RegistrationProviderConnection>(alias, "Id")}")
        + " OR " + Exists<RegistrationFormVersion>("cv", $"{Tenant<RegistrationFormVersion>("cv")} AND {C<RegistrationFormVersion>("cv", "EventId")} = @event"
            + $" AND {C<RegistrationFormVersion>("cv", "ExternalRegistrationProviderConnectionId")} = {C<RegistrationProviderConnection>(alias, "Id")}") + ")";
    private string DecisionPrefix(string expression)
    {
        if (context.Database.IsNpgsql()) return $"split_part({expression},':',1)";
        if (context.Database.IsSqlServer()) return $"LEFT({expression},CHARINDEX(':',{expression} + ':') - 1)";
        if (context.Database.IsSqlite()) return $"substr({expression},1,instr({expression} || ':',':') - 1)";
        return $"SUBSTRING_INDEX({expression},':',1)";
    }
    private string CompactId(string expression) => context.Database.IsNpgsql()
        ? $"replace(CAST({expression} AS text),'-','')"
        : context.Database.IsSqlServer() ? $"replace(CONVERT(varchar(36),{expression}),'-','')"
        : $"replace({expression},'-','')";
    private string BindingDecision(string expression) => Exists<RegistrationProviderBinding>("db",
        $"{Binding("db")} AND {CompactId(C<RegistrationProviderBinding>("db", "Id"))} = {DecisionPrefix(expression)}");
    private string Effect(string alias) => $"{Tenant<IncomingWebhookEffectOutbox>(alias)} AND ("
        + $"{C<IncomingWebhookEffectOutbox>(alias, "Id")} = @effect OR "
        + BindingDecision(C<IncomingWebhookEffectOutbox>(alias, "ProviderDecisionId")) + ")";
    private string Message(string alias) => $"{Tenant<IncomingWebhookMessage>(alias)} AND ("
        + BindingDecision(C<IncomingWebhookMessage>(alias, "ProviderMessageId")) + " OR "
        + Exists<IncomingWebhookEffectOutbox>("me", $"{Effect("me")} AND {C<IncomingWebhookEffectOutbox>("me", "IncomingWebhookMessageId")} = {C<IncomingWebhookMessage>(alias, "Id")}") + ")";

    internal string Predicate(HumanEventProviderSource source, Type type, string alias)
    {
        string tenant = source == HumanEventProviderSource.SecretBinding
            ? $"{C(type, alias, "ScopeId")} = @tenant"
            : $"{C(type, alias, "TenantId")} = @tenant";
        string ChildBinding() => tenant + " AND " + Exists<RegistrationProviderBinding>("pb",
            $"{Binding("pb")} AND {C<RegistrationProviderBinding>("pb", "Id")} = {C(type, alias, "RegistrationProviderBindingId")}");
        string ChildConnection() => tenant + " AND " + Exists<RegistrationProviderConnection>("pc",
            $"{Connection("pc")} AND {C<RegistrationProviderConnection>("pc", "Id")} = {C(type, alias, "RegistrationProviderConnectionId")}");
        return source switch
        {
            HumanEventProviderSource.Binding => Binding(alias),
            HumanEventProviderSource.Connection => Connection(alias),
            HumanEventProviderSource.Capability or HumanEventProviderSource.FieldMapping
                or HumanEventProviderSource.OptionMapping or HumanEventProviderSource.Subscription => ChildBinding(),
            HumanEventProviderSource.ApprovedOrigin or HumanEventProviderSource.Schema => ChildConnection(),
            HumanEventProviderSource.Effect => Effect(alias),
            HumanEventProviderSource.Message => Message(alias),
            HumanEventProviderSource.Receipt => tenant + " AND " + Exists<IncomingWebhookMessage>("rm",
                $"{Message("rm")} AND {C<IncomingWebhookMessage>("rm", "Id")} = {C(type, alias, "IncomingWebhookMessageId")}"),
            HumanEventProviderSource.SecretBinding => tenant + " AND (" + $"{C(type, alias, "Id")} = @secret OR "
                + Exists<RegistrationProviderConnection>("sc", $"{Connection("sc")} AND ({C<RegistrationProviderConnection>("sc", "ApiTokenSecretBindingId")} = {C(type, alias, "Id")}"
                    + $" OR {C<RegistrationProviderConnection>("sc", "WebhookSecretBindingId")} = {C(type, alias, "Id")})")
                + " OR " + Exists<RegistrationProviderBinding>("sb", $"{Binding("sb")} AND ({C<RegistrationProviderBinding>("sb", "WebhookSecretBindingId")} = {C(type, alias, "Id")}"
                    + $" OR {C(type, alias, "Qualifier")} = {CompactId(C<RegistrationProviderBinding>("sb", "Id"))})") + ")",
            _ => tenant + $" AND ({C(type, alias, "EventId")} = @event"
                + (source == HumanEventProviderSource.Submission ? $" OR {C(type, alias, "Id")} = @submission" : "")
                + (source == HumanEventProviderSource.Form ? $" OR {C(type, alias, "Id")} = @form" : "")
                + (source == HumanEventProviderSource.Version ? $" OR {C(type, alias, "Id")} = @version" : "") + ")"
        };
    }

    internal string Query(HumanEventProviderSource source, Type type, string? originalPredicate = null, bool originalsOnly = false)
    {
        string fields = string.Join(",", Properties(type).Select(property =>
            $"{C(type, "s", property.Name)} AS {Names.Quote(property.Name)}"));
        string hint = current && context.Database.IsSqlServer() ? " WITH (UPDLOCK,HOLDLOCK)" : "";
        string tenantProperty = source == HumanEventProviderSource.SecretBinding ? "ScopeId" : "TenantId";
        string original = $"{C(type, "s", tenantProperty)} = @tenant AND ({originalPredicate})";
        string predicate = originalPredicate is null ? Predicate(source, type, "s")
            : originalsOnly ? original : $"({Predicate(source, type, "s")}) OR ({original})";
        return $"SELECT {fields} FROM {Names.Table(type)} s{hint} WHERE {predicate}{Current}";
    }

    internal static string Native(string sql) => sql
        .Replace("@tenant", "p_tenant", StringComparison.Ordinal)
        .Replace("@event", "p_event", StringComparison.Ordinal)
        .Replace("@binding", "p_binding", StringComparison.Ordinal)
        .Replace("@connection", "p_connection", StringComparison.Ordinal)
        .Replace("@effect", "p_effect", StringComparison.Ordinal)
        .Replace("@submission", "p_submission", StringComparison.Ordinal)
        .Replace("@form", "p_form", StringComparison.Ordinal)
        .Replace("@version", "p_version", StringComparison.Ordinal)
        .Replace("@secret", "p_secret", StringComparison.Ordinal);
}
