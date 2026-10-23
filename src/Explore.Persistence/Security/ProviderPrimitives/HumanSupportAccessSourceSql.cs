using Explore.Application.Settings.Groups;
using Explore.Domain;

namespace Explore.Persistence.Security;

internal sealed class HumanSupportAccessSourceSql(ExploreDbContext context)
{
    internal static readonly (Type Type, string[] Columns)[] Sources =
    [
        (typeof(SupportAccessSession), ["Id", "ActorUserId", "TargetTenantId", "TargetTenantUserId", "StatusId",
            "ModeId", "StartedAtUtc", "ExpiresAtUtc", "EndedAtUtc", "ReasonCode", "TicketReference", "ConcurrencyStamp"]),
        (typeof(SystemSetting), ["Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked",
            "Description", "Category", "AllowedValues"])
    ];

    internal HumanPrincipalSourceSql Names { get; } = new(context);
    private string Fields(int index) => string.Join(",", Sources[index].Columns.Select(property =>
        $"{Names.Column(Sources[index].Type, property)} AS {Names.Quote(property)}"));
    private string Session(string property) => Names.Column(typeof(SupportAccessSession), property);
    internal string[] Queries =>
    [
        $"""
        SELECT {Fields(0)} FROM {Names.Table(typeof(SupportAccessSession))}
        WHERE {Session("Id")} = @session AND {Session("ActorUserId")} = @actor
            AND (@resolved IS NULL OR {Session("TargetTenantId")} = @resolved)
            AND (@tenant IS NULL OR {Session("TargetTenantId")} = @tenant)
        """,
        $"""
        SELECT {Fields(1)} FROM {Names.Table(typeof(SystemSetting))}
        WHERE {Names.Column(typeof(SystemSetting), "SettingKey")} IN
            ({string.Join(",", SupportAccessSettingGroup.SettingKeys.Select(key => "'" + key.Replace("'", "''", StringComparison.Ordinal) + "'"))})
        """
    ];
}
