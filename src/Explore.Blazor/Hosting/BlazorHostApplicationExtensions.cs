// ABOUTME: Applies the reusable ordered Explore.Blazor middleware pipeline and endpoint graph.
// ABOUTME: Maps YARP and proxy antiforgery only for the registered Split transport profile.

using Blazouter.Extensions;
using Blazouter.Server.Extensions;
using Event.Web.BffHosting.Proxy;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Components;
using Explore.Blazor.Extensions;
using Explore.Blazor.HealthChecks;

namespace Explore.Blazor.Hosting;

public static class BlazorHostApplicationExtensions
{
    public static async Task<WebApplication> InitializeBlazorHostAsync(
        this WebApplication app,
        BlazorHostProfile profile)
    {
        ValidateProfile(app, profile);
        if (profile == BlazorHostProfile.Split)
        {
            bool requireApiReadyOnStart =
                !app.Environment.IsEnvironment("Testing")
                || app.Configuration.GetValue<bool>("Blazor:RequireApiReadyOnStart");
            if (requireApiReadyOnStart)
            {
                await using var scope = app.Services.CreateAsyncScope();
                var probe = scope.ServiceProvider.GetRequiredService<IExploreApiReadinessProbe>();
                try
                {
                    await probe.EnsureReadyAsync(app.Lifetime.ApplicationStopping)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is Explore.Blazor.Client.Clients.ApiException
                    or HttpRequestException
                    or TaskCanceledException)
                {
                    throw new InvalidOperationException(
                        "The split Blazor host cannot start because the Explore API is not ready.",
                        exception);
                }
            }

            await app.InitializeDynamicAuthSchemesAsync();
        }

        return app;
    }

    public static WebApplication UseBlazorHostMiddleware(
        this WebApplication app,
        BlazorHostProfile profile,
        GracefulShutdownState shutdownState,
        Func<HttpContext, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(shutdownState);
        ValidateProfile(app, profile);

        if (profile == BlazorHostProfile.Split)
        {
            app.UseEventBffForwardedHeaders();
        }

        string? publicPathBase = ResolvePublicPathBase(app.Configuration);
        if (publicPathBase is not null)
        {
            app.UsePathBase(publicPathBase);
        }

        app.UseEventBffAdminHostAccessControl();
        if (profile == BlazorHostProfile.Split)
        {
            app.ConfigureGracefulShutdown(shutdownState);
        }

        app.UseBffSecurityHeaders();
        if (profile == BlazorHostProfile.Split)
        {
            app.MapDefaultEndpoints();
        }

        if (predicate is not null)
        {
            app.UseWhen(predicate, branch => UseBlazorHostMiddlewareCore(branch, app, profile));
            return app;
        }

        UseBlazorHostMiddlewareCore(app, app, profile);
        return app;
    }

    private static string? ResolvePublicPathBase(IConfiguration configuration)
    {
        string? value = configuration["PublicBaseUrl"]
            ?? configuration["App:PublicBaseUrl"]
            ?? configuration["Application:PublicBaseUrl"];
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment) &&
               uri.AbsolutePath != "/"
            ? uri.AbsolutePath.TrimEnd('/')
            : null;
    }

    private static void UseBlazorHostMiddlewareCore(
        IApplicationBuilder pipeline,
        WebApplication app,
        BlazorHostProfile profile)
    {
        if (app.Environment.IsDevelopment())
        {
            pipeline.UseWebAssemblyDebugging();
            pipeline.UseDeveloperExceptionPage();
        }
        else
        {
            pipeline.UseExceptionHandler("/Error", createScopeForErrors: true);
            pipeline.UseHsts();
        }

        pipeline.UseWhen(
            context => !context.Request.Path.StartsWithSegments(
                "/bff",
                StringComparison.OrdinalIgnoreCase),
            branch => branch.UseStatusCodePagesWithReExecute(
                "/errors/{0}",
                createScopeForStatusCodePages: true));
        pipeline.UseHttpsRedirection();
        pipeline.UseStartupRedirectMiddleware(app);
        pipeline.UsePathTenantResolverMiddleware();
        pipeline.UseLocalCredentialPrivacyHeaders();
        pipeline.UseRouting();
        pipeline.UseAuthentication();
        pipeline.UseAdmissionCheckInBffTransport();
        pipeline.UseAntiforgeryTokenMiddleware(app);
        pipeline.UseRequestLocalization();
        pipeline.UseAccessTokenCaptureMiddleware();
        pipeline.UseBffDiagnosticsMiddleware(app);
        pipeline.UseOnboardingAuthGateMiddleware();
        pipeline.UseAuthorization();
        if (profile == BlazorHostProfile.Split)
        {
            pipeline.UseEventApiProxyAntiforgery();
        }

        pipeline.UseBffEndpointAntiforgery();
        pipeline.UseRateLimiter();
        pipeline.UseAntiforgery();
    }

    public static WebApplication MapBlazorHostEndpoints(
        this WebApplication app,
        BlazorHostProfile profile)
    {
        ValidateProfile(app, profile);

        if (profile == BlazorHostProfile.Split)
        {
            app.MapControllers();
        }

        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/test-endpoint",
                () => Results.Ok(new
                {
                    message = "Server endpoint works!",
                    timestamp = DateTime.UtcNow
                }))
                .WithName("TestEndpoint");
        }

        BffAuthEndpoints.MapAuthEndpoints(app);
        BffEndpointExtensions.MapBffEndpoints(app);
        app.MapStaticAssets();

        if (profile == BlazorHostProfile.Split)
        {
            app.MapReverseProxy();
        }

        app.MapRazorComponents<App>()
            .AddBlazouterSupport()
            .AddInteractiveServerRenderMode()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(Explore.Blazor.Client._Imports).Assembly);

        return app;
    }

    private static void ValidateProfile(WebApplication app, BlazorHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(app);
        var registeredProfile = app.Services
            .GetRequiredService<BlazorHostProfileRegistration>()
            .Profile;
        if (registeredProfile != profile)
        {
            throw new InvalidOperationException(
                $"Blazor host profile '{profile}' does not match registered profile '{registeredProfile}'.");
        }
    }
}
