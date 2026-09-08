namespace Explore.API.Extensions;

public static class CorsExtensions
{
    public static IServiceCollection AddApiCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var corsAllowedOrigins = configuration.GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["https://iloveibadah.app"];

        services.AddCors(options =>
        {
            options.AddPolicy("InternalAppPolicy",
                policy => policy.WithOrigins(corsAllowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());

            options.AddPolicy("ExternalAppPolicy",
                policy => policy.WithOrigins(corsAllowedOrigins)
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                    .AllowAnyHeader());

            options.AddPolicy("InternalWebsitePolicy",
                policy => policy.WithOrigins(corsAllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());

            options.AddPolicy("ExternalWebsitePolicy",
                policy => policy.WithOrigins(corsAllowedOrigins)
                    .WithMethods("GET", "OPTIONS")
                    .WithHeaders("Accept", "Content-Type", "Authorization", "X-Tenant-Slug"));

            options.AddPolicy("DevPolicy",
                policy => policy.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });

        return services;
    }
}
