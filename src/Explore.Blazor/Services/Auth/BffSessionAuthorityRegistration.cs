using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Services.Auth;

public static class BffSessionAuthorityRegistration
{
    public static IServiceCollection AddBffSessionAuthority(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var options = new BffSessionAuthorityOptions();
        configuration.GetSection(BffSessionAuthorityOptions.SectionName).Bind(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RedisBffTicketStore>();
        services.AddSingleton<ITicketStore>(provider => provider.GetRequiredService<RedisBffTicketStore>());
        services.AddSingleton<IBffSessionAuthority>(provider => provider.GetRequiredService<RedisBffTicketStore>());
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .PostConfigure<ITicketStore>((cookie, store) => cookie.SessionStore = store);
        return services;
    }
}
