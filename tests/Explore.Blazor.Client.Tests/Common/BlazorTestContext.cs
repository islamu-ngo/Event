using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Federation;
using Explore.Blazor.Client.Contracts.Services.Notifications;
using Explore.Blazor.Client.Contracts.Services.Reporting;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using MudBlazor;
using MudBlazor.Interop;
using MudBlazor.Services;

using Options = Microsoft.Extensions.Options.Options;

namespace Explore.Blazor.Client.Tests.Common;

/// <summary>
/// Custom BUnit test context with pre-configured services for ISLAMU Event Blazor testing.
/// Provides MudBlazor support, authentication mocking, and common service mock helpers.
/// </summary>
/// <remarks>
/// <para>
/// This context follows enterprise testing best practices:
/// - User IDs are Guid (matching domain model) - converted to string for JWT claims
/// - Tenant IDs are Guid (matching domain model and service interface)
/// - MudBlazor services are pre-registered for proper component rendering
/// - JSInterop runs in strict mode (bUnit default) — all JS calls must be explicitly set up
/// - MudBlazor JS-dependent services are mocked at the DI level (not via JSInterop handlers)
/// </para>
/// <para>
/// Usage:
/// <code>
/// using var ctx = new BlazorTestContext();
/// ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User");
/// ctx.AddMockService&lt;IEventService&gt;(svc => svc.GetAllEventsAsync().Returns(events));
/// var cut = ctx.Render&lt;EventList&gt;();
/// </code>
/// </para>
/// </remarks>
public class BlazorTestContext : BunitContext
{
    private readonly BunitAuthorizationContext _authContext;

    /// <summary>
    /// Creates a new BlazorTestContext with MudBlazor and authentication support pre-configured.
    /// </summary>
    public BlazorTestContext()
    {
        // Mock all JS-dependent MudBlazor services BEFORE AddMudServices().
        // MudBlazor registers services internally with TryAdd* — our pre-registered
        // mocks won't be overridden, eliminating all MudBlazor JS interop calls.
        // This follows MudBlazor's own testing pattern (see their BunitTest base class).
        MockMudBlazorJsServices();

        // Add MudBlazor non-JS services (dialog, snackbar, localization, etc.)
        // JS-dependent services see our mocks already registered and skip via TryAdd*.
        Services.AddMudServices(config =>
        {
            config.PopoverOptions.ThrowOnDuplicateProvider = false;
        });

        // Add bUnit's fake authorization (provides CascadingAuthenticationState)
        _authContext = this.AddAuthorization();

        // JSInterop runs in strict mode (bUnit default).
        // MudBlazor JS calls are mostly eliminated by service-level mocks above.
        // Some MudBlazor components (MudInput) still call IJSRuntime directly for blur events.
        // These residual calls need explicit JSInterop handlers:
        SetupResidualMudBlazorJsInterop();

        // ── Infrastructure services (needed by virtually all component tests) ──
        Services.AddLogging();
        AddLocalizationMocks();
        Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        Services.AddSingleton(Substitute.For<IBffAuthApi>());
        Services.AddSingleton(Substitute.For<IUiShellClient>());
        Services.AddSingleton(Substitute.For<
            Explore.Blazor.Client.Contracts.Services.ITicketPurchaseGovernanceService>());
        Services.AddSingleton(Substitute.For<IAtprotoFederationSettingsService>());
        Services.AddSingleton(Substitute.For<ITenantReportingIntakePolicyService>());
        Services.AddSingleton(Substitute.For<IBrowserActionInterop>());
        // EventLocation disclosure is now a page-level dependency of EventDetail. A default fail-closed
        // substitute (no disclosures) keeps unrelated page tests from having to know about venue privacy.
        Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Services.Events.IEventLocationService>());
        AddAccessibilityMocks();
        AddAppearanceThemeMock();
        AddPublicExperienceMock();
        Services.AddScoped(_ => Substitute.For<INotificationRefreshStreamClient>());
        var dockLayoutPersistence = Substitute.For<IDockLayoutPersistence>();
        dockLayoutPersistence.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DockLayoutSnapshot?>(null));
        dockLayoutPersistence.SaveAsync(Arg.Any<DockLayoutSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        dockLayoutPersistence.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        Services.AddSingleton(dockLayoutPersistence);
        Services.AddScoped(_ => Substitute.For<ILanguagePreferenceService>());
        Services.AddScoped<CurrentUserState>();
        Services.AddSingleton(Substitute.For<ITagService>());
        Services.AddSingleton(Substitute.For<ICategoryService>());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Services is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        base.Dispose(disposing);
    }

    // ── Opt-in domain mock groups ──
    // Tests call these explicitly to declare their dependencies.
    // Use AddAllDefaultMocks() for backward-compatible convenience when many services are needed.

    /// <summary>
    /// Add localization service mocks. Called by constructor — most components use T["key"].
    /// </summary>
    public void AddLocalizationMocks()
    {
        var translationService = Substitute.For<ITranslationService>();
        translationService.CurrentLanguage.Returns("en");
        translationService.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(ci => ci.ArgAt<string?>(1) ?? ci.ArgAt<string>(0));
        translationService.GetAvailableLanguagesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string> { "en" });
        translationService.PreloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        Services.AddSingleton(translationService);
    }

    /// <summary>
    /// Add accessibility service mocks. Called by constructor — widely injected across components.
    /// </summary>
    public void AddAccessibilityMocks()
    {
        Services.AddScoped(_ => Substitute.For<IAccessibilityFocusService>());
        Services.AddScoped(_ => Substitute.For<IAccessibilityAnnouncerService>());
    }

    /// <summary>
    /// Add public experience defaults used by Home and shared shell components.
    /// Tests can register a later substitute to override the default shell response.
    /// </summary>
    public void AddPublicExperienceMock()
    {
        var publicExperienceService = Substitute.For<Explore.Blazor.Client.Services.IPublicExperienceService>();
        publicExperienceService.GetCachedShellAsync().Returns(Task.FromResult<PublicExperienceShellDto?>(null));
        Services.AddSingleton(publicExperienceService);
    }

    /// <summary>
    /// Add appearance theme defaults used by shared shell components such as NavMenu and ThemeQuickSwitcher.
    /// </summary>
    public void AddAppearanceThemeMock()
    {
        var appearanceThemeService = Substitute.For<IAppearanceThemeService>();
        appearanceThemeService.Current.Returns(new AppearanceState());
        appearanceThemeService.CreateTheme(Arg.Any<string>()).Returns(new MudTheme());
        appearanceThemeService.InitializeAsync(Arg.Any<MudThemeProvider>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appearanceThemeService.ResolveEffectiveDarkModeAsync(Arg.Any<MudThemeProvider>())
            .Returns(Task.FromResult(false));
        appearanceThemeService.SetThemeModeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appearanceThemeService.SetDirectionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appearanceThemeService.SetActiveProfileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appearanceThemeService.ClonePresetAndActivateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Services.AddSingleton(appearanceThemeService);
    }

    /// <summary>
    /// Add shell state mocks (AiAssistantState, TenantNavLinksState, NotificationService).
    /// Required by MainLayout, NavMenu, and admin settings layouts.
    /// NOT registered by constructor — call explicitly when testing shell/layout components.
    /// </summary>
    public void AddShellStateMocks()
    {
        Services.AddScoped<AiAssistantState>();
        Services.AddScoped<TenantNavLinksState>();
        Services.AddScoped<DockLayoutState>();
        Services.AddScoped<IDockPanelRegistry>(provider => provider.GetRequiredService<DockLayoutState>());
        Services.AddSingleton(MockServiceFactory.CreateNotificationService());

        var publicExperienceService = Substitute.For<IPublicExperienceService>();
        publicExperienceService.GetSettingsAsync().Returns(Task.FromResult<PublicExperienceSettingsDto?>(null));
        Services.AddSingleton(publicExperienceService);

        var userSettingsService = Substitute.For<IUserSettingsService>();
        userSettingsService.GetSettingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SettingGroupResponseDto?>(null));
        Services.AddSingleton(userSettingsService);

        var shellContextService = Substitute.For<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UiShellContextDto?>(null));
        Services.AddSingleton(shellContextService);

        var shellPreferencesService = Substitute.For<IShellPreferencesService>();
        shellPreferencesService.LoadAsync(Arg.Any<UiShellContextDto>(), Arg.Any<CancellationToken>())
            .Returns(new ShellPreferenceState(WorkspaceKey.Events.Value, null, "/settings/personal"));
        Services.AddSingleton(shellPreferencesService);
    }

    /// <summary>
    /// Add group service mock with empty defaults.
    /// Required by GroupProfile, CreateEvent, NavMenu admin sections.
    /// NOT registered by constructor — call explicitly when testing group-dependent components.
    /// </summary>
    public void AddGroupServiceMock()
    {
        var groupService = Substitute.For<IGroupService>();
        groupService.GetMyGroupsAsync().Returns(new List<GroupListDto>());
        Services.AddSingleton(groupService);
    }

    /// <summary>
    /// Register all optional domain mocks for convenience.
    /// Use for tests that render complex components with many dependencies.
    /// Equivalent to calling AddShellStateMocks() + AddGroupServiceMock() individually.
    /// </summary>
    public void AddAllDefaultMocks()
    {
        AddShellStateMocks();
        AddGroupServiceMock();
    }

    /// <summary>
    /// Mock all JS-dependent MudBlazor services at the DI level.
    /// This prevents MudBlazor components from making any JS interop calls,
    /// following the same pattern used by MudBlazor's own test suite.
    /// Must be called BEFORE <see cref="ServiceCollectionExtensions.AddMudServices(IServiceCollection)"/>
    /// because MudBlazor uses TryAdd* (won't override existing registrations).
    /// </summary>
    private void MockMudBlazorJsServices()
    {
        // Register concrete mock implementations that return proper non-null defaults.
        // NSubstitute mocks would return null for factory methods and properties,
        // causing NullReferenceException during MudBlazor component rendering.

        // Popover — prevents mudPopover.initialize, connect, disconnect
        Services.AddScoped<IPopoverService, MockPopoverService>();

        // Resize observer factory — prevents mudResizeObserver.connect, disconnect
        Services.AddScoped<IResizeObserverFactory, MockResizeObserverFactory>();

        // Key interceptor — prevents mudKeyInterceptor.connect, disconnect
        Services.AddScoped<IKeyInterceptorService, MockKeyInterceptorService>();

        // JS event factory — prevents mudJsEvent.* calls
        Services.AddTransient<IJsEventFactory, MockJsEventFactory>();

        // JS API service — prevents mudElementRef.* calls (getBoundingClientRect, addOnBlurEvent, etc.)
        // Uses NSubstitute because MudBlazor v9 declares UpdateStyleProperty as internal —
        // concrete classes outside MudBlazor's assembly cannot implement IJsApiService.
        // NSubstitute (Castle.DynamicProxy) handles this at runtime. All methods return ValueTask (no NRE risk).
        Services.AddTransient(_ => Substitute.For<IJsApiService>());

        // Scroll manager — prevents mudScrollManager.lockScroll, unlockScroll
        Services.AddTransient<IScrollManager, MockScrollManager>();

        // Scroll listener factory — prevents scroll listener JS calls
        Services.AddTransient<IScrollListenerFactory, MockScrollListenerFactory>();

        // Scroll spy factory — prevents scroll spy JS calls
        Services.AddTransient<IScrollSpyFactory, MockScrollSpyFactory>();

        // Browser viewport service — prevents viewport detection JS calls.
        // Returns desktop-sized viewport (1920x1080, Breakpoint.Lg) to avoid mobile mode.
        Services.AddSingleton<IBrowserViewportService, MockBrowserViewportService>();
    }

    /// <summary>
    /// Sets up JSInterop handlers for MudBlazor JS calls that bypass the service layer.
    /// Some MudBlazor components (e.g., MudInput) call IJSRuntime directly
    /// instead of going through injectable services like IJsApiService.
    /// These calls cannot be intercepted by DI-level mocks and need bUnit JSInterop handlers.
    /// </summary>
    private void SetupResidualMudBlazorJsInterop()
    {
        // Some MudBlazor components call IJSRuntime directly, bypassing injectable services.
        // Must use catch-all argument matcher because calls include ElementReference
        // and DotNetObjectReference arguments that vary per component instance.

        // MudInput<T> directly calls IJSRuntime for blur event management
        JSInterop.SetupVoid("mudElementRef.addOnBlurEvent", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudElementRef.removeOnBlurEvent", _ => true).SetVoidResult();

        // MudComponentBase and related components call getBoundingClientRect directly.
        // This is a typed return call (InvokeAsync<BoundingClientRect>), not void.
        JSInterop.Setup<BoundingClientRect>("mudElementRef.getBoundingClientRect", _ => true)
            .SetResult(new BoundingClientRect());

        // MudHotkey registers keyboard shortcuts directly through IJSRuntime
        JSInterop.SetupVoid("mudHotkeyListener.registerOrUpdateHotkey", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudHotkeyListener.unregisterHotkey", _ => true).SetVoidResult();

        // MudThemeProvider calls watchDarkMode during OnAfterRenderAsync when rendered directly
        // (e.g., in MainLayout tests that include the full layout tree).
        JSInterop.SetupVoid("mudThemeProvider.watchDarkMode", _ => true).SetVoidResult();

        // MudOverlay/PointerEventsNoneService calls these during its lifecycle and dispose.
        // IPointerEventsNoneService is internal in MudBlazor v9 — cannot mock at DI level.
        JSInterop.SetupVoid("mudPointerEventsNone.addListener", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPointerEventsNone.cancelListener", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPointerEventsNone.dispose", _ => true).SetVoidResult();

        // MudFocusTrap saves and restores focus directly through IJSRuntime.
        // Keep this in the shared harness so overlay/dialog tests stay strict without
        // every test needing to know MudBlazor's internal focus helper identifiers.
        JSInterop.SetupVoid("mudElementRef.saveFocus", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudElementRef.restoreFocus", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudElementRef.focusFirst", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudElementRef.focusLast", _ => true).SetVoidResult();

        // Address autocomplete conditionally prevents native browser behavior only
        // for the navigation keys handled by its WAI-ARIA combobox state machine.
        var addressSuggestionModule =
            JSInterop.SetupModule("/js/address-suggestion-combobox.js");
        addressSuggestionModule
            .SetupVoid("bindComboboxNavigation", _ => true)
            .SetVoidResult();
        addressSuggestionModule
            .SetupVoid("unbindComboboxNavigation", _ => true)
            .SetVoidResult();
        addressSuggestionModule
            .SetupVoid("scrollActiveOptionIntoView", _ => true)
            .SetVoidResult();
        addressSuggestionModule
            .SetupVoid("ensureContainingDialogModal", _ => true)
            .SetVoidResult();

        // Browser storage APIs used by ProtectedBrowserStorage or component dependencies.
        // Returns empty string by default — individual tests can override with specific setups.
        JSInterop.Setup<string>("sessionStorage.getItem", _ => true).SetResult("");
        JSInterop.SetupVoid("sessionStorage.setItem", _ => true).SetVoidResult();
        JSInterop.SetupVoid("sessionStorage.removeItem", _ => true).SetVoidResult();
    }

    /// <summary>
    /// Renders a component wrapped with MudBlazor providers (MudPopoverProvider, etc.).
    /// Use this for components that use MudPopover, MudMenu, or other popover-based components.
    /// </summary>
    /// <typeparam name="TComponent">Component type to render</typeparam>
    /// <param name="parameterBuilder">Optional parameter builder</param>
    /// <returns>Rendered component</returns>
    public IRenderedComponent<TComponent> RenderMudComponent<TComponent>(
        Action<ComponentParameterCollectionBuilder<TComponent>>? parameterBuilder = null)
        where TComponent : IComponent
    {
        // Render the component within a MudPopoverProvider wrapper
        var wrapper = Render<MudPopoverProvider>();

        // Now render the actual component
        return parameterBuilder != null
            ? Render<TComponent>(parameterBuilder)
            : Render<TComponent>();
    }

    /// <summary>
    /// Configure authenticated user with claims for testing authorized components.
    /// </summary>
    /// <param name="userId">User ID as Guid (domain model type) - stored as string in JWT claims</param>
    /// <param name="name">Display name for the user</param>
    /// <param name="email">Optional email address</param>
    /// <param name="tenantId">Optional tenant ID for multi-tenancy testing</param>
    /// <remarks>
    /// <para>
    /// The userId parameter is Guid because that's the domain model type (User.Id is Guid).
    /// JWT claims store this as a string, so we convert it internally.
    /// This design bridges domain expectations with JWT reality.
    /// </para>
    /// <para>
    /// Claims are added in the same order the application expects:
    /// sub (OIDC standard), nameidentifier (legacy), name, email, tenant_id
    /// </para>
    /// </remarks>
    public void SetAuthenticatedUser(Guid userId, string? name = null, string? email = null, Guid? tenantId = null)
    {
        var userIdString = userId.ToString();

        var claims = new List<Claim>
        {
            // Primary user ID claims - stored as string (JWT standard)
            // Test-only identity claims for components that consume AuthenticationStateProvider directly.
            new("sub", userIdString),
            new(ClaimTypes.NameIdentifier, userIdString)
        };

        if (!string.IsNullOrEmpty(name))
            claims.Add(new Claim(ClaimTypes.Name, name));
        if (!string.IsNullOrEmpty(email))
            claims.Add(new Claim(ClaimTypes.Email, email));
        if (tenantId.HasValue)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));

        _authContext.SetAuthorized(name ?? "TestUser");
        _authContext.SetClaims(claims.ToArray());
    }

    /// <summary>
    /// Configure authenticated user with specific roles.
    /// </summary>
    /// <param name="userId">User ID as Guid</param>
    /// <param name="name">Display name for the user</param>
    /// <param name="roles">Roles to assign to the user</param>
    public void SetAuthenticatedUserWithRoles(Guid userId, string name, params string[] roles)
    {
        SetAuthenticatedUser(userId, name);
        _authContext.SetRoles(roles);
    }

    /// <summary>
    /// Configure authenticated user with specific policies.
    /// </summary>
    /// <param name="userId">User ID as Guid</param>
    /// <param name="name">Display name for the user</param>
    /// <param name="policies">Policies to authorize for the user</param>
    public void SetAuthenticatedUserWithPolicies(Guid userId, string name, params string[] policies)
    {
        SetAuthenticatedUser(userId, name);
        _authContext.SetPolicies(policies);
    }

    /// <summary>
    /// Configure authenticated user with additional custom claims (e.g., admin claims).
    /// </summary>
    /// <param name="userId">User ID as Guid</param>
    /// <param name="name">Display name for the user</param>
    /// <param name="additionalClaims">Additional claims to add beyond standard identity claims</param>
    public void SetAuthenticatedUserWithClaims(Guid userId, string name, params Claim[] additionalClaims)
    {
        var userIdString = userId.ToString();

        var claims = new List<Claim>
        {
            new("sub", userIdString),
            new(ClaimTypes.NameIdentifier, userIdString),
            new(ClaimTypes.Name, name)
        };

        claims.AddRange(additionalClaims);

        _authContext.SetAuthorized(name);
        _authContext.SetClaims(claims.ToArray());
    }

    /// <summary>
    /// Configure anonymous (unauthenticated) user for testing public components.
    /// </summary>
    public void SetAnonymousUser()
    {
        _authContext.SetNotAuthorized();
    }

    /// <summary>
    /// Configure authorizing state (loading authentication).
    /// Useful for testing loading states.
    /// </summary>
    public void SetAuthorizingState()
    {
        _authContext.SetAuthorizing();
    }

    /// <summary>
    /// Register a mock service using NSubstitute.
    /// </summary>
    /// <typeparam name="T">Service interface type</typeparam>
    /// <returns>The mock instance for further configuration</returns>
    public T AddMockService<T>() where T : class
    {
        var mock = Substitute.For<T>();
        Services.AddSingleton(mock);
        return mock;
    }

    /// <summary>
    /// Register a mock service with inline configuration.
    /// </summary>
    /// <typeparam name="T">Service interface type</typeparam>
    /// <param name="configure">Configuration action for setting up mock behavior</param>
    /// <returns>The configured mock instance</returns>
    /// <example>
    /// ctx.AddMockService&lt;IEventService&gt;(svc =>
    ///     svc.GetAllEventsAsync().Returns(eventList));
    /// </example>
    public T AddMockService<T>(Action<T> configure) where T : class
    {
        var mock = Substitute.For<T>();
        configure(mock);
        Services.AddSingleton(mock);
        return mock;
    }

    /// <summary>
    /// Register a concrete service instance.
    /// </summary>
    public void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        Services.AddSingleton<TService, TImplementation>();
    }

    /// <summary>
    /// Register a scoped mock service (useful for per-request services).
    /// </summary>
    public T AddScopedMockService<T>() where T : class
    {
        var mock = Substitute.For<T>();
        Services.AddScoped(_ => mock);
        return mock;
    }

    /// <summary>
    /// Add TenantConfiguration options for multi-tenancy testing.
    /// </summary>
    /// <param name="tenantId">Default tenant ID (falls back to standard default tenant)</param>
    /// <param name="enabled">Whether multi-tenancy is enabled</param>
    /// <param name="slug">Tenant slug for URL routing (default: "default")</param>
    /// <param name="tenantName">Display name of the default tenant (default: "Default")</param>
    public void AddTenantConfiguration(
        Guid? tenantId = null,
        bool enabled = false,
        string slug = "default",
        string tenantName = "Default")
    {
        var config = new TenantConfiguration
        {
            DefaultTenantId = tenantId ?? Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"),
            Enabled = enabled,
            DefaultTenant = slug,
            DefaultTenantName = tenantName
        };
        Services.AddSingleton(Options.Create(config));
    }

    /// <summary>
    /// Register all core service mocks with default configuration.
    /// </summary>
    public void AddAllCoreMocks()
    {
        MockServiceFactory.RegisterAllCoreMocks(Services);
    }

    /// <summary>
    /// Register all lookup service mocks with empty data.
    /// </summary>
    public void AddLookupServiceMocks()
    {
        MockServiceFactory.RegisterLookupServiceMocks(Services);
    }

    /// <summary>
    /// Register all lookup service mocks with pre-generated test data.
    /// </summary>
    /// <param name="itemCount">Number of items to generate for each lookup</param>
    public void AddLookupServiceMocksWithData(int itemCount = 5)
    {
        MockServiceFactory.RegisterLookupServiceMocksWithData(Services, itemCount);
    }
}
