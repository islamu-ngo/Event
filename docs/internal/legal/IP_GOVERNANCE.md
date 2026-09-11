<!-- ABOUTME: Canonical policy for IP provenance, clean-room research, dependency licensing, and audit evidence. -->
<!-- ABOUTME: Protects ISLAMU's CLA-backed alternative-licensing options without relicensing third-party material. -->

# IP Protection, Clean-Room Governance, And Audit Readiness

> **Audience:** Maintainers | Contributors | AI agents | Legal reviewers
> **Status:** Operational policy; legal conclusions and exceptions require qualified legal review
> **Owner:** Project Steward | Contributor Experience
> **Last Verified:** 2026-08-12
> **Source Anchors:** `legal/CLA.md`, `LICENSE`, `Directory.Packages.props`, `Directory.Build.props`, `.ci/scripts/validate-dependency-license-policy.cs`, `.github/workflows/_build-test.yml`, `docs/DUAL_VERSIONING.md`

This is the repository's source of truth for externally informed implementation, third-party material, dependency-license compatibility, and provenance evidence. It is an engineering control, not legal advice or a guarantee that a court, regulator, or licensor will agree with a project classification.

## Legal Posture And Authority Boundary

ISLAMU Event is publicly distributed under `AGPL-3.0-or-later`. The CLA grants the Project Steward broad inbound rights in contributor-owned Contributions so ISLAMU-owned material can also be offered under alternative terms selected by the Project Steward.

**Anti-SaaS Governance Invariant:** The Project Steward's alternative licensing authority is strictly constrained by a community covenant: alternative terms are limited to **Enterprise Internal-Use On-Premises/VPC deployments** (waiving AGPL Section 13 copyleft contagion over internal corporate infrastructure) and $0 non-profit/humanitarian grants. The Project Steward commits never to grant an alternative license permitting a third party to operate a closed-source, proprietary SaaS or cloud service. All SaaS offerings must operate under `AGPL-3.0-or-later`. See [I-VSD Strategy Review](../../../islamic-value-sensitive-design/governance/i-vsd-licensing-and-commercial-strategy.md).

The CLA grants no rights in third-party material. A contributor signature cannot cure copied code, missing authority, or an incompatible dependency. Every assembled distribution must comply with each third-party component's own terms even when ISLAMU-owned material is offered under another license.

| Material | Who controls outbound terms? | Rule |
|---|---|---|
| ISLAMU-owned code and valid CLA Contributions | Project Steward within the applicable rights | May follow the public AGPL path or another approved outbound path. |
| Third-party library, service, image, asset, dataset, generated output, or documentation | Its rightsholder or applicable license | Retains its own terms and must be evaluated for every intended distribution mode. |
| Unverified or copied material | Unknown or third party | Must not enter implementation context, the repository, build artifacts, or releases. |

## Non-Negotiable Rules

1. Do not copy third-party code, snippets, SQL, migrations, tests, comments, documentation prose, schemas with expressive selection, assets, or generated artifacts into this repository.
2. Do not provide third-party implementation source, decompiled output, disassembly, ASTs, or source-derived structural notes to an implementation agent or implementation context window.
3. Externally informed implementation starts from a sanitized functional specification describing observable behavior, constraints, inputs, outputs, errors, and edge cases only.
4. Independently design the implementation's structure, sequence, organization, naming, data model, control flow, UI composition, tests, and documentation using repository-native patterns.
5. Do not add a dependency whose terms prevent the assembled ISLAMU offering from being lawfully distributed under every outbound model the Project Steward intends to support. An exception requires documented legal and distribution approval before merge.
6. Do not represent the CLA as relicensing authority over third-party material.
7. Record provenance and the independent-design review before externally informed work is merged. Absence of external influence may be recorded as `Not externally informed`.

## Three Defensive Pillars

### 1. Literal-Copying Prevention

The clean-room boundary is stricter than a line-by-line similarity check. Prohibited material includes source and object-code-derived representations, exact internal naming, distinctive comments, tests, database migrations, query text, copied screen assets, documentation passages, and translations of code from one language or framework to another.

Public API or interoperability requirements may require exact identifiers or wire values. Treat those as externally constrained elements, record the source and necessity, copy no surrounding implementation expression, and escalate when contract or access terms are uncertain.

### 2. Non-Literal Copying And SSO Review

Different syntax or a different framework is not sufficient independence. Review the implementation's Structure, Sequence, and Organization (SSO), including module boundaries, domain decomposition, state transitions, data relationships, operation ordering, UI hierarchy, error taxonomy, and test arrangement.

Use Abstraction-Filtration-Comparison (AFC) as a conservative engineering review heuristic:

1. **Abstraction:** describe both the external behavior and proposed ISLAMU design at progressively higher levels, from user goal to workflows, subsystems, data, and operations.
2. **Filtration:** identify elements dictated by functionality, standards, interoperability, security, efficiency, platform constraints, public-domain material, or commonplace domain practice. Labeling an element filtered is a review judgement, not a legal conclusion.
3. **Comparison:** compare only the remaining discretionary expression. If distinctive ordering, grouping, naming, relationships, UI composition, or prose remains materially similar, redesign it or obtain qualified legal review before implementation continues.

Repository-native architecture is evidence of independent design when it follows real project requirements: Clean Architecture boundaries, typed aggregates, CQRS request handling, repository-owned persistence, EF Core configuration, server-authored HAL affordances, tenant isolation, and integer minor-unit money. These patterns are not a safe harbor by themselves.

### 3. Clean-Room Context Sanitization

The researcher or product analyst observes authorized public behavior and documentation, then produces a source-free handoff. The implementer receives only that handoff plus ISLAMU repository context.

If restricted source enters an implementation context:

1. Stop implementation immediately.
2. Discard unmerged output created from the contaminated context.
3. Record the incident without reproducing the restricted material.
4. Have an unexposed reviewer create a new functional specification from permitted observations.
5. Start implementation in a fresh context and obtain legal review when contamination scope is uncertain.

## Functional Ideas, Commonplace Elements, And *Scènes À Faire*

Functional goals, industry-standard flows, public standards, and elements dictated by external constraints may be unprotectable or receive narrow protection in some jurisdictions. EU software law distinguishes program expression from underlying ideas and principles, and EU case law distinguishes functionality from program expression. United States software cases use doctrines including merger, *scènes à faire*, and AFC.

This repository uses those concepts only to classify risk. A `scènes à faire` label is never self-approval. Record why the element is commonplace or constrained, preferably with multiple independent observations when the feature is not defined by a single public standard. Do not reproduce a source's expressive selection, arrangement, text, graphics, or detailed workflow merely because its high-level function is common.

## Clean-Room Workflow

### Phase 1 — Authorized Observation And Functional Specification

- Prefer public product behavior, public documentation, standards, protocols, and independently observable inputs and outputs.
- Record source title/URL, access date, access basis, and which facts were observed. Do not archive source excerpts, source trees, or third-party assets.
- Describe what users can do, business constraints, failure modes, accessibility requirements, security requirements, and acceptance criteria.
- Separate observations from assumptions and design decisions.

### Phase 2 — Source-Free Handoff

- Produce the handoff with the template in `.agents/skills/ip-clean-room/resources/audit-record-template.md`.
- Include no third-party identifiers unless needed for source provenance or an interoperability contract.
- Attest that no implementation source, snippet, AST, decompiled artifact, SQL, migration, test, comment, or asset is present.
- End the research context. Start implementation from the sanitized handoff in a fresh context.

### Phase 3 — Independent Implementation And SSO Review

- Derive the design from ISLAMU requirements, current repository architecture, and applicable standards.
- Record at least one meaningful independent design choice in domain model, workflow, persistence, API/HAL, UI, or failure handling.
- Run the AFC/SSO checklist before review. Similarity that is not dictated by a functional constraint is a blocker.

### Phase 4 — Provenance And Audit Evidence

- Link the sanitized handoff, source register, issue/intent, design decision, dependency review, tests, and PR.
- Append the durable conclusion to `dev/_journal/journal.md` when it is non-obvious and reusable.
- Preserve facts and hashes through normal Git history; do not place restricted content in evidence artifacts.

## Research Artifact Storage Decision

Sanitized feature-specific research belongs in `dev/active/<workstream>/` by default. Promote only durable, implementation-independent requirements into canonical `docs/` pages. Do not create “non-attribution” reports: audit readiness requires source identity and access provenance, while clean-room isolation requires excluding source expression. Raw excerpts, screenshots, downloads, or source-derived artifacts must remain outside the repository and outside implementation contexts.

## Dependency And License Gate

Before adding or updating a package, library, container image, generated component, font, asset, or dataset, record:

- exact component and version;
- direct, transitive, build-only, test-only, optional-service, or shipped-runtime role;
- license expression and authoritative terms;
- obligations for the public AGPL distribution and every intended alternative offering;
- whether terms attach only to the component or restrict the combined/derivative work;
- notices, source-offer, attribution, patent, trademark, hosting, seat, field-of-use, redistribution, or sublicensing requirements;
- approval, rejection, replacement, or separately licensed path.

Signed configuration extension packs remain third-party inputs even when their
signature is trusted. Admission requires an allowlisted issuer/key, compatible
declarative contract version, approved license expression, retained provenance,
and independent payload validation. A signature proves origin/integrity only;
it cannot waive licensing, authorize code, plugins, SQL, migrations, scripts,
or grant target administration rights. Imported legal-template provenance must
retain template ID/version, source kind, license expression, and review
reference. Legal Markdown must be independently reviewed for the target
operator and jurisdiction; portability is not permission to copy unlicensed
text or a claim of legal compliance.

The CI command `dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .` is mandatory minimum evidence for dependency changes. It checks the repository allow/deny policy and visible exceptions; it is not legal advice and cannot prove that a commercial contract, unusual exception, asset license, service terms, or assembled distribution is compatible.

A component is blocked when it would force terms onto ISLAMU-owned material, prohibit an intended distribution or hosting model, or otherwise prevent the Project Steward from offering the ISLAMU-owned work under a selected outbound license. Resolve the blocker by choosing a compatible version or replacement, or by obtaining documented separate rights that cover every intended build and distribution. Merely making a component optional, loading it dynamically, or moving it to another process does not waive review.

### Existing AutoMapper And MediatR Precedent

The repository's existing dual-versioning is the reference pattern, not a blanket exception:

- default self-hoster and contributor builds pin AutoMapper `14.0.0` and MediatR `12.5.0`, the repository-documented last permissively licensed releases;
- newer commercial releases are selected only by the explicit `UseCommercialLuckyPennyLibraries=true` MSBuild opt-in or `USE_COMMERCIAL_LUCKYPENNY_LIBS=true` Docker build argument; `AUTOMAPPER_COMMERCIAL_VERSION` and `MEDIATR_COMMERCIAL_VERSION` may feed the corresponding build-time version overrides;
- the commercial runtime path requires its own `LUCKYPENNY_LICENSE_KEY` configuration and is not silently imposed on the default build;
- FOSS lock files keep the default dependency graph deterministic, while the CI dependency-license audit keeps exceptions visible.

See `docs/DUAL_VERSIONING.md`, `Directory.Packages.props`, `Directory.Build.props`, and `.env.example`. Any future dual-version path must document the same default/opt-in boundary, security posture, license ownership, lock-file behavior, and distribution evidence. It must not assume that the AutoMapper/MediatR decision approves another vendor or license.

## PR Evidence And Stop Conditions

A PR must state one of:

- `Not externally informed; no dependency changed`; or
- the sanitized handoff and provenance record, independent SSO review, and dependency-license evidence.

Stop and request qualified legal review when rights, access terms, protectable expression, reverse engineering, interoperability exceptions, contributor authority, commercial redistribution, or dependency compatibility is unclear. Do not merge on an undocumented assumption.

## Primary Legal References

- [Directive 2009/24/EC on the legal protection of computer programs](https://eur-lex.europa.eu/legal-content/EN/ALL/?uri=CELEX:32009L0024) — EU distinction between protected expression and underlying ideas/principles.
- [Court of Justice, Case C-406/10 summary](https://eur-lex.europa.eu/legal-content/EN/SUM/?uri=celex:62010CJ0406) — functionality, programming language, and data-file formats versus protected expression.
- [U.S. Copyright Act, 17 U.S.C. § 102](https://www.law.cornell.edu/uscode/text/17/102) — idea, procedure, process, system, and method-of-operation boundary.
- [U.S. Copyright Office Circular 61](https://www.copyright.gov/circs/circ61.pdf) — computer-program registration guidance and scope limits.

## Related Repository Sources

- [CLA](../../../legal/CLA.md)
- [Contribution Legal Governance](CONTRIBUTION_GOVERNANCE.md)
- [Dual-Versioning Strategy](../DUAL_VERSIONING.md)
- [CI/CD Governance](../CI_CD_GOVERNANCE.md)
- [Clean-Room Skill](../../../.agents/skills/ip-clean-room/SKILL.md)
