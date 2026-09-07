namespace Explore.Domain.Settings.Documents;

public static class SettingsDocumentKeys
{
    public static class Tenant
    {
        public const string PublicExperience = "tenant.public_experience";
        public const string RenderPolicy = "tenant.render_policy";
        public const string ModuleGovernance = "tenant.module_governance";
        public const string Branding = "tenant.branding";
        public const string DirectoryOperatorIdentity = "tenant.directory_operator_identity";
        public const string EventDefaults = "tenant.event_defaults";

        public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        {
            PublicExperience,
            RenderPolicy,
            ModuleGovernance,
            Branding,
            DirectoryOperatorIdentity,
            EventDefaults,
        };
    }
}
