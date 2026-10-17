# AI provider response limits

Model discovery requests use closed string selectors: `scope` is `Tenant` or
`Instance`, and `credentialScope` is `None`, `Instance` or `Tenant`. Select the
intended existing credential authority; do not send an API key in the discovery
request. The published OpenAPI contract and generated client expose these same
choices.

Event limits provider responses before parsing them. A provider's request-token
limit is not a response-size guarantee: model catalogues, tool arguments,
unexpected fields and error bodies need their own boundaries.

## Supported profiles

These limits cover OpenAI Responses, OpenAI-compatible chat completions,
Anthropic, Anthropic-compatible chat, and Azure OpenAI completion transports.
OpenAI and compatible remote model discovery use the discovery budget. Anthropic
does not offer remote discovery through these adapters; Azure lists its configured
deployment rather than probing a remote model catalogue.

Azure is the only admitted Microsoft.Extensions.AI SDK profile. Other SDK
profiles must not be enabled by merely changing a provider identifier or
replacing a client: they need a transport that enforces the same limits before
SDK parsing. Streaming is not supported by these adapters.

## Nonsecret configuration

Use the `AiResponseLimits` configuration section. Environment configuration uses
the corresponding `AiResponseLimits__` prefix, for example
`AiResponseLimits__MaximumChatBytes=2097152`. These settings are advanced
nonsecret policy; keep provider credentials in the selected secret authority.
Tenant settings cannot raise these instance-wide ceilings.

| Configuration key | Default / maximum | Unit |
| --- | ---: | --- |
| `MaximumDiscoveryBytes` | 1048576 | response bytes (1 MiB) |
| `MaximumChatBytes` | 4194304 | response bytes (4 MiB) |
| `MaximumDepth` | 32 | JSON nesting levels |
| `MaximumCollectionItems` | 256 | entries per collection or object; total discovered models |
| `MaximumModelIdentifierCharacters` | 256 | decoded identifier characters |
| `MaximumChoices` | 16 | total completion choices |
| `MaximumToolCalls` | 128 | total tool calls |
| `MaximumToolArgumentBytes` | 65536 | each argument value (64 KiB) |
| `MaximumScalarBytes` | 262144 | each JSON scalar (256 KiB) |

Each value must be positive and no greater than its listed maximum. Invalid
settings block admission rather than removing protection. Lower values can
reject previously accepted responses. JSON escaping consumes the scalar and
argument byte budgets too. Unknown response fields are not exempt.

Automatic response decompression is disabled. Providers and intermediate
proxies must return identity-encoded responses; compressed replies are rejected
because this release does not separately bound compressed and decoded streams.

## Rejection and recovery

A size, nesting, collection, identifier, scalar, argument or encoding rejection
is a nontransient policy failure. It does not trigger automatic retries and
does not expose the provider's response body or exception message. A rejected
model catalogue is not replaced by a successful configured-model fallback.

Inspect the fixed failure category, such as `response_too_large`,
`response_encoding_denied`, `response_identifier_limit` or
`response_tool_argument_limit`. Reduce provider output, catalogue size or tool
argument size, or correct an unexpectedly compressed response. A locally lowered
limit can be raised back to the documented ceiling, but there is no unlimited
setting. Do not collect raw provider replies in support reports.

Ordinary HTTP 408, 429 and server failures remain retryable according to the
existing provider policy. User cancellation remains cancellation, while the
configured request deadline reports a timeout.

## Assurance status

This implementation is awaiting the security programme's final build and test
gate. Source completion is not a claim of live-provider certification. Changes
to an SDK or HTTP pipeline require new bounded-consumption evidence before
admitting that profile.
