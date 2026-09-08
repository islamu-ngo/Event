using System.Globalization;
using Blazouter.Extensions;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Configuration;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Providers;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Organizations;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Localization;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Http;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices(config =>
{
    config.PopoverOptions.Duration = TimeSpan.FromMilliseconds(300);
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
});
builder.Services.AddBlazouter();
builder.Services.AddEventControlPlaneClient();
builder.Services.AddScoped<AuthenticatedRouteGuard>();
builder.Services.AddScoped<MultiTenantOnboardingRouteGuard>();
builder.Services.AddScoped<AdminRouteGuard>();
builder.Services.AddScoped<TenantAdminRouteGuard>();
builder.Services.AddScoped<OrgAdminRouteGuard>();
builder.Services.AddScoped<GroupAdminRouteGuard>();

// Register the message handler that adds credentials to requests
builder.Services.AddTransient<BrowserCredentialsMessageHandler>();

// Register the message handler that adds anti-forgery tokens to mutating requests
builder.Services.AddTransient<BffAntiforgeryMessageHandler>();

// Register shared NSwag operation behavior for monolithic and per-tag clients
builder.Services.AddTransient<EventApiBehaviorMessageHandler>();

// Register handler for 401 responses that triggers a server-side login
builder.Services.AddTransient<BffUnauthorizedHandler>();

// Configure default HttpClient for WASM with credentials
// This HttpClient sends cookies with all requests
builder.Services.AddHttpClient("BffClient", client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
})
.AddHttpMessageHandler<BrowserCredentialsMessageHandler>()
.AddHttpMessageHandler<BffAntiforgeryMessageHandler>()
.AddHttpMessageHandler<BffUnauthorizedHandler>();

// Register a default HttpClient for general use (also with credentials)
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("BffClient");
});

// Register the NSwag-generated per-tag API clients for WASM.
// Requests use the same-origin BFF and shared transport behavior chain.
foreach (var (interfaceType, implementationType) in GeneratedEventApiClients.ClientTypes)
{
    builder.Services.AddTypedApiClient(
        interfaceType,
        implementationType,
        client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
        .AddHttpMessageHandler<EventApiBehaviorMessageHandler>()
        .AddHttpMessageHandler<BrowserCredentialsMessageHandler>()
        .AddHttpMessageHandler<BffAntiforgeryMessageHandler>()
        .AddHttpMessageHandler<BffUnauthorizedHandler>();
}

// Register TenantConfiguration for single-tenant mode (default)
// In WASM, configuration section may not be available, so defaults from TenantConfiguration class are used
builder.Services.Configure<TenantConfiguration>(
    builder.Configuration.GetSection(TenantConfiguration.SectionName));

// Register lazy assembly loader for WASM lazy loading
builder.Services.AddScoped<ILazyAssemblyLoader, LazyAssemblyLoaderService>();

// ──────────────────────────────────────────────
// Shared application services (Server + WASM)
// ──────────────────────────────────────────────
builder.Services.AddSharedApplicationServices((_, client) =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// WASM-specific services (different from server-side registrations)
builder.Services.AddScoped<IAnalyticsInterop, AnalyticsInterop>();
builder.Services.AddScoped<ICookieConsentInterop, CookieConsentInterop>();
builder.Services.AddScoped<CookieConsentStateService>();

builder.Services.AddScoped<Explore.Blazor.Client.Services.Http.BffClient>();
builder.Services.AddScoped<Explore.Blazor.Client.Services.Http.IBffClient>(sp =>
    sp.GetRequiredService<Explore.Blazor.Client.Services.Http.BffClient>());

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

// Add logging for debugging in WASM
builder.Logging.SetMinimumLevel(LogLevel.Information);

var host = builder.Build();

// Set CultureInfo from the .AspNetCore.Culture cookie so .NET date/number formatting
// respects user language in WASM. Runs before first render for correct initial formatting.
try
{
    var jsRuntime = host.Services.GetRequiredService<IJSRuntime>();
    var langCookie = await jsRuntime.InvokeAsync<string?>("localization.getLanguageCookie");
    if (!string.IsNullOrWhiteSpace(langCookie) && CultureRegistry.Contains(langCookie))
    {
        var culture = new CultureInfo(langCookie);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
catch
{
    // Startup must not fail due to culture detection — fall through to defaults.
}

await host.RunAsync();
