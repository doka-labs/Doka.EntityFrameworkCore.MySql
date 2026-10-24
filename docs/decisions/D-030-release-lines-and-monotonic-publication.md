---
id: D-030
status: implemented
date: 2026-10-22
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Exact framework release lines, current publication authority and monotonic Latest"
supersedes: []
superseded-by: []
amends: [D-013, D-026]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-030 -- Separate release-line qualification from current publication authority

## Context and Problem Statement

Doka's production 10.x line must remain maintainable after main adopts a new
.NET/EF Core major. An optional independently published platform RC also lets
consumers prepare before GA. Main-only source assumptions cannot support those
lines, and publishing an older stable line must not replace a newer GitHub
Latest release.

The owner requires the existing single-maintainer release process and
administrator bypass to remain unchanged. Release lines are not a mandate to
replace publisher environments, credentials or operator access.

## Decision Drivers

- Keep one release entry and one package/signature/recovery implementation.
- Preserve single-maintainer approval and administrator bypass.
- Qualify each line's own PR base, exact source and toolchain.
- Retain exact candidate bytes, three packages/symbols and personal signed tags.
- Separate frozen qualification from current permission to publish.
- Keep stable Latest monotonic across majors and out-of-order patches.
- Maintain active-line schedules, security audits and dependency coverage.
- Leave branch creation, activation and hosted mutations separately approved.

## Considered Options

- One shared pipeline with exact release-line policy and coordinated admission
- A separate copied publisher for each platform branch
- Main-only publication with local preview packages

## Decision Outcome

Chosen option: "One shared pipeline with exact release-line policy and coordinated admission",
because it extends the existing release process without duplicating its trust
and recovery rules or introducing another operational subsystem.

Canonical branches are `main`, `maintenance/10.x` and optional `preview/11.x`;
`x` is literal. `feature/dotnet-11` stays unchanged. The policy admits exact
refs, roles, package majors, channels and upstream stages. Its only lifecycle
states are `inactive`, `prepared` and `active`. Main/10 starts active; maintenance
and preview start inactive. The convention extends to later maintained majors.

Every authorized line uses `release-candidate.yml` and the existing `nuget`
environment. Select branch and canonical version; the suffix selects RC or
stable. Normal `qualification_only=false` qualifies once, then awaits the
personal signed tag and existing protected approval. Preparation sets it true
and requests no publisher environment, credentials, tag or publication writes.
Administrator bypass and the single-maintainer/self-review arrangement are not
changed or made additional eligibility conditions. No new environment or
credential generation is required by this feature.

Qualification freezes its exact source/ref/workflow, branch-owned SDK/EF graph,
test runner and observed policy revision/digest. Publication independently reads
validated current protected-main authority after approval and inside the shared
`doka-publication` exclusion. It checks the same run/candidate bytes and current
active line rather than accepting the candidate's policy as standing authority.
Unknown refs, wrong majors/channels, unprotected sources, changed bytes and
unavailable current authority fail. Prepared lines cannot publish.

The shared non-cancelling queue serializes publishers. It does not lock policy
merges or administrator actions. Complete an affected release before disabling
or reassigning its line. Ordinary failed-job recovery reuses the original run,
candidate and admission; it does not rebuild bytes under an existing tag.
There is no exceptional admission allowlist, framework-transition report,
publisher-migration state machine or automatic historical-run inventory.
This feature does not revoke old workflow generations; new validators cannot
change old-SHA code or override administrator access.

Latest compares canonical SemVer precedence inside the shared publisher. A
preview is never Latest; an older stable major or patch must not displace the
higher eligible stable release. Historical verification uses each release's
own signed evidence and does not require current publication rights or continued
Latest status. Existing legacy release schemas remain historical contracts.

Schedules run the default active line once and dispatch other active lines'
own workflows. Exact child commits, correlations and actual successful producer
jobs are verified. Daily branch-native dependency audits cover maintained
non-default lines independently of Dependabot's default-branch security PRs.
Inactive/prepared lines gain no scheduled work or automatic publication rights.

Optional public platform RCs keep the three existing package IDs and exact
approved upstream RC pins. The simple PublicRcConsumer fixture verifies exact
public NuGet content without a dependency on an unpublished SafeMigrations RC.
GA uses fresh GA source/dependencies and qualification, not relabelled RC bytes.
The [Release Lines runbook](../operations/release-lines.md) owns the practical
release, preparation, activation, retry, support and consumer procedures.

This amends D-013's earlier branch/multi-targeting proposal and D-026's main-only
assumptions while retaining PR merge-tree, signing, attestation, exact-byte
recovery and complete-package contracts. Hosted activation remains separate.

### Consequences

- Good, because ordinary releases keep the existing operator actions.
- Good, because the current `nuget` environment and administrator access remain.
- Good, because one implementation validates each branch's real graph.
- Good, because prepared qualification and exact public RC consumption exist
  without requiring preview adoption.
- Bad, because each maintained major still needs applicable reviewed ports,
  dependency servicing and its own qualification.
- Bad, because repository code cannot prove hosted protections or revoke old
  workflow code; those are not new promises of this feature.

### Confirmation

- Run the retained release-tool suites for branch/major/channel/toolchain,
  prepared isolation, current authority, exact-byte retry, signed publication,
  all package/symbol subjects, Latest and original historical schemas.
- Test single-maintainer environment data with administrator bypass true,
  false or absent, without changing the existing review/branch requirements.
- Test scheduler formatting variants and reject actual missing/skipped/failed
  producers or wrong child identities. Dispatch alone cannot satisfy a gate.
- Run public consumer offline contract tests and applicable repository/ADR tests.
- Before claiming operational activation or public consumption, observe the
  separately approved real hosted path or published RC. Local tests are not it.

## Pros and Cons of the Options

### One shared pipeline with exact release-line policy and coordinated admission

- Good, because all lines retain one trust/recovery implementation.
- Bad, because current eligibility and frozen evidence still need clear bounds.

### A separate copied publisher for each platform branch

- Good, because a branch could change its workflow independently.
- Bad, because authentication, signature, retry and Latest rules would drift.

### Main-only publication with local preview packages

- Good, because no independent source-line authorization is needed.
- Bad, because public platform RCs and maintained 10.x after main/11 are absent.

## More Information

The repository implementation and local tests are not evidence that inactive
branches, hosted grants or a public platform RC have been activated. Those
operations remain separately approved and observed.

### Re-evaluation Triggers

- GitHub changes queue, workflow, environment or retry semantics.
- NuGet changes Trusted Publishing identity or package publication contracts.
- Another framework major needs an explicitly adopted maintenance/preview line.
- Upstream servicing policy or the active line set changes.

### Decision History

- 2026-10-22: Decision recorded with status implemented.
- 2026-10-22: Exact-line capability keeps main/10 active and maintenance/preview
  operationally inactive.
- 2026-10-23: Earlier unreleased revisions explored additional transition and
  publisher-isolation machinery. They are historical design work, not current
  operator requirements.
- 2026-10-24: Owner approved the scope correction: retain practical release
  lines and existing single-maintainer/admin-bypass behavior; withdraw the new
  environment/credential migration, exceptional lifecycle and comprehensive
  transition-readiness subsystem. Ordinary release safeguards remain required.

### Implementation References

- `eng/release/lines.py`
- `eng/release/lines-policy.json`
- `eng/release/publication.py`
- `eng/release/schedules.py`
- `eng/release/consumer.py`
- `.github/workflows/release-candidate.yml`
- `eng/tests/test_release_line_consumer.py`
- `docs/operations/release-lines.md`

### Sources

- [GitHub concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)
  (primary source; retrieved 2026-10-22)
- [GitHub environment protection](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)
  (primary source; retrieved 2026-10-22)
- [GitHub reruns](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)
  (primary source; retrieved 2026-10-22)
- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
  (primary source; retrieved 2026-10-22)
- [NuGet exact versions](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning)
  (primary source; retrieved 2026-10-22)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
  (primary source; retrieved 2026-10-22)
