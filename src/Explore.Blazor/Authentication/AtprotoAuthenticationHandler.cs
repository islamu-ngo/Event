// ABOUTME: Runs the CarpaNet AT Protocol OAuth challenge and callback inside the Blazor BFF.
// ABOUTME: Enforces bounded handles, protected flow bindings, HTTPS redirects, and verified bridge results.

using System.Text.Encodings.Web;
using CarpaNet.OAuth.Storage;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Authentication;

public sealed record AtprotoCallbackCompletion(
    AtprotoOAuthFlowSeed Seed,
    AtprotoBffSessionResult Session);

public sealed class AtprotoAuthenticationHandler(
    IOptionsMonitor<AtprotoAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AtprotoOAuthClientFactory clientFactory,
    IOAuthStateStore stateStore,
    IOAuthSessionStore sessionStore,
    AtprotoOAuthFlowContext flowContext,
    AtprotoTenantOriginResolver originResolver,
    AtprotoBrowserProof browserProof)
    : AuthenticationHandler<AtprotoAuthenticationOptions>(options, logger, encoder)
{
    public const string HandleProperty = "atproto_handle";
    public const string ReturnPathProperty = "atproto_return_path";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        properties.Items.TryGetValue(HandleProperty, out var handle);
        properties.Items.TryGetValue(ReturnPathProperty, out var storedReturnPath);
        var authorizationUrl = await CreateAuthorizationUrlAsync(
            handle,
            properties.RedirectUri ?? storedReturnPath ?? "/",
            AtprotoSubjectClassifications.Person,
            Context.RequestAborted).ConfigureAwait(false);
        Response.Redirect(authorizationUrl);
    }

    public async Task<string> CreateAuthorizationUrlAsync(
        string? rawHandle,
        string returnPath,
        string? rawClassification,
        CancellationToken cancellationToken)
        => await CreateAuthorizationUrlAsync(
            rawHandle,
            returnPath,
            rawClassification,
            null,
            null,
            cancellationToken).ConfigureAwait(false);

    public async Task<string> CreateAuthorizationUrlAsync(
        string? rawHandle,
        string returnPath,
        string? rawClassification,
        Guid? canonicalActorId,
        Guid? expectedCanonicalActorConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        var handle = NormalizeHandle(rawHandle);
        var classification = AtprotoSubjectClassifications.Normalize(rawClassification);
        if (!IsValidCanonicalActorTarget(canonicalActorId, expectedCanonicalActorConcurrencyStamp))
        {
            throw new InvalidOperationException("ATProto canonical Actor target is invalid.");
        }
        if (!IsSafeReturnPath(returnPath))
        {
            throw new InvalidOperationException("ATProto return path is invalid.");
        }

        var tenantOrigin = originResolver.Resolve(Request);
        var browserBinding = browserProof.CreateBinding(Context);
        using var lease = clientFactory.CreateForNewFlow(stateStore, sessionStore);
        var resolved = await lease.ResolveIdentityAsync(handle, cancellationToken).ConfigureAwait(false);
        var seed = new AtprotoOAuthFlowSeed(
            resolved.Did,
            resolved.PdsUri,
            tenantOrigin.TenantId,
            tenantOrigin.TenantSlug,
            tenantOrigin.Origin,
            returnPath,
            lease.PinnedKeyId,
            classification,
            canonicalActorId,
            expectedCanonicalActorConcurrencyStamp)
        {
            BrowserBinding = browserBinding
        };
        var authorizationUrl = await lease.Session.AuthorizeAsync(
            resolved.Did,
            ApiBackedOAuthStateStore.EncodeAppState(seed),
            cancellationToken).ConfigureAwait(false);
        return ValidateAuthorizationUrl(authorizationUrl);
    }

    public async Task<AtprotoCallbackCompletion> CompleteCallbackAsync(CancellationToken cancellationToken)
    {
        if (stateStore is not ApiBackedOAuthStateStore protectedStateStore)
        {
            throw new InvalidOperationException("ATProto callback requires the protected state store.");
        }

        var state = Request.Query["state"];
        var code = Request.Query["code"];
        var error = Request.Query["error"];
        var errorDescription = Request.Query["error_description"];
        var issuer = Request.Query["iss"];
        var hasCodeResult = code.Count == 1 && error.Count == 0;
        var hasErrorResult = error.Count == 1 && code.Count == 0;
        if (state.Count != 1 || !IsBoundedCallbackValue(state[0], 16, 512)
            || issuer.Count != 1 || !IsBoundedCallbackValue(issuer[0], 8, 2048)
            || hasCodeResult == hasErrorResult
            || (hasCodeResult && code[0] is not { Length: >= 1 and <= 4096 })
            || (hasErrorResult && !IsSafeOAuthError(error[0]))
            || code.Count > 1
            || error.Count > 1
            || (errorDescription.Count == 1 && !IsBoundedCallbackValue(errorDescription[0], 1, 1024))
            || errorDescription.Count > 1)
        {
            throw new InvalidOperationException("ATProto callback parameters are invalid.");
        }

        var pinnedKeyId = await protectedStateStore
            .GetPinnedKeyIdAsync(state[0]!, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("ATProto OAuth state is unavailable.");
        using var lease = clientFactory.CreateForPinnedKey(pinnedKeyId, stateStore, sessionStore);
        using var client = await lease.Session
            .CallbackAsync(Request.GetEncodedUrl(), cancellationToken)
            .ConfigureAwait(false);
        var binding = flowContext.Binding
            ?? throw new InvalidOperationException("ATProto OAuth state binding is unavailable.");
        var session = flowContext.SessionResult
            ?? throw new InvalidOperationException("ATProto session bridge did not complete.");
        if (!client.IsAuthenticated
            || !string.Equals(client.AuthenticatedDid, binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || !UrisEqual(client.BaseUrl, binding.Seed.ExpectedPdsUri)
            || !string.Equals(session.Did, binding.Seed.ExpectedDid, StringComparison.Ordinal)
            || !string.Equals(session.Classification, binding.Seed.Classification, StringComparison.Ordinal)
            || session.CanonicalActorId != binding.Seed.CanonicalActorId
            || session.ExpectedCanonicalActorConcurrencyStamp != binding.Seed.ExpectedCanonicalActorConcurrencyStamp)
        {
            throw new InvalidOperationException("ATProto callback identity binding failed.");
        }

        return new(binding.Seed, session);
    }

    private static bool IsBoundedCallbackValue(string? value, int minimumLength, int maximumLength) =>
        value is not null
        && value.Length >= minimumLength
        && value.Length <= maximumLength
        && !value.Any(character => char.IsControl(character));

    private static bool IsSafeOAuthError(string? value) =>
        IsBoundedCallbackValue(value, 1, 128)
        && value!.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    public static string NormalizeHandle(string? rawHandle)
    {
        var handle = rawHandle?.Trim();
        if (handle?.StartsWith('@') == true)
        {
            handle = handle[1..];
        }

        if (string.IsNullOrWhiteSpace(handle)
            || handle.Length > 253
            || handle.Any(character => character > 0x7f)
            || handle.Contains("..", StringComparison.Ordinal)
            || handle.StartsWith('.')
            || handle.EndsWith('.'))
        {
            throw new InvalidOperationException("ATProto handle is invalid.");
        }

        var labels = handle.Split('.');
        if (labels.Length < 2 || labels.Any(label =>
                label.Length is < 1 or > 63
                || label[0] == '-'
                || label[^1] == '-'
                || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            throw new InvalidOperationException("ATProto handle is invalid.");
        }

        return handle.ToLowerInvariant();
    }

    private static string ValidateAuthorizationUrl(string value)
    {
        if (value.Length > 8192
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)
                .ContainsKey("login_hint"))
        {
            throw new InvalidOperationException("ATProto authorization redirect is invalid.");
        }

        return uri.AbsoluteUri;
    }

    private static bool IsSafeReturnPath(string value) =>
        value.Length is > 0 and <= 2048
        && value[0] == '/'
        && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.StartsWith("/\\", StringComparison.Ordinal)
        && !value.Contains('\r')
        && !value.Contains('\n');

    private static bool IsValidCanonicalActorTarget(Guid? canonicalActorId, Guid? expectedConcurrencyStamp) =>
        canonicalActorId.HasValue == expectedConcurrencyStamp.HasValue
        && canonicalActorId != Guid.Empty
        && expectedConcurrencyStamp != Guid.Empty;

    private static bool UrisEqual(Uri left, Uri right) =>
        string.Equals(left.AbsoluteUri.TrimEnd('/'), right.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal);
}
