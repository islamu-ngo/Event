---
name: debug-issue
description: "Load when diagnosing a bug, exception, regression, wrong result, failing flow, or unknown root cause in repository code using callers/callees, execution flows, recent changes, and impact analysis; not for implementing a known fix without investigation."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Knowledge-graph and disciplined loop workflow for tracing repository bugs to root cause. -->
<!-- ABOUTME: Uses red-capable feedback loops, minimisation, falsifiable hypotheses, tagged probes, and impact analysis. -->

# Debug Issue: Disciplined Diagnosis Loop

A disciplined 6-phase protocol for diagnosing defects and regressions. Never skip phases without explicit justification.

## Phase 1: Build a Deterministic Red Feedback Loop (Spend 90% of Effort Here)

> [!CRITICAL]
> **Hypothesis Gate**: You are strictly forbidden from reading code to form speculative theories or applying fixes before you have created **one fast (<2s), deterministic command** that drives the code path and asserts the user's exact symptom (goes **RED** on this bug, **GREEN** when fixed).

Ways to construct the red command (in priority order):
1. **Failing TUnit Test**: Author a targeted unit/integration test using `--treenode-filter "/*/*/*<TestClass>/*"`.
2. **HTTP / Integration Request**: A curl/HTTP call asserting expected status, RFC 7807 ProblemDetails, or HAL `_links`.
3. **Throwaway Invariant Harness**: A minimal invocation in a test fixture exercising the failing CQS handler or domain aggregate.

**Phase 1 Gate**: Name the single test command and execute it once, observing the expected red failure.

## Phase 2: Reproduce and Minimise

1. Confirm the failure matches the user's reported symptom (not an adjacent unrelated error).
2. **Minimise**: Cut inputs, configuration, request payloads, and setup steps **one at a time**, re-running the red loop after each cut. Keep only what is strictly *load-bearing*. Done when removing any remaining input makes the loop go green.

## Phase 3: 3–5 Ranked Falsifiable Hypotheses

Generate 3–5 ranked hypotheses before testing any of them. Every hypothesis must make a concrete, testable prediction:
> **Format:** *"If `<X>` is the root cause, then `<changing Y>` will make the bug disappear / `<changing Z>` will make it worse."*

If you cannot state the falsifiable prediction, the hypothesis is invalid.

## Phase 4: Targeted Probing & Tagged Debug Logs

1. Use knowledge-graph tools (`get_minimal_context`, `query_graph_tool` with `callers_of`/`callees_of`, `get_affected_flows_tool`) to trace the execution flow.
2. If temporary logging probes are needed, **tag every debug log with a unique prefix**: `[DEBUG-<random_hex>]` (e.g. `[DEBUG-4a1f]`).
3. This guarantees that `grep "[DEBUG-"` ensures 100% clean log removal before merge.

## Phase 5: Fix and Regression Seam Verification

1. Write the regression test at the **correct public seam** (native CQS handler or API route).
2. **Seam Deficiency Finding**: If no clean public interface exists to test the bug without shallow mocking, that missing seam is an architectural defect. Record it in the diagnosis summary.
3. Apply the minimal fix to satisfy the failing test.
4. Watch the test turn **GREEN**.
5. Re-run the Phase 1 loop against the original un-minimised scenario.

## Phase 6: Cleanup & Verification

- [ ] Original reproduction passes (feedback loop is green).
- [ ] Regression test passes via `--treenode-filter`.
- [ ] All `[DEBUG-...]` temporary logs removed (`grep "[DEBUG-"` returns 0 matches).
- [ ] Successful hypothesis and root cause documented in the final summary.

## Token Efficiency Rules
- ALWAYS start with `get_minimal_context(task="<your task>")` before any other graph tool.
- Use `detail_level="minimal"` on all calls. Only escalate to "standard" when minimal is insufficient.
- Target: complete any review/debug/refactor task in ≤5 tool calls and ≤800 total output tokens.
