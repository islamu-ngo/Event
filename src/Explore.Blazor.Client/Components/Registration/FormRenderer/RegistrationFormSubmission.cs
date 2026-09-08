namespace Explore.Blazor.Client.Components.Registration.FormRenderer;

public sealed record RegistrationFormSubmission(IReadOnlyDictionary<Guid, object?> Answers);
