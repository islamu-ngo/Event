namespace Explore.Domain.Constants;

public static class InfrastructureSecretSettingKeys
{
    public static class Email
    {
        public const string SmtpUsername = "smtp.username";
        public const string SmtpPassword = "smtp.password";
    }

    public static class Storage
    {
        public const string AccessKeyId = "storage.s3.access_key_id";
        public const string SecretAccessKey = "storage.s3.secret_access_key";
    }

    public static class Cerbos
    {
        public const string CustomAdminUsername = "cerbos.custom_admin_username";
        public const string CustomAdminPassword = "cerbos.custom_admin_password";
    }

    public static class Authentication
    {
        public const string KeycloakClientSecret = "auth.keycloak_client_secret";
        public const string GoogleClientSecret = "auth.google_client_secret";
    }

    public static class Reporting
    {
        public const string OspreyApiKey = "reporting.osprey_api_key";
        public const string OspreyWebhookSecret = "reporting.osprey_webhook_secret";
        public const string CoopApiKey = "reporting.coop_api_key";
        public const string CoopWebhookSecret = "reporting.coop_webhook_secret";
    }

    public static class Integrations
    {
        public static class Listmonk
        {
            public const string ApiUsername = "integrations.listmonk.api_username";
            public const string ApiKey = "integrations.listmonk.api_key";
        }
    }

    public static class Localization
    {
        public const string TmsApiKey = "localization.tms_api_key";
    }

    public static class Management
    {
        public const string ControlPlaneRegistrationCredentials =
            "management.control_plane_registration_credentials";
    }
}
