using System.Text;

namespace Explore.Atproto.Transport;

public abstract class AtprotoPrivateKeyJwtHandlerBase(
    AtprotoAuthorizationServerRegistry registry,
    string clientId,
    string callbackUri,
    string requiredScope,
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    public const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    public const int MaximumFormBytes = 64 * 1024;

    protected string ClientId { get; } = clientId;

    protected abstract ValueTask<string> CreateAssertionAsync(
        string issuer,
        CancellationToken cancellationToken);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var binding = await PrepareClassifiedRequestAsync(request, cancellationToken).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (binding is null || !response.IsSuccessStatusCode)
        {
            return response;
        }

        if (!HasValidMandatoryNonce(response))
        {
            response.Dispose();
            throw new AtprotoOAuthSecurityException("missing_dpop_nonce");
        }

        return response;
    }

    private async Task<AtprotoOAuthEndpointBinding?> PrepareClassifiedRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri is null)
        {
            return null;
        }

        var mapped = registry.TryResolve(request.RequestUri, out var binding);
        if (request.Content is null
            || !string.Equals(
                request.Content.Headers.ContentType?.MediaType,
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase))
        {
            if (mapped)
            {
                throw new AtprotoOAuthSecurityException("form_content_required");
            }

            return null;
        }

        var bytes = await AtprotoHttpContent.ReadBoundedAsync(
            request.Content,
            MaximumFormBytes,
            cancellationToken).ConfigureAwait(false);
        var form = ParseForm(bytes);
        if (!mapped)
        {
            if (form.Keys.Any(IsOAuthClassificationField))
            {
                throw new AtprotoOAuthSecurityException("unmapped_oauth_endpoint");
            }

            return null;
        }

        ValidateDpopHeader(request);
        ValidateForm(binding, form);
        if (form.ContainsKey("client_assertion") || form.ContainsKey("client_assertion_type"))
        {
            throw new AtprotoOAuthSecurityException("duplicate_client_assertion");
        }

        if (form.TryGetValue("client_id", out var suppliedClientId)
            && !string.Equals(suppliedClientId, ClientId, StringComparison.Ordinal))
        {
            throw new AtprotoOAuthSecurityException("client_id_mismatch");
        }

        form["client_id"] = ClientId;
        form["client_assertion_type"] = AssertionType;
        form["client_assertion"] = await CreateAssertionAsync(binding.Issuer, cancellationToken).ConfigureAwait(false);
        AtprotoHttpContent.ReplaceRequestForm(request, form);
        return binding;
    }

    private void ValidateForm(
        AtprotoOAuthEndpointBinding binding,
        IReadOnlyDictionary<string, string> form)
    {
        switch (binding.Kind)
        {
            case AtprotoOAuthEndpointKind.PushedAuthorization:
                RequireExactFields(
                    form,
                    ["client_id", "redirect_uri", "response_type", "state", "code_challenge", "code_challenge_method", "scope", "dpop_jkt"],
                    ["login_hint"]);
                RequireExact(form, "client_id", ClientId);
                RequireExact(form, "redirect_uri", callbackUri);
                RequireExact(form, "response_type", "code");
                RequireNonEmpty(form, "state");
                RequireNonEmpty(form, "code_challenge");
                RequireExact(form, "code_challenge_method", "S256");
                RequireExact(form, "scope", requiredScope);
                RequireNonEmpty(form, "dpop_jkt");
                if (form.ContainsKey("login_hint"))
                {
                    RequireNonEmpty(form, "login_hint");
                }

                break;
            case AtprotoOAuthEndpointKind.Token when form.GetValueOrDefault("grant_type") == "authorization_code":
                RequireExactFields(form, ["grant_type", "code", "code_verifier", "redirect_uri", "client_id"], []);
                RequireExact(form, "grant_type", "authorization_code");
                RequireNonEmpty(form, "code");
                RequireNonEmpty(form, "code_verifier");
                RequireExact(form, "redirect_uri", callbackUri);
                RequireExact(form, "client_id", ClientId);
                break;
            case AtprotoOAuthEndpointKind.Token when form.GetValueOrDefault("grant_type") == "refresh_token":
                RequireExactFields(form, ["grant_type", "refresh_token", "client_id"], []);
                RequireExact(form, "grant_type", "refresh_token");
                RequireNonEmpty(form, "refresh_token");
                RequireExact(form, "client_id", ClientId);
                break;
            case AtprotoOAuthEndpointKind.Revocation:
                RequireExactFields(form, ["token", "token_type_hint"], []);
                RequireNonEmpty(form, "token");
                RequireExact(form, "token_type_hint", "refresh_token");
                break;
            default:
                throw new AtprotoOAuthSecurityException("ambiguous_oauth_form");
        }
    }

    private static void RequireExactFields(
        IReadOnlyDictionary<string, string> form,
        IReadOnlyCollection<string> required,
        IReadOnlyCollection<string> optional)
    {
        if (required.Any(field => !form.ContainsKey(field))
            || form.Keys.Any(field => !required.Contains(field, StringComparer.Ordinal)
                                     && !optional.Contains(field, StringComparer.Ordinal)))
        {
            throw new AtprotoOAuthSecurityException("ambiguous_oauth_form");
        }
    }

    private static void RequireNonEmpty(IReadOnlyDictionary<string, string> form, string field)
    {
        if (!form.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new AtprotoOAuthSecurityException("ambiguous_oauth_form");
        }
    }

    private static void RequireExact(
        IReadOnlyDictionary<string, string> form,
        string field,
        string expected)
    {
        if (!form.TryGetValue(field, out var value) || !string.Equals(value, expected, StringComparison.Ordinal))
        {
            throw new AtprotoOAuthSecurityException("ambiguous_oauth_form");
        }
    }

    private static Dictionary<string, string> ParseForm(byte[] bytes)
    {
        string content;
        try
        {
            content = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new AtprotoOAuthSecurityException("invalid_form_encoding", exception);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in content.Split('&', StringSplitOptions.None))
        {
            var separator = pair.IndexOf('=');
            if (pair.Length == 0 || separator <= 0)
            {
                throw new AtprotoOAuthSecurityException("ambiguous_form");
            }

            var name = DecodeFormComponent(pair[..separator]);
            var value = DecodeFormComponent(pair[(separator + 1)..]);
            if (!result.TryAdd(name, value))
            {
                throw new AtprotoOAuthSecurityException("duplicate_form_field");
            }
        }

        return result;
    }

    private static string DecodeFormComponent(string component)
    {
        using var decoded = new MemoryStream(component.Length);
        for (var index = 0; index < component.Length; index++)
        {
            var character = component[index];
            if (character > 0x7f)
            {
                throw new AtprotoOAuthSecurityException("invalid_form_encoding");
            }

            if (character == '+')
            {
                decoded.WriteByte((byte)' ');
                continue;
            }

            if (character != '%')
            {
                decoded.WriteByte((byte)character);
                continue;
            }

            if (index + 2 >= component.Length
                || !TryUpperHex(component[index + 1], out var high)
                || !TryUpperHex(component[index + 2], out var low))
            {
                throw new AtprotoOAuthSecurityException("invalid_form_encoding");
            }

            decoded.WriteByte((byte)((high << 4) | low));
            index += 2;
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(decoded.ToArray());
        }
        catch (DecoderFallbackException exception)
        {
            throw new AtprotoOAuthSecurityException("invalid_form_encoding", exception);
        }
    }

    private static bool TryUpperHex(char character, out int value)
    {
        if (character is >= '0' and <= '9')
        {
            value = character - '0';
            return true;
        }

        if (character is >= 'A' and <= 'F')
        {
            value = character - 'A' + 10;
            return true;
        }

        value = 0;
        return false;
    }

    private static void ValidateDpopHeader(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("DPoP", out var values))
        {
            throw new AtprotoOAuthSecurityException("dpop_proof_required");
        }

        var proofs = values.ToArray();
        if (proofs.Length != 1
            || proofs[0].Length is < 5 or > 8192
            || proofs[0].Any(character => !IsCompactJwtCharacter(character))
            || proofs[0].Split('.').Length != 3
            || proofs[0].Split('.').Any(string.IsNullOrEmpty))
        {
            throw new AtprotoOAuthSecurityException("invalid_dpop_proof");
        }
    }

    private static bool IsCompactJwtCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';

    private static bool IsOAuthClassificationField(string name) =>
        name is "grant_type" or "response_type" or "token" or "client_id" or "redirect_uri"
            or "code" or "refresh_token" or "code_verifier" or "code_challenge"
            or "client_assertion" or "client_assertion_type";

    private static bool HasValidMandatoryNonce(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("DPoP-Nonce", out var values))
        {
            return false;
        }

        var entries = values.ToArray();
        return entries.Length == 1
            && entries[0].Length is >= 1 and <= 512
            && entries[0].All(character => character <= 0x7f && !char.IsControl(character) && !char.IsWhiteSpace(character));
    }
}
