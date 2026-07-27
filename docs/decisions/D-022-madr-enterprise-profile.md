---
id: D-022
status: implemented
date: 2026-07-27
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Repository architecture decision governance"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-022 -- Adopt MADR 4.0.0 with the Doka enterprise profile

## Context and Problem Statement

The repository decision records used an informal Markdown shape with repeated
metadata, inconsistent section depth, and no executable relationship or index
contract. Reviewers could inspect an individual decision, but automation could
not prove that identifiers, status history, sources, and cross-decision links
remained coherent across the complete corpus.

The project needs one recognizable ADR format whose enterprise extensions are
explicit, versioned, and enforced by the same local, CI, and release-candidate
gate.

## Decision Drivers

- Decision rationale, alternatives, and consequences must remain reviewable.
- Relationships, provenance, and indexes need deterministic validation.
- The governance path must add no third-party runtime dependency.

## Considered Options

- MADR 4.0.0 with the Doka enterprise profile
- Unextended MADR 4.0.0
- Keep the existing informal ADR format
- Adopt a third-party ADR management tool

## Decision Outcome

Chosen option: "MADR 4.0.0 with the Doka enterprise profile", because it preserves MADR familiarity while making every enterprise requirement executable.

### Consequences

- Good, because decision structure, provenance, relationships, and generated indexes fail deterministically on drift.
- Bad, because all existing and future ADRs must satisfy a stricter authoring contract.

### Confirmation

- Run `eng/validate-adrs.sh` and the ADR validator regression tests.
- Verify local build, CI, and release-candidate paths invoke the same validator.

## Pros and Cons of the Options

### MADR 4.0.0 with the Doka enterprise profile

- Good, because it combines a recognized lean format with enforceable enterprise evidence.
- Bad, because the stricter profile adds authoring and migration work beyond upstream MADR.

### Unextended MADR 4.0.0

- Good, because contributors can use the upstream template without repository-specific rules.
- Bad, because relationships, source dates, history, and deterministic indexes remain optional.

### Keep the existing informal ADR format

- Good, because no migration or new tooling is required.
- Bad, because metadata and evidence drift remain review-only concerns.

### Adopt a third-party ADR management tool

- Good, because mature tooling may provide richer querying and rendering.
- Bad, because the repository gains a dependency and external format lifecycle.

## More Information

The profile and validator implementation are part of the same cohesive
decision. The Doka profile intentionally differs from or extends upstream MADR
4.0.0 in these ways:

- The H1 uses an immutable `D-NNN` identifier and a repository-safe filename.
- YAML front matter has a fixed flat schema, controlled status vocabulary,
  ownership, scope, version pins, and bidirectional relationship fields.
- Every MADR core section is mandatory, including Confirmation and the full
  option trade-off section.
- Consequences and every option require symmetric good and bad evidence.
- Confirmation must be executable or reproducible rather than review-only.
- Re-evaluation triggers, dated status history, implementation references, and
  primary-source provenance are mandatory.
- Markdown and JSON indexes, including the relationship graph, are generated
  deterministically and checked for drift.

The complete normative contract is `docs/decisions/MADR-PROFILE.md`; upstream
MADR remains the structural basis rather than a locally forked replacement.

### Re-evaluation Triggers

- MADR publishes a new major version with a materially better metadata or relationship contract.
- The BCL-only parser cannot represent a required governance concept without ambiguity.
- Repository scale makes the generated index or graph unsuitable for review.

### Decision History

- 2026-07-27: Decision recorded with status implemented.
- 2026-07-27: MADR 4.0.0 and Doka enterprise profile 1.0 adopted.
- 2026-07-27: D-001 through D-021 migrated and deterministic validation enabled.

### Implementation References

- `docs/decisions/MADR-PROFILE.md`
- `docs/decisions/adr-template.md`
- `eng/Doka.EntityFrameworkCore.MySql.AdrValidator/`
- `eng/validate-adrs.sh`

### Sources

- [MADR 4.0.0 full template](https://github.com/adr/madr/blob/4.0.0/template/adr-template.md) (primary source; retrieved 2026-07-27)
- [MADR 4.0.0 changelog](https://github.com/adr/madr/blob/4.0.0/CHANGELOG.md) (primary source; retrieved 2026-07-27)
- [MADR status field decision](https://adr.github.io/madr/decisions/0008-add-status-field.html) (primary source; retrieved 2026-07-27)
- [MADR Confirmation heading decision](https://adr.github.io/madr/decisions/0018-use-confirmation-as-heading.html) (primary source; retrieved 2026-07-27)
