namespace Explore.Application.Exceptions;

/// <summary>
/// The original single-attempt owner cannot establish physical settlement.
/// This is neither a business rejection nor authority to replay or write failure history.
/// </summary>
public sealed class HumanMutationOutcomeUnknownException(Exception innerException)
    : Exception("The original human mutation outcome could not be established.", innerException);
