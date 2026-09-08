namespace Explore.Application.Contracts.Services;

public interface IEventReportEvidenceProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedText);
}
