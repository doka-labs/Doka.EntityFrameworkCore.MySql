# Project Governance

This document defines how `Doka.EntityFrameworkCore.MySql` is governed, who
holds project roles, how decisions are accepted, and which continuity limits
are currently carried openly.

## Project Stewardship

The repository is hosted by the Doka Labs GitHub organization. Dominic
Kalkbrenner (`@kdominic89`) is the lead maintainer, release maintainer, and
security responder. The package metadata identifies Dominic Kalkbrenner as the
author and copyright holder and Doka Labs as the company.

The lead maintainer is accountable for:

- accepting or rejecting changes to the supported product and public API;
- maintaining the support matrix, compatibility policy, and release roadmap;
- triaging defects, security reports, and dependency advisories;
- preserving repository, package, signing, and publication controls; and
- publishing releases only through the reviewed release procedure.

The same person may hold more than one role. Role concentration does not turn
an unmet continuity or independent-review criterion into a met criterion.

## Roles and Responsibilities

| Role | Responsibilities | Current holder |
| --- | --- | --- |
| Lead maintainer | Product direction, governance decisions, support policy, final technical arbitration | Dominic Kalkbrenner (`@kdominic89`) |
| Release maintainer | Candidate qualification, signed tags, protected publication approval, release recovery | Dominic Kalkbrenner (`@kdominic89`) |
| Security responder | Private intake, triage, coordinated disclosure, remediation ownership | Dominic Kalkbrenner (`@kdominic89`) |
| Reviewer | Independent assessment of correctness, security, compatibility, evidence, and maintainability | Assigned per pull request |
| Contributor | Proposes focused changes with the required tests, documentation, and evidence | Any participant following `CONTRIBUTING.md` |

A role change is made through a reviewed pull request that updates this table
and any affected repository, package, environment, signing, and incident
response controls. A title without the required access is not an active role.

## Decision Model

Issues and pull requests are the normal public decision record. The lead
maintainer accepts routine changes after the applicable review and automated
checks complete.

A material decision requires an Architecture Decision Record when it changes
one or more of the following:

- public API or supported-engine policy;
- security or privacy boundaries;
- dependency, build, test, benchmark, or release authority;
- compatibility or servicing commitments; or
- an architectural ownership or dependency direction.

ADRs follow the repository's validated MADR profile. An accepted ADR records
the decision makers, alternatives, consequences, primary sources, executable
confirmation, and later amendments. The ADR corpus explains why a decision
exists; this file remains the authority for who may make it.

When evidence changes an accepted decision, the project amends or supersedes
the ADR. It does not silently reinterpret the earlier decision in code.

## Change Acceptance

All repository changes use a pull request unless an external incident makes
the normal path unavailable. The complete author and reviewer contract is in
`CONTRIBUTING.md`.

A change is acceptable only when:

- its scope and motivation are reviewable;
- every applicable automated check passes;
- behavior changes carry proportionate positive and negative tests;
- public API, engine differences, documentation, and evidence impacts are
  explicitly classified;
- security and privacy boundaries have no unresolved finding; and
- review comments and requested changes are resolved without hiding dissent.

An approval applies only to the reviewed commit. Material changes after an
approval require renewed review. Automated checks support human review but do
not replace it.

The project does not claim the OpenSSF Gold independent-review criterion until
repository history demonstrates that at least half of proposed modifications
were reviewed before release by someone other than the author.

## Conflicts of Interest

An author does not review their own change as independent evidence. A person
who is the subject of a conduct or security report does not decide that report
when an unconflicted responder is available. The conflict and available
escalation path are disclosed when independent internal review is unavailable.

The Code of Conduct owns community enforcement. `SECURITY.md` owns private
vulnerability handling.

## Continuity and Succession

The current role registry has one release-capable maintainer. The project
therefore does not currently claim either of these OpenSSF Best Practices
criteria:

- continuity after the loss of any one person; or
- a bus factor of two or more.

Silver continuity becomes claimable only after one of these arrangements is
implemented and tested:

1. a second maintainer receives the legal authority and independently usable
   GitHub, NuGet, security-reporting, signing, and release access needed to
   accept changes and publish within one week; or
2. a sealed succession arrangement provides those rights and recovery
   materials to an identified successor without placing credentials in the
   repository.

The public evidence must name the arrangement and its last verification date
without disclosing credentials. Recovery is not considered tested merely
because a second account or encrypted archive exists.

## Governance Review

Review this document when a role holder, organization setting, publication
credential, signing key, support line, or decision process changes, and at
least once per calendar year. The review must also reconcile `ROADMAP.md`,
`SECURITY.md`, the threat model, and the repository-security runbook.

## Primary Sources

- OpenSSF Best Practices, [Silver criteria](https://www.bestpractices.dev/en/criteria/1),
  retrieved 2026-08-21. The governance, roles, continuity, bus-factor, roadmap,
  architecture, and assurance-case requirements are the external criteria
  addressed by this governance system.
- OpenSSF Best Practices, [Gold criteria](https://www.bestpractices.dev/en/criteria/2),
  retrieved 2026-08-21. Independent review and contributor requirements remain
  separate from the existence of this document.
