namespace Explore.Application.Exceptions;

public sealed class TenantEnrollmentClosedException()
    : Exception("Public enrollment is closed for the selected tenant.")
{
}
