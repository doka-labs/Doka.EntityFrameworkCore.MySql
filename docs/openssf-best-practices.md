# OpenSSF Best Practices Evidence

This document maps repository-owned evidence to the OpenSSF Best Practices
Silver and Gold documentation criteria. The official criteria and the project
entry remain the source of truth. This page must not mark a criterion met when
the required operational or organizational property does not exist.

## Official Project State

- [Project entry](https://www.bestpractices.dev/en/projects/13999)
- Passing achieved: yes
- Silver achieved: no
- Gold achieved: no
- Saved form completion: Passing 100 percent, Silver 15 percent, Gold 17 percent
- Repository evidence review: 2026-08-21

The percentages on the project entry reflect answers saved in the OpenSSF
form. They are not an automatic repository scan. A repository document may be
ready while its form answer remains unknown, and a form answer must not claim
an organizational property that documentation alone cannot create.

## Silver Documentation Evidence

| Criterion | Repository evidence | Current disposition |
| --- | --- | --- |
| Contribution requirements | [`CONTRIBUTING.md`](../CONTRIBUTING.md) | Ready |
| DCO or equivalent legal mechanism | None | Not adopted; this is a `SHOULD` criterion and requires a recorded decision or implementation |
| Governance model | [`GOVERNANCE.md`](../GOVERNANCE.md) | Ready |
| Code of conduct | [`CODE_OF_CONDUCT.md`](../CODE_OF_CONDUCT.md) | Ready |
| Roles and responsibilities | [`GOVERNANCE.md`](../GOVERNANCE.md) | Ready |
| Access continuity | [`GOVERNANCE.md`](../GOVERNANCE.md) | Blocked until a real and tested succession arrangement exists |
| Bus factor of at least two | [`GOVERNANCE.md`](../GOVERNANCE.md) | Not met; Silver permits a justified `SHOULD` disposition |
| One-year roadmap with non-goals | [`ROADMAP.md`](../ROADMAP.md) | Ready |
| High-level architecture | [Provider architecture](architecture.md) | Ready |
| Security requirements | [`SECURITY.md`](../SECURITY.md) and the [threat model](security/threat-model.md) | Ready |
| Quick start | [`README.md`](../README.md#quick-start) | Ready |
| Current documentation | [Documentation index](README.md) and the executable documentation contract | Ready, subject to every change keeping the gate green |
| Achievement link | [`README.md`](../README.md) | Ready |
| Vulnerability response | [`SECURITY.md`](../SECURITY.md) | Ready |
| Coding standards and enforcement | [`CONTRIBUTING.md`](../CONTRIBUTING.md), [`.editorconfig`](../.editorconfig), and quality gates | Ready |
| Signed release verification | [Release verification](security/release-verification.md) | Ready |
| Security assurance case | [Security assurance case](security/assurance-case.md) | Ready |

The table covers documentation-facing criteria. Silver also contains build,
dependency, test, coverage, cryptography, input-validation, and analysis
criteria that require executable or external evidence in the OpenSSF form.

## Gold Preparation

Gold is not currently claimable because Silver, bus factor, independent
contributors, and independent-review evidence are not satisfied. The
repository can still prepare criteria that do not depend on inventing people
or history.

| Criterion | Current evidence or preparation | Current disposition |
| --- | --- | --- |
| Code-review requirements | [`CONTRIBUTING.md`](../CONTRIBUTING.md) defines conduct, checks, and acceptance | Ready |
| Independent review of at least half of changes | Repository history | Not claimed |
| Copyright statement in every source file | Package metadata names Dominic Kalkbrenner, but source headers are absent | Open |
| License statement in every source file | Root MIT license exists, but source headers are absent | Open |
| Small tasks for new contributors | Must be represented by current public issues, not an empty template | Open |
| Required cryptographic 2FA | GitHub organization and repository settings | External evidence required |
| Reproducible build | Requires a bit-for-bit independent rebuild result, not only deterministic build settings | Not claimed by this documentation |
| 90 percent statement and 80 percent branch coverage | [`eng/coverage-policy.json`](../eng/coverage-policy.json) | Current enforced assembly floors do not satisfy Gold |
| Human security review within five years | Threat model and assurance case define scope | Open until a dated review records reviewer, findings, and resolution |
| Hardening | Threat model and assurance case | Evidence prepared; final disposition requires criterion-by-criterion review |
| Dynamic analysis for major releases | Release qualification evidence | Final disposition requires confirmation that the invoked tools meet the OpenSSF definition |

The eventual per-file header policy must cover every authored source language,
generated-source ownership, and formats that cannot carry comments. The known
package metadata supports these candidate SPDX statements:

```text
SPDX-FileCopyrightText: 2026 Dominic Kalkbrenner
SPDX-License-Identifier: MIT
```

They must not be applied partially. A future header change needs one complete
inventory, language-appropriate comment syntax, generated-file handling, and a
gate that rejects newly unclassified source files.

## Update Procedure

1. Re-read the current official Silver and Gold criteria.
2. Verify the repository evidence and any external organization settings.
3. Update this page when a disposition or evidence owner changes.
4. Update the official project form with the exact public URL and an honest
   `Met`, `Unmet`, `N/A`, or justification.
5. Re-read the public project entry and confirm that the saved answer matches
   the intended criterion.

Unknown is preferable to a claim supported only by circular documentation.

## Primary Sources

- OpenSSF Best Practices, [Silver criteria](https://www.bestpractices.dev/en/criteria/1),
  retrieved 2026-08-21.
- OpenSSF Best Practices, [Gold criteria](https://www.bestpractices.dev/en/criteria/2),
  retrieved 2026-08-21.
- OpenSSF Best Practices, [Doka.EntityFrameworkCore.MySql project entry](https://www.bestpractices.dev/en/projects/13999),
  retrieved 2026-08-21.
