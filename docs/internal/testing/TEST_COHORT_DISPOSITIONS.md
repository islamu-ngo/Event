# Add-On And Ticketing Recovery Test Dispositions

Scope: the two Domain mutation wrappers only. The wider testing-assurance programme remains unresolved; this record is not a portfolio audit or enterprise-quality certification.

## Assertion Ownership

Each row retains the complete method body and local helpers; no deduplication is inferred from neighbouring test names. All cases are single `[Test]` methods without data variants. Destination namespace: `Event.Domain.UnitTests.Entities`. Add-on methods move from `Explore.Domain.AddOns.MutationTests.EventAddOnMutationTests` to `EventAddOnLifecycleTests`; recovery methods move from `Explore.Domain.Recovery.MutationTests.TicketingRecoveryMutationTests` to `TicketingRecoveryInvariantTests`. Method names remain unchanged.

| Source and destination method | Protected assertion families | Disposition |
| --- | --- | --- |
| CatalogLifecycleValidatesLineageAndFreezesPublishedOffers | Currency/version/draft facts; empty publish denial; item/stamp change; duplicate rejection; publication timestamp/freeze; retirement and replay denial | Preserved; all named ordinary add-on cases passed |
| CatalogFactoryAndTransitionsRejectEveryInvalidBoundary | Empty tenant/event, zero version, null/foreign-tenant/foreign-catalog/wrong-currency item; unpublished retirement; local/future publication and backward/local retirement denial | Preserved; all named ordinary add-on cases passed |
| ItemFactoryNormalizesDisclosureAndRejectsInvalidCapacity | Trimmed name/description/disclosures; literal price/currency/capacity; zero capacity denial | Preserved; all named ordinary add-on cases passed |
| ItemFactoryRejectsMissingIdentityMoneyAndOversizedCopy | Empty item/tenant/catalog, null money, blank/oversized name, oversized description and blank fulfillment disclosure | Preserved; all named ordinary add-on cases passed |
| OrderLineSnapshotsLiteralCheckedTotalAndPinnedCatalog | Catalog pin/count; literal quantity/name/unit/line/order totals/currency; duplicate denial; checked overflow | Preserved; all named ordinary add-on cases passed |
| OrderLineFactoryRejectsMissingAndMismatchedCommercialFacts | Unpublished catalog, empty line, zero quantity, null order/catalog/item and unattached item denial | Preserved; all named ordinary add-on cases passed |
| InventoryAllocationReleasesMonotonicallyAndClosesActiveSlot | Quantity/released/allocated/active-slot facts; partial release; over-release/backdated denial; final zero and slot closure | Preserved; all named ordinary add-on cases passed |
| InventoryFactoryAndReleaseRejectMalformedAuthority | Empty allocation/operation, null line, local allocation/release time and zero release denial | Preserved; all named ordinary add-on cases passed |
| FulfillmentAndRefundStatesAreReplaySafeAndMonotonic | Fulfillment lineage/time; literal refund amount; pending status; confirm/fail idempotency, first timestamps and mutually exclusive terminal transitions | Preserved; all named ordinary add-on cases passed |
| FulfillmentAndRefundFactoriesRejectMalformedAuthority | Empty identities/null line/local time; invalid refund quantity; backdated confirm/fail/pending-release and premature completion denial | Preserved; all named ordinary add-on cases passed |
| ConfirmedRefundCanRecoverPendingInventoryReleaseExactlyOnce | Pending inventory-release confirmation once; pending status/replay; completion once; confirmed status/replay | Preserved; all named ordinary add-on cases passed |
| ResultFactoriesExposeOnlyValidOutcomeAndPayloadPairs | Inventory/fulfillment/refund success/replay payloads; failure outcomes/null payloads; success-as-failure rejection | Preserved; all named ordinary add-on cases passed |
| ManifestPreservesEveryConsistencyFact | Operation/tenant/release/schema/checkpoint/cutoff/key/floors/cursor/fence/generations/digest | Preserved; all named ordinary recovery cases passed |
| ManifestRejectsMalformedIdentityFloorTimeAndDigest | Empty/non-v7 identities; blank/oversized revisions; negative floors/generations; zero key; malformed digest/local cutoff; valid length edges | Preserved; all named ordinary recovery cases passed |
| ValidationReportsEveryClosedMismatchWithoutAdvancing | Release/schema/key/authority/provider/idempotency/fence mismatch outcomes; each stays RecoveryOnly without validation timestamp | Preserved; all named ordinary recovery cases passed |
| ValidatedRecoveryRequiresStrictGenerationAndFenceAdvance | Successful/repeated validation/time; stale capability, credential or fence denied; advancing all succeeds; status/generations/fence/timestamps/stamp | Preserved; all named ordinary recovery cases passed |
| WorkersAndSalesOpenInExactOrderAndFence | Wrong worker fence and premature sales denied; correct workers open first; worker replay denied; sales opens with expected timestamps | Preserved; all named ordinary recovery cases passed |
| StopAndPauseReturnToClosedStatesMonotonically | Legitimate opening; pause closes workers/sales; pause replay/stale stop fence denied; advancing stop returns RecoveryOnly and clears opening timestamps | Preserved; all named ordinary recovery cases passed |
| FailureAndReissueIntentRemainClosedAndDigestFree | Failed state/normalized bounded code/time; validation/stop remain closed; reissue tenant/operation/ticket/generation/pending/v7/time facts | Preserved; all named ordinary recovery cases passed |

## Catastrophic-State Witnesses And Sensitivity

The four named recovery transition methods reject stale or premature reopening and demonstrate the valid worker-before-sales sequence. The three named allocation/refund methods demonstrate monotonic release and replay-safe terminal state. These are deterministic public aggregate tests, not database-race, distributed fencing, provider-side exactly-once or authentication evidence. No product logic changes are authorized here.

Original reference runs at `fefcea5f16e3bd723e22076d505a65c696c4e40b`: AddOns 12 passed, Recovery 7 passed, zero failures/skips. The ordinary Domain baseline slice `AdmissionRecoveryCapabilityTests` passed all four cases. Original runner durations were 266ms / 213ms; build/restore time is separate. Reference TRX artifacts are retained under `/home/amir/.cache/agent-tmp/testing-assurance-modernization/reference-AddOns/` and `reference-Recovery/`.

Sensitivity was observed before retirement: expecting a repeated inventory-release completion to succeed failed `ConfirmedRefundCanRecoverPendingInventoryReleaseExactlyOnce`; expecting fence 110 after an accepted rotation to 111 failed `ValidatedRecoveryRequiresStrictGenerationAndFenceAdvance`. Both invocations exited 2, each with one expected assertion failure and zero skips (12 add-on cases / 7 recovery cases). The expectations were restored immediately. Artifacts remain separately under `sensitivity/` and `sensitivity-recovery/`. These are deliberately wrong test expectations, not product defects. An attempted combined filter selected only the add-on class in this pinned runner; the recovery class was therefore run separately. Exact class selections and named TRX outcomes govern reconciliation.

## Execution Consumers And Boundaries

Add-on replacement proof: `ordinary-addons/` passed all twelve mapped identities (214ms), followed by a post-retirement `addons-phase/` gate (12/12, zero skips, 617ms). Peer review then required immediate no-partial-state checks after rejected inventory release, terminal refund rejection and refund replays. These additional typed public-state assertions passed all twelve cases in `addons-strengthened/` (230ms), preserving every original assertion; the final phase gate passed 12/12 in 634ms. No product fix was needed. Recovery proof in `ordinary-recovery/` passed all seven identities (196ms), including immediate unchanged-state snapshots after each denial and cleared opening timestamps immediately after pause. After retirement its `recovery-phase/` gate passed 7/7 with zero skips in 676ms. Independent adversarial review approved both sets after these additions; the primary recovery vote (60%) and secondary commercial/evidence vote (40%) have no unresolved finding. Static approval does not substitute for execution evidence.

`Explore.slnx` already includes `Event.Domain.UnitTests` and omits both wrappers. `.github/workflows/_build-test.yml` executes that ordinary project. Repository-wide non-Markdown executable/configuration search found no wrapper consumer beyond each wrapper's own project/configuration. No solution/workflow/package change is required.

The unchanged Domain baseline build passed with 184 pre-existing analyzer warnings and zero errors. No warnings are suppressed or repaired by this slice. Final named-case reconciliation, final Domain/architecture gates and candidate identity remain pending. Provider/migration, HTTP/authentication, UI/BFF/accessibility, AppHost/enabled workers, external protection, browser, load/benchmark and trusted CI admission remain owned by the deferred programme.

## Documentation Research And Provenance

On 2026-10-23 Context7 resolved `/thomhurst/tunit` and confirmed the public four-segment assembly/namespace/class/method tree filter and TRX output interfaces. Live web research consulted [Microsoft's MTP dotnet test reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp) and [MTP overview](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro). Newer web features are not assumed available in pinned TUnit 1.63.0 / MTP 2.3.3: the actual executable help confirms `--treenode-filter`, `--minimum-expected-tests`, `--report-trx` and `--results-directory`.

Functional handoff: preserve every repository-native public-operation assertion in the ordinary project; require exact named passing outcomes and retain separate reference/sensitivity/final artifacts. No external implementation source, expressive structure, package or asset is incorporated. The test design and organization derive from the existing project and approved plan; no new dependency or licence decision is introduced.
