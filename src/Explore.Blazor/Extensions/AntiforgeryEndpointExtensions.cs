using Explore.Blazor.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.Extensions;

public static class AntiforgeryEndpointExtensions
{
    private const string AntiforgeryValidatedItemKey = "Explore.Blazor.AntiforgeryValidated";

    public static TBuilder ValidateAntiforgery<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
            await GetAntiforgeryFailureAsync(context.HttpContext) ?? await next(context));
        return builder;
    }

    public static TBuilder ValidateAntiforgeryBeforeRateLimiting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(BffAntiforgeryMetadata.Instance);
        builder.ValidateAntiforgery();
        return builder;
    }

    public static IApplicationBuilder UseBffEndpointAntiforgery(this IApplicationBuilder app)
    {
        return app.Use(InvokeBffEndpointAntiforgeryAsync);
    }

    private static async Task InvokeBffEndpointAntiforgeryAsync(HttpContext context, Func<Task> next)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<BffAntiforgeryMetadata>() is null)
        {
            await next();
            return;
        }

        IResult? failure = await GetAntiforgeryFailureAsync(context);
        if (failure is not null)
        {
            await failure.ExecuteAsync(context);
            return;
        }

        await next();
    }

    private static async Task<IResult?> GetAntiforgeryFailureAsync(HttpContext context)
    {
        if (context.Items.ContainsKey(AntiforgeryValidatedItemKey))
        {
            return null;
        }

        var selfCallTokenService = context.RequestServices.GetService<IBffSelfCallTokenService>();
        if (selfCallTokenService?.Validate(context) == true)
        {
            context.Items[AntiforgeryValidatedItemKey] = true;
            return null;
        }

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            context.Items[AntiforgeryValidatedItemKey] = true;
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                detail: "The antiforgery token was missing or invalid.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Antiforgery validation failed");
        }
    }

    private sealed class BffAntiforgeryMetadata
    {
        public static readonly BffAntiforgeryMetadata Instance = new();
    }
}
