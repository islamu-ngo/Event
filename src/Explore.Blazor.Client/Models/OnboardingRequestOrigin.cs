namespace Explore.Blazor.Client.Models;

/// <summary>The effective BFF request URL, captured server-side after trusted proxy processing.</summary>
public sealed record OnboardingRequestOrigin(string? Url);
