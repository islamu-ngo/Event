using Explore.API.ExceptionHandling;
using Microsoft.AspNetCore.Diagnostics;

namespace Explore.API.Extensions;

public static class ExceptionHandlingExtensions
{
    public static IServiceCollection AddApiExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;
                context.ProblemDetails.Extensions["correlationId"] =
                    context.HttpContext.Items["CorrelationId"] as string;

                if (context.ProblemDetails.Status == StatusCodes.Status415UnsupportedMediaType)
                {
                    context.ProblemDetails.Detail ??= "The request content type is not supported for this endpoint.";
                }
            };
        });

        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<ConfigurationImportExceptionHandler>();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    public static IApplicationBuilder UseApiExceptionHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var httpContext = statusCodeContext.HttpContext;
            if (httpContext.Response.StatusCode != StatusCodes.Status415UnsupportedMediaType
                || httpContext.Response.HasStarted
                || httpContext.Response.ContentLength.HasValue)
            {
                return;
            }

            var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = ApiValidationProblemDetailsFactory.CreateUnsupportedMediaType(httpContext)
            });
        });
        return app;
    }
}
