namespace Explore.Domain.Constants;

public static class ExternalBindingTypes
{
    public static class External
    {
        public const string ProviderCustomer = "provider-customer";
        public const string ManagedTenantProvisioningOperation = "managed-tenant-provisioning-operation";
        public const string ExternalAdminUser = "external-admin-user";
        public const string ExternalAdminTenantUser = "external-admin-tenant-user";
        public const string ExternalAdminTenantUserProfile = "external-admin-tenant-user-profile";
        public const string ExternalAdminUserActor = "external-admin-user-actor";
        public const string ExternalAdminUserLogin = "external-admin-user-login";
        public const string CustomerOrganization = "customer-organization";
        public const string CustomerOrganizationActor = "customer-organization-actor";
        public const string CustomerGroup = "customer-group";
        public const string CustomerGroupActor = "customer-group-actor";
    }

    public static class Internal
    {
        public const string Tenant = "Tenant";
        public const string User = "User";
        public const string TenantUser = "TenantUser";
        public const string TenantUserProfile = "TenantUserProfile";
        public const string Actor = "Actor";
        public const string UserExternalLogin = "UserExternalLogin";
        public const string Organization = "Organization";
        public const string Group = "Group";
    }
}
