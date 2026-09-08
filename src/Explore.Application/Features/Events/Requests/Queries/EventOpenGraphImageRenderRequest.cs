namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record EventOpenGraphImageRenderRequest(
    string Title,
    DateOnly? FirstSessionDate,
    DateOnly? LastSessionDate,
    string BrandDisplayName,
    Stream? FeaturedImage,
    string? FeaturedImageContentType);
