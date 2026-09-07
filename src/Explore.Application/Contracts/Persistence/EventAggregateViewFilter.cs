namespace Explore.Application.Contracts.Persistence;

public sealed record EventAggregateViewFilter(
    string? Title,
    DateTimeOffset? StartAtFrom,
    DateTimeOffset? StartAtTo,
    string? Status,
    string? Visibility);
