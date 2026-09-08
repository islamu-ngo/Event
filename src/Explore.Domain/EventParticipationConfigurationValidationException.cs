namespace Explore.Domain;

public sealed class EventParticipationConfigurationValidationException : Exception
{
    public EventParticipationConfigurationValidationException(
        IEnumerable<EventParticipationConfigurationValidationError> errors)
        : base("Event participation configuration is invalid.")
    {
        ArgumentNullException.ThrowIfNull(errors);

        var materializedErrors = errors.ToArray();
        if (materializedErrors.Length == 0)
        {
            throw new ArgumentException("At least one validation error is required.", nameof(errors));
        }

        Errors = materializedErrors;
    }

    public IReadOnlyList<EventParticipationConfigurationValidationError> Errors { get; }
}
