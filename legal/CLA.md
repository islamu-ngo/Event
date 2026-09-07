<!-- ABOUTME: Contributor License Agreement for inbound ISLAMU Event contributions. -->
<!-- ABOUTME: Defines project-steward inbound rights, signature evidence, and AGPL alternative-licensing terms. -->

# ISLAMU Event Contributor License Agreement

> **Version:** 1.0
> **Status:** Operational draft pending legal review
> **Owner:** ISLAMU (ASBL en formation) | Platform/Ops
> **Maintainer of record:** Amir Akrari
> **Purpose:** Preserve the ISLAMU (ASBL en formation) organization's ability to keep ISLAMU Event public under AGPL-3.0-or-later while also offering alternative terms for legitimate sustainability, enterprise internal-use compliance, nonprofit, humanitarian, public-sector, and procurement-restricted on-premises deployments—with an explicit Project Steward governance commitment against closed-source proprietary SaaS commercialization.

A new CLA version is published by bumping the `Version` line above and updating the `path-to-signatures` version segment in `.github/workflows/cla.yml` (e.g. `signatures/v1.0/cla.json`). Version bumps are strictly reserved for material modifications to licensing terms or contributor rights. Earlier signatures remain valid only for the CLA version under which they were recorded.

This Agreement is structured to be self-executing across the lifecycle of ISLAMU: the transition from association in formation (*ASBL en formation*) to formally registered non-profit association with legal personality (*ISLAMU ASBL*) does not constitute a version change, does not require bumping the version or changing signature paths, and preserves the full validity of all existing signatures in `signatures/v1.0/cla.json` without requiring contributors to re-sign.

This Contributor License Agreement (CLA) applies to contributions submitted to ISLAMU Event, including API, Blazor, infrastructure, workflow, documentation, configuration, generated artifacts, tests, and related project materials.

This document is not personal legal advice. Contributors should only sign if they have the rights needed to grant these permissions.

## Why A CLA Alongside AGPL-3.0-Or-Later?

ISLAMU Event is publicly distributed under AGPL-3.0-or-later. That public license remains in place for the open-source project and for anyone who receives ISLAMU Event under AGPL-3.0-or-later.

This CLA is an additional inbound license from contributors to the ISLAMU project steward. It allows the ISLAMU non-profit organization (ISLAMU ASBL en formation), once formally incorporated with legal personality, to also offer ISLAMU Event and contributed material under alternative terms when needed for legitimate deployments, such as enterprise internal-use on-premises/VPC compliance (where corporate policies ban AGPL network copyleft contagion for private intranet tools), humanitarian missions, public-sector procurement, or commercial sustainability.

The CLA does not take ownership away from contributors. Contributors keep ownership of their own Contributions. The CLA gives the Project Steward the rights needed to maintain, distribute, sublicense, relicense, commercialize, and protect ISLAMU Event sustainably.

### Community Protection & The Anti-SaaS Covenant

The Project Steward is bound by a strict community stewardship principle: **No entity may use an alternative license from ISLAMU to build an unfair, closed-source Software-as-a-Service (SaaS) or managed cloud service that denies source code back to the community.**

1. **Enterprise Internal-Use Only:** Any alternative commercial or institutional license granted by the Project Steward is strictly restricted to **internal organizational operations and private on-premises/VPC events**. It waives AGPL copyleft fears for private enterprise integrations (e.g., Active Directory, internal APIs) but expressly prohibits reselling or offering ISLAMU Event as an external hosted service or SaaS to third parties.
2. **Universal SaaS Parity:** Any party wishing to offer ISLAMU Event as a public or commercial SaaS to third parties must do so under the public **AGPL-3.0-or-later** license. This legally guarantees that all SaaS operators must publish their source code modifications, ensuring that no vendor can create an asymmetric proprietary feature advantage over the open-source community.

## Agreement

By posting the CLA signature comment on a pull request, You and the Project Steward agree to the terms below.

### Multi-Phase Stewardship and Automatic Assumption

1. **Pre-Incorporation Representation and Custody:** Until the ISLAMU non-profit association is formally incorporated with legal personality under Belgian law, Amir Akrari acts solely as Interim Representative and Custodian for ISLAMU Event on behalf of ISLAMU (ASBL en formation). The Interim Representative exercises the rights and licenses granted under this Agreement solely in a representative and custodial capacity for the purpose of operating, maintaining, protecting, licensing, and transferring ISLAMU Event to the future non-profit association.
2. **Statutory Retroactive Ratification (Article 2:2 CSA):** Upon incorporation, the Organization (ISLAMU ASBL) shall formally ratify and assume this Agreement in accordance with Article 2:2 of the Belgian Companies and Associations Code (*Code des sociétés et des associations* / *Wetboek van vennootschappen en verenigingen*). Pursuant to applicable law, upon such assumption all commitments, licenses, and grants made under this Agreement are deemed by operation of law to have been contracted directly by the Organization from the date of Your signature (*dès l'origine*), and the Interim Representative is fully and retroactively released from any personal liability in connection with the stewardship of this Agreement.
3. **Direct Third-Party Beneficiary (*Stipulation pour autrui*):** You expressly agree that all grants, rights, licenses, warranties, and covenants made by You under this Agreement are executed both with the Interim Representative and directly for the benefit of the future ISLAMU ASBL as an intended third-party beneficiary (*stipulation pour autrui au profit d'une personne future* pursuant to Articles 5.109 through 5.111 of the Belgian Civil Code). Upon acquiring legal personality, the Organization may directly enforce and enjoy all rights and licenses granted herein without any requirement of separate assignment or notice.
4. **Advance Irrevocable Consent to Assignment and Novation:** You irrevocably consent in advance to the automatic assignment, transfer, and novation of this Agreement and all rights, licenses, titles, and covenants granted herein from the Interim Representative to ISLAMU ASBL immediately upon its acquisition of legal personality. No further notice, amendment, re-execution, or re-signature is required.
5. **Signature Continuity and Invariance:** Signatures recorded under Version 1.0 of this Agreement during the pre-incorporation period remain perpetually valid, effective, and binding in favor of ISLAMU ASBL following its incorporation. The corporate transition from association in formation to registered legal personality shall not invalidate existing signatures, alter the validity of records on the `cla-signatures` branch, or require contributors to re-sign.

## Definitions

**Contribution** means any code, documentation, design, test, configuration, workflow, generated artifact update, or other material You submit intentionally to ISLAMU Event.

A communication is not a Contribution if You conspicuously mark it in writing as `Not a Contribution`.

**Third-Party Materials** means software, libraries, container images, services, datasets, fonts, media, documentation, or other material whose rights are owned or controlled by someone other than You or the Project Steward.

**You** means the individual contributor and, if applicable, the organization on whose behalf the Contribution is submitted.

**Organization** means the ISLAMU non-profit association (currently ISLAMU ASBL en formation, becoming ISLAMU ASBL upon acquisition of legal personality pursuant to Belgian law), and its lawful corporate successors and assigns.

**Interim Representative** (or **Trustee**) means Amir Akrari, acting as the interim legal representative and custodian of all rights and licenses granted under this Agreement on behalf of ISLAMU (ASBL en formation). Upon acquisition of legal personality by the Organization and ratification of this Agreement, all rights and duties of the Interim Representative under this Agreement are assumed by and transferred to the Organization free of charge and without encumbrance.

**Project Steward** means:
(a) prior to the formal incorporation of the Organization with legal personality, ISLAMU (ASBL en formation), acting through its Interim Representative solely in the custodial capacity described herein; and
(b) upon and after formal incorporation and assumption of this Agreement, the Organization (ISLAMU ASBL), together with its lawful corporate successors, affiliates, and permitted assigns.

**Downstream Recipients** means users, customers, sublicensees, distributors, hosting partners, public-sector partners, nonprofit partners, and other recipients who receive ISLAMU Event or related offerings from or on behalf of the Project Steward.

## Copyright License Grant

Subject to this Agreement, You grant to the Project Steward, its successors and assigns, and to recipients of software, services, or materials distributed or provided by or on behalf of the Project Steward a perpetual, worldwide, non-exclusive, no-charge, royalty-free, irrevocable, transferable, and sublicensable copyright license to reproduce, use, execute, host, operate, modify, prepare derivative works of, publicly display, publicly perform, publish, distribute, make available, sublicense, relicense, sell, offer for sale, and otherwise exploit Your Contributions and derivative works thereof, as part of ISLAMU Event or related offerings, under any license or commercial terms selected by the Project Steward.

## Trademarks

This Agreement does not grant You, the Project Steward, or Downstream Recipients any rights to use any trade names, trademarks, service marks, or logos of the Project Steward, the Organization, or any contributor, except as required for reasonable and customary use in describing the origin of the software or as explicitly granted in a separate written agreement (such as through the Official Partner Program).

## Third-Party Materials And Outbound Licensing

This Agreement grants rights only in Contributions and other rights that You own or are authorized to license. It does not grant, expand, restrict, supersede, or relicense any rights in Third-Party Materials.

ISLAMU Event may interoperate with, reference, or be distributed alongside Third-Party Materials. Each Third-Party Material retains its respective license, public-domain status, or other applicable terms. Any alternative license or commercial terms selected by the Project Steward apply only to Contributions and other material the Project Steward owns or is separately authorized to license. Nothing in those alternative terms may be interpreted as removing rights that a recipient receives directly under an applicable third-party license or public-domain dedication.

You must identify Third-Party Materials included in or required by a Contribution and provide the available source, license, notice, provenance, and modification information needed for the Project Steward to evaluate and comply with their terms. Signing this Agreement does not cure missing authority or make Third-Party Materials relicensable by the Project Steward.

## Patent License Grant

Subject to this Agreement, You grant to the Project Steward, its successors and assigns, and to recipients of software, services, or materials distributed or provided by or on behalf of the Project Steward a perpetual, worldwide, non-exclusive, no-charge, royalty-free, irrevocable patent license to make, have made, use, offer to sell, sell, import, and otherwise transfer ISLAMU Event, where such license applies only to patent claims licensable by You that are necessarily infringed by Your Contribution alone or by combination of Your Contribution with ISLAMU Event to which the Contribution was submitted.

If any entity institutes patent litigation alleging that Your Contribution or ISLAMU Event incorporating Your Contribution infringes a patent, then any patent licenses granted to that entity under this Agreement terminate as of the date such litigation is filed.

## Contributor Ownership

You keep ownership of your Contributions. This CLA does not prevent you from using, licensing, or publishing your own Contributions elsewhere.

## Representations

You confirm that:

- you have the legal right to submit the Contribution and grant this CLA;
- your Contribution is your original work or you have permission to submit it;
- if your employer, client, school, or another organization owns rights in the Contribution, you are authorized to submit it under this CLA;
- you are not knowingly submitting material that violates another party's rights;
- you will identify third-party code, assets, generated output, or license restrictions that are not obvious from the Contribution.

If a Contribution is owned or controlled by Your employer, client, school, or another legal entity, You represent that You are authorized to submit the Contribution under this Agreement. The Project Steward may require a separate Corporate Contributor License Agreement or written confirmation before accepting the Contribution.

## Moral Rights

To the extent allowed by law, You waive or agree not to assert moral rights that would interfere with the Project Steward's or Downstream Recipients' exercise of this CLA. Where waiver is not allowed, You consent to the Project Steward and Downstream Recipients exercising the rights granted above.

## Privacy And Signature Records

By signing, You understand that the Project Steward will store and process signature records for legal, compliance, provenance, and project-administration purposes. These records may include Your GitHub username, GitHub user ID, pull request number, signing comment ID and body, timestamp, and CLA version.

Signature records recorded on the dedicated `cla-signatures` branch (under `signatures/v1.0/cla.json`) form an immutable electronic audit trail serving as authoritative legal evidence of signature and agreement under Article 8.1 and Article 8.9 of the Belgian Civil Code. Signature records are retained perpetually to document the unbroken chain of licensing title for Contributions and may be publicly visible.

## No Obligation

The Project Steward is not required to use, merge, publish, maintain, support, or distribute any Contribution.

## Support

You are not required to provide support for Your Contributions.

## No Warranty

Contributions are provided as-is, without warranties or conditions of any kind, unless required by applicable law or separately agreed in writing.

## Severability

If any provision of this Agreement is unenforceable, the remaining provisions remain in effect.

## Survival

The license grants, representations, disclaimers, and provisions necessary to interpret or enforce this Agreement survive termination or withdrawal of any Contribution.

## Governing Law

This Agreement is governed by the laws of Belgium, unless mandatory law provides otherwise.

## Venue

Courts located in Brussels, Belgium shall have jurisdiction, unless mandatory law provides otherwise.

## How To Sign

Every non-bot contributor to a pull request must sign this CLA by posting the following exact comment on the pull request:

```text
I have read and agree to the ISLAMU Event Contributor License Agreement v1.0, and I confirm that I have the right to submit my contribution under it.
```

The ISLAMU CLA workflow (powered by [cla-assistant/github-action][cla-action-link], pinned at `contributor-assistant/github-action@ca4a40a7d1004f18d9960b404b97e5f30a505a08 # v2.6.1`) records each signature in `signatures/v1.0/cla.json` on the dedicated `cla-signatures` branch. The recorded entry stores the contributor's GitHub username, user ID, the pull request number, the signing comment ID and body, and an ISO-8601 timestamp.

If the CLA version changes due to a material revision of licensing terms or contributor rights, the workflow switches the signatures file to a new version segment (for example `signatures/v2.0/cla.json`) and contributors must re-sign the new version. Corporate lifecycle milestones—including ISLAMU acquiring legal personality and ratifying this Agreement pursuant to Article 2:2 CSA—operate strictly within Version 1.0, do not switch the signatures file, and do not require re-signing.

To re-run the CLA check after signing, post a comment containing only `recheck`.

The CLA workflow runs on `pull_request_target` and `issue_comment` events only. It does not checkout, build, test, cache, restore packages, or execute pull-request head code.

The CLA workflow uses explicit `GITHUB_TOKEN` permissions for signature storage, pull-request comments, issue comments, and commit statuses. It must not receive deployment, package, OIDC, registry, or secret-bearing credentials for pull-request head code.

[cla-action-link]: https://github.com/contributor-assistant/github-action
