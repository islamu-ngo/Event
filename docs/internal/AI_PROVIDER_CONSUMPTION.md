# AI provider response consumption

> **Status:** Source implemented; execution evidence deferred until programme assurance.
> **Scope:** Infrastructure AI completion and model discovery, not credential acquisition.
> **Source anchors:** `AiResponseReader`, `AiResponseLimits`, `AiResponseBudgetHandler`, `InfrastructureServicesRegistration`.

## Allocation boundary

Model discovery publishes `AiModelDiscoveryScope` and `AiCredentialScope` through
the shared OpenAPI string-enum catalog, including eager nested components.
Their closed wire values are `Tenant`/`Instance` and `None`/`Instance`/`Tenant`.
The generated client consumes those definitions rather than guessing numeric
selectors. Discovery requests select a credential scope; they do not carry a
replacement API key.

`OpenAiCompatibleChatProvider`, `OpenAiResponsesChatProvider` and both Anthropic
adapters issue requests with `HttpCompletionOption.ResponseHeadersRead`. They pass
the original `HttpContent` to `AiResponseReader` before deserializing any response
DTO, `JsonElement`, string or SDK response. Request payload serialization is a
separate, locally owned operation.

The parent-owned `BoundedResponseReader` rejects known oversized bodies and
nonidentity content encodings before acquiring the body stream. For unknown or
dishonest lengths it consumes at most the configured byte maximum plus one
overflow byte. It closes acquired streams on success, rejection and cancellation.
Its UTF-8 token scan bounds depth, arrays and every scalar, including unknown
fields. `AiResponseReader` then scans the bounded bytes without building a JSON
tree. It bounds unknown object members, discovery identifiers, aggregate model
entries, aggregate choices, aggregate tool calls, and each tool-argument value.
Property names and tool type values use the same escaped, case-insensitive
semantics as the consuming adapters.

Tool-argument objects, including Anthropic `input`, are measured from the opening
to the closing token before any `JsonElement` is created. String argument and
scalar ceilings count UTF-8 JSON representation bytes; JSON escapes also consume
budget. Identifier length counts decoded UTF-16 characters, using a fixed-size
span rather than allocating an oversized identifier string. Unknown fields
consume the same depth, scalar, collection and total-byte budgets as known fields.
Duplicate recognized collections also count toward aggregate model/choice/tool
limits rather than resetting those limits.

`AiResponseLimits` binds the nonsecret `AiResponseLimits` configuration section.
Each setting may be reduced from, but not raised above, the following supported
ceiling. Zero, negative or excessive values fail options admission; the reader
also rejects an invalid options object at its own boundary.

| Setting | Default and maximum |
| --- | ---: |
| `MaximumDiscoveryBytes` | 1048576 |
| `MaximumChatBytes` | 4194304 |
| `MaximumDepth` | 32 |
| `MaximumCollectionItems` | 256 |
| `MaximumModelIdentifierCharacters` | 256 |
| `MaximumChoices` | 16 |
| `MaximumToolCalls` | 128 |
| `MaximumToolArgumentBytes` | 65536 |
| `MaximumScalarBytes` | 262144 |

Tenant credentials, model selection and request token limits cannot replace or
raise these instance-owned response limits. All original response objects are
disposed by the adapter, including rejection before stream acquisition.

## Anthropic and Refit

Anthropic's old `ApiResponse<AnthropicMessageResponse>` contract deserialized
inside Refit before the adapter could apply a budget. The adapter now sends the
native HTTP request and applies the reader itself; the unused Refit interface
has been removed. Both official and compatible profiles retain `/messages`,
`x-api-key`, `anthropic-version: 2023-06-01`, JSON null omission, and provider
request-id mapping. They do not advertise remote model discovery.

## Azure SDK transport and admission

The only enabled Microsoft.Extensions.AI SDK profile is Azure OpenAI.
`RegisterAiHttpClients` installs `AiResponseBudgetHandler` around the vetted
primary transport for both the instance client and the public tenant client.
The handler runs inside the HTTP pipeline: `HttpClient` completion buffering
and `HttpClientPipelineTransport` SDK parsing happen only after it returns.
It consumes and validates the original stream, then substitutes a bounded
`ByteArrayContent` whose length is derived from accepted bytes.

The instance SDK factory and tenant SDK constructor both use this named
transport with SDK retries disabled. This is an Azure-specific construction
contract, not a claim that an arbitrary `IChatClient` has a safe transport.
Other provider IDs are rejected by the SDK adapter with
`provider_transport_unsupported`; the factory also refuses non-Azure profiles.
Synchronous HTTP dispatch is rejected by the budget handler rather than silently
using an unbounded inherited `Send` implementation. Streaming remains unsupported.
Any new SDK/provider or replacement pipeline needs an owned pre-materialization
boundary and native transport evidence before profile admission.

`AiTransportAuthority` explicitly sets `AutomaticDecompression = None`. Compressed
responses are rejected; no compressed/decoded dual-budget implementation is
claimed. The Azure catalogue is a configured deployment descriptor, not a
successful remote-discovery result. Azure credential acquisition remains in the
separately owned credential-custody workstream.

## Safe failures

Security rejections are nontransient categories, never body text or exception
messages: `response_too_large`, `response_encoding_denied`,
`response_depth_limit`, `response_item_limit`, `response_scalar_limit`,
`response_identifier_limit`, `response_choice_limit`,
`response_tool_call_limit`, `response_tool_argument_limit`,
`response_invalid_json`, and `response_policy_invalid`.

Completion adapters map typed rejections, including SDK inner exceptions, to a
fixed public message. The discovery gateway returns the fixed category with an
empty result rather than reporting successful discovery. `IAiModelCatalog`
discovery propagates the safe typed rejection and never substitutes a configured
catalogue for a rejected remote response.

Ordinary unsuccessful HTTP bodies are discarded without reading. Direct
adapters retain status-based failures; Azure receives an empty error body while
the status and response headers remain intact. HTTP 408, 429 and 5xx remain
transient. Caller cancellation propagates; the adapter-owned deadline remains a
distinct transient `provider_timeout`.

## Deferred evidence

`AiBoundedConsumptionTests` contains stream-observable direct-adapter and real
Azure SDK pipeline cases for byte ceilings, disposal, caller cancellation,
unknown scalars and objects, escaped identifiers/property names, collection and
tool budgets, compression rejection, custom headers, catalogue rejection and
SDK error-status handling. Tests use terminal in-memory HTTP handlers, not live
providers. Existing provider mapping and endpoint-authority suites remain part
of the final gate. The touched OpenAI timeout test now fires an injected timer
after observing request entry; it no longer waits 1100 ms and hopes a deadline
has elapsed.

No tests, builds, generators or validators have been executed for this source
slice. Native Azure SDK transport behavior and final compilation remain pending
proof; source-only language-server diagnostics are not substitutes. At the
whole-programme gate, run the Infrastructure Release build and the combined
Infrastructure tests once, including the parent-owned
`BoundedResponseReaderTests` and all affected provider tests.

The [operator guide](../public/documentation/readme/configuration-and-operations/ai-provider-response-limits.md)
describes configuration and recovery without duplicating implementation details.
