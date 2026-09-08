using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EntitlementScopeTypeConfiguration : LookupConfiguration<EntitlementScopeType>
{
    protected override string TableName => "entitlement_scope_types";
}
