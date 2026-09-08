using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EntitlementSelectionRuleConfiguration : LookupConfiguration<EntitlementSelectionRule>
{
    protected override string TableName => "entitlement_selection_rules";
}
