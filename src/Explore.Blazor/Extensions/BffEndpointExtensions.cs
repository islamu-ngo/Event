// ABOUTME: Façade that maps all BFF server endpoints by delegating to bounded-context endpoint groups.
// ABOUTME: Keeps Program.cs stable while auth, preference, storage, and setup-secret endpoints live in dedicated files.

namespace Explore.Blazor.Extensions;

public static class BffEndpointExtensions
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        return BffAuthEndpoints.MapAuthEndpoints(app);
    }

    public static WebApplication MapBffEndpoints(this WebApplication app)
    {
        app.MapManifestEndpoints();
        app.MapConfigurationManifestEndpoints();
        app.MapPreferenceEndpoints();
        app.MapStorageEndpoints();
        app.MapSetupSecretEndpoints();
        app.MapLocalCredentialEndpoints();
        app.MapLocalIdentityLifecycleEndpoints();
        app.MapSupportAccessEndpoints();
        app.MapRegistrationProviderEmbedEndpoints();
        app.MapRegistrationPaymentEndpoints();
        app.MapTicketPurchaseEndpoints();
        app.MapParticipantReadinessEndpoints();
        app.MapTicketTransferEndpoints();
        app.MapFairReturnWaitlistEndpoints();
        app.MapEventAddOnBff();
        app.MapAdmissionRecoveryEndpoints();
        app.MapAtprotoOAuthEndpoints();

        return app;
    }
}
