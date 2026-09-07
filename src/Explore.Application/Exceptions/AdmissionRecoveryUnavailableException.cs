namespace Explore.Application.Exceptions;

public sealed class AdmissionRecoveryUnavailableException() :
    Exception("Admission recovery is temporarily unavailable.");
