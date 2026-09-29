<!-- ABOUTME: Clean-room workflow for observing external behavior and producing a source-free implementation handoff. -->
<!-- ABOUTME: Defines allowed evidence, context separation, storage, and contamination response. -->

# Research And Handoff Workflow

## 1. Establish Authority

- Confirm the material is publicly observable or otherwise lawfully accessible for the planned research.
- Read terms governing access, automated collection, documentation reuse, and interoperability when relevant.
- Stop for qualified legal review if access or use rights are unclear.

## 2. Observe Behavior, Not Implementation

Allowed inputs are public behavior, public documentation, standards, protocols, and independently observable inputs/outputs. Record source title, URL, access date, and the facts observed.

Do not open, download, paste, summarize, or transform third-party source, snippets, decompiled artifacts, disassembly, ASTs, SQL, migrations, tests, comments, internal schemas, or assets for the implementation team. Do not retain copied screenshots or prose in the repository.

## 3. Write The Functional Specification

Capture only:

- user goals and actors;
- inputs, outputs, state transitions, and errors;
- business, security, privacy, accessibility, and interoperability constraints;
- observable edge cases and acceptance criteria;
- assumptions and unresolved questions, clearly labeled.

Prefer multiple independent observations for commonplace product behavior. A single standard or protocol can remain authoritative for exact interoperability requirements.

## 4. Sanitize And Hand Off

Use [Audit Record Template](audit-record-template.md). Confirm that the handoff contains no source expression or source-derived internal structure. Store feature-specific handoffs under `dev/active/<workstream>/`; promote only durable conclusions to governing docs.

End the research context. The implementer starts in a fresh context with only the sanitized handoff, ISLAMU repository material, and permitted standards/interface facts.

## 5. Contamination Response

If restricted material enters implementation context:

1. stop;
2. discard unmerged output from that context;
3. record the incident without reproducing the material;
4. assign an unexposed reviewer to recreate the functional specification from permitted observations;
5. restart in a fresh context and escalate uncertain scope.

