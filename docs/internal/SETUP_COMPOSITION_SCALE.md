<!-- ABOUTME: Documents measured Setup composition profiles and fail-closed admission. -->
<!-- ABOUTME: Explains evidence binding, target compatibility, telemetry, and unchanged defaults. -->

# Setup Composition Scale Profiles

Setup composition scale profiles are measured workload manifests over the
existing authoritative parser limits. They do not define another grammar, tree,
wire identity, filesystem policy, serializer, or fallback. The default
`SetupCompositionCompiler` behavior and every value in
`SetupCompositionLimits.Default` remain unchanged.

## Measured Profiles

The repository-owned generator measures four synthetic, non-secret workloads:

| Profile | Source | Purpose |
|---|---|---|
| `small` | JSON | Low-count direct-source baseline |
| `medium` | YAML | Parser-event and scalar-conversion workload |
| `large` | Linux directory | Multi-file discovery, read, merge, and revalidation |
| `ceiling` | JSON | Exact 4,096-entry mapping boundary |

`eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs` records the
source shape, host/runtime/process limits, source/Core/Wire/target revisions,
warmups and measured iterations, median and p95 elapsed time, allocation and
GC counts, peak working set, cancellation, stack-overflow disposition, and
release artifact size/hash. The generated machine record is
`eng/setup-assistant/generated/composition-scale-profiles.json`; the human
measurement record is
`.omo/evidence/20260831-setup-assistant-security-and-portability/phase8-scale-results.md`.

Timing and allocation measurements are evidence, not flaky test thresholds.
Verification recompiles each deterministic workload and checks authoritative
artifact bytes and hashes without asserting wall-clock speed.

## Admission

`SetupCompositionScaleProfiles.Admit` requires:

1. an exact known profile name;
2. the exact generated `ArtifactDigest` for that measurement;
3. an enabled profile; and
4. a target maximum artifact size at least as large as the measured authoritative
   artifact.

Admission returns the exact immutable profile or one closed failure:
`UnknownProfile`, `ProfileDisabled`, `EvidenceMismatch`, or
`TargetIncompatible`. Rejection returns no profile and never clamps, selects a
smaller profile, falls back to the standard default, or changes compiler
behavior. `expanded` is deliberately known-disabled because no measured and
target-accepted evidence authorizes limits above the authoritative ceiling.

Every admitted profile exposes `SetupCompositionLimits.Default` as its
effective limit set. A future larger profile requires a new controlled
measurement, target-acceptance evidence, generated digest, and governance
review; it cannot be activated by editing runtime configuration.

## Target Compatibility

Each measured authoritative result is reparsed through the frozen v1alpha2 Wire
codec used by the target contract and must remain below
`ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes`. This checks
the target-consumed contract rather than trusting source size or local parser
success. Live endpoint and deployment-specific limits remain later live-target
gates and may only reduce admission.

## Telemetry Boundary

`SetupCompositionScaleTelemetry` is the complete allowed runtime measurement
surface:

- closed source kind;
- closed profile identifier;
- closed outcome;
- aggregate bytes, nodes, and files; and
- duration in microseconds.

Keys, values, paths, hashes, exception text, tenant/user identifiers, secrets,
provider coordinates, and application data are not telemetry fields. The
generated evidence contains synthetic artifact hashes for reproducibility;
those hashes are not runtime telemetry.

## Reproduction

Controlled measurement writes to an explicit non-product output directory:

```bash
dotnet run eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs -- \
  --measure /tmp/islamu-setup-composition-scale
```

Repository verification is non-mutating:

```bash
dotnet run eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs -- \
  --check
```

The check validates exact revisions and evidence digests, recompiles all four
workloads through Setup Core and the target Wire contract, and compares
normalized bytes sizes and SHA-256 hashes.
