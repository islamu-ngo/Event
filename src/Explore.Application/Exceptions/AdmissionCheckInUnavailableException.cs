namespace Explore.Application.Exceptions;

public sealed class AdmissionCheckInUnavailableException() :
    Exception("Admission check-in is temporarily unavailable.");
