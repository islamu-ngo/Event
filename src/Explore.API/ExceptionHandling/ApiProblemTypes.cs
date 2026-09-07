namespace Explore.API.ExceptionHandling;

internal static class ApiProblemTypes
{
    public const string BadRequest = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    public const string Unauthorized = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    public const string Forbidden = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    public const string NotFound = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    public const string Conflict = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
    public const string Gone = "https://tools.ietf.org/html/rfc9110#section-15.5.11";
    public const string PayloadTooLarge = "https://tools.ietf.org/html/rfc9110#section-15.5.14";
    public const string UnsupportedMediaType = "https://tools.ietf.org/html/rfc9110#section-15.5.16";
    public const string UnprocessableEntity = "https://tools.ietf.org/html/rfc9110#section-15.5.21";
    public const string TooManyRequests = "https://tools.ietf.org/html/rfc9110#section-15.5.30";
    public const string InternalServerError = "https://tools.ietf.org/html/rfc9110#section-15.6.1";
    public const string BadGateway = "https://tools.ietf.org/html/rfc9110#section-15.6.3";
    public const string ServiceUnavailable = "https://tools.ietf.org/html/rfc9110#section-15.6.4";
}
