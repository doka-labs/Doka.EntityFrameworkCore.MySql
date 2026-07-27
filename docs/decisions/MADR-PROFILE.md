# Doka MADR Enterprise Profile 1.0

## Purpose

The Doka profile adopts MADR 4.0.0 and adds the governance needed for an
enterprise provider: controlled metadata, explicit relationships, symmetric
trade-offs, executable confirmation, source provenance, decision history, and
deterministic indexes.

The upstream MADR structure remains recognizable. Doka additions are stricter
constraints, not replacements for the MADR sections.

## Normative Language

The terms MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY are normative.

## File and Identifier Contract

- Decision files MUST use `D-NNN-lowercase-version-safe-slug.md`; the slug
  permits lowercase letters, digits, dashes, and version dots.
- Metadata, filename, and H1 identifiers MUST match.
- Identifiers MUST be unique and contiguous from `D-001`.
- Decision content MUST be ASCII-only.
- A decision identifier is immutable after the file is merged.

## Metadata Contract

Every ADR MUST begin with these flat YAML keys in this exact order:

```yaml
---
id: D-NNN
status: proposed
date: YYYY-MM-DD
decision-makers: [Doka maintainers]
consulted: []
informed: [Provider contributors]
scope: "Bounded decision scope"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---
```

Nested YAML and undeclared keys are forbidden. Flat metadata keeps the
repository-owned parser deterministic and prevents tool-specific YAML
interpretation differences.

Front matter is the single source of truth for decision metadata. The body
MUST NOT repeat status, date, or scope as labeled metadata, and MUST NOT retain
an "original record metadata" migration block. Historical status changes
belong under Decision History; implementation detail belongs under More
Information or Implementation References.

Allowed statuses are:

- `proposed`: open for review and not authoritative.
- `accepted`: authoritative, with implementation or a trigger-driven
  confirmation path still pending.
- `implemented`: authoritative and confirmed by repository evidence.
- `rejected`: reviewed but never made authoritative.
- `deprecated`: still historically relevant but no longer recommended.
- `superseded`: replaced by another ADR and paired with `superseded-by`.

The normal status path is `proposed -> accepted -> implemented`; a proposal MAY
instead become `rejected`. A decision MAY move from `accepted` or `implemented`
to `deprecated` or `superseded`.
Historical states MUST be recorded under Decision History; metadata represents
the current state only.

## Section Contract

Every decision MUST contain these headings in this order:

1. `Context and Problem Statement`
2. `Decision Drivers`
3. `Considered Options`
4. `Decision Outcome`
5. `Consequences`
6. `Confirmation`
7. `Pros and Cons of the Options`
8. `More Information`
9. `Re-evaluation Triggers`
10. `Decision History`
11. `Implementation References`
12. `Sources`

The title MUST use `# D-NNN -- Short decision title`.

Every Markdown heading MUST be preceded by a blank line. This keeps the corpus
rendering stable across CommonMark-compatible tools and makes section
boundaries reviewable in plain-text diffs.

`Decision Outcome` MUST use:

```text
Chosen option: "Exact considered option", because ...
```

The chosen option MUST exactly match one item under Considered Options.

## Symmetric Trade-offs

Every considered option MUST have a same-named H3 section under Pros and Cons
of the Options. Each option MUST state at least one:

- `Good, because ...`
- `Bad, because ...`

Consequences MUST also state at least one good and one bad outcome. This
prevents a chosen option from receiving only benefits while rejected options
receive only costs.

## Confirmation Contract

Confirmation MUST identify a reproducible command, test, repository gate, or
inspection that can prove continued compliance. An accepted decision whose
implementation is trigger-driven MUST confirm both:

- the trigger has or has not fired; and
- the implementation gate runs when the trigger fires.

Review approval alone is not confirmation evidence.

## Relationship Contract

Relationships use ADR identifiers only:

- `supersedes` and `superseded-by`
- `amends` and `amended-by`

Every relationship MUST be bidirectional. A `superseded-by` relationship
requires status `superseded`. An amendment changes part of a still-valid
decision and does not change its status.

The validator derives `README.md` and `decision-index.json` from metadata.
Manual index or graph edits are rejected.

## Source Provenance

External URLs MUST appear only under Sources. Every external entry MUST use:

```text
- [Source title](https://authoritative.example/path) (primary source; retrieved YYYY-MM-DD)
```

Primary sources are vendor documentation, official specifications, official
source repositories or issue trackers, and first-party release or lifecycle
policies. Aggregators, blogs about another vendor, and agent memory do not
qualify for capability, version, security, or lifecycle claims.

The validator enforces source placement, the explicit primary-source
declaration, and a valid retrieval date. Reviewers remain responsible for
confirming that the cited publisher is authoritative for the claim; that
semantic judgment is not delegated to a domain allowlist.

Decisions based only on repository evidence MUST state:

```text
- No external sources; repository evidence only.
```

Retrieval dates record when the claim was verified, not when the ADR was first
authored.

## Decision History

History entries MUST use:

```text
- YYYY-MM-DD: Description of the decision or status change.
```

The first entry MUST use `Decision recorded with status <status>.`. A status
change MUST use `Status changed from <old> to <new>.`. The validator evaluates
the chain against the allowed transitions and the current metadata status.

Migration to this profile does not rewrite the date of the original decision.
The migration date is appended as a separate history entry.

## Tooling and Gates

- Validate: `eng/validate-adrs.sh`
- Regenerate index and graph: `eng/validate-adrs.sh --write-index`
- Regression tests:
  `dotnet test tests/Doka.EntityFrameworkCore.MySql.Tests/Doka.EntityFrameworkCore.MySql.Tests.csproj --filter FullyQualifiedName~AdrRepositoryValidatorTests`

The same validator MUST run from the local build, CI quality gate, and
release-candidate path.

## Upstream Basis

- MADR 4.0.0 full template:
  https://github.com/adr/madr/blob/4.0.0/template/adr-template.md
- MADR 4.0.0 changelog:
  https://github.com/adr/madr/blob/4.0.0/CHANGELOG.md
- MADR decision to use YAML front matter for status:
  https://adr.github.io/madr/decisions/0008-add-status-field.html
- MADR decision to use Confirmation:
  https://adr.github.io/madr/decisions/0018-use-confirmation-as-heading.html

All upstream sources were retrieved on 2026-07-27.
