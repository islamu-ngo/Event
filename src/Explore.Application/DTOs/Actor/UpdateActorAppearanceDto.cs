using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Actor;

public sealed record UpdateActorAppearanceDto
{
    public OptionalUpdate<string?> BackgroundColor { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> BackgroundEffect { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> BannerColor { get; init; } = OptionalUpdate<string?>.Unspecified();
}
