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
amends: [D-013, D-025, D-026]
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

### 2026-11-03 Amendment: One Complete Qualification Definition

PR CI and release candidates invoke the same read-only reusable
`.github/workflows/quality-gates.yml`. It owns quality and repository tests,
six-engine specifications, full integration and examples, migration deployment,
runtime trim/cache NativeAOT, qualified dependency matrices, exact packing of
all three packages and symbols, isolated real-package consumers with runtime
readback, SBOM and coverage. Normal PR green includes package consumers and
SBOM; it does not require release-only candidate attestation, tagging or
publication.

The CI caller retains the stable `repository-qualification` check and tested
merge-tree receipt. The release caller retains line/source preflight, invokes
the common qualifier once with explicit version and frozen authorization
artifact inputs, then assembles, attests and publishes the same-run bytes.
The qualifier has no publisher environment, publishing secrets, OIDC,
attestations or repository-write permissions. Existing Python release modules
retain authority, receipt and recovery validation; shared shell recipes avoid
parallel package, consumer, SBOM and engine implementations.

Same-run stage names, attempts, digests and checkpoints remain binding. Current
main/10 keeps its six release-stage receipts, separately from the complete
product job graph, and assembly does not repack. The existing `nuget`
environment, personal signed tags, single-maintainer approval, administrator
bypass, qualification-only preparation and admission recovery remain unchanged.
No hosted grant or publication is introduced by this amendment.

The branch owns its SDK, framework, test runner and applicable reviewed
dependency policy, including transfer to .NET 11. EF Core qualification records
the floor graph and exercises the highest exact admitted patch; scheduled
floating dependency probes remain independent. Benchmarks are independent
engineering diagnostics; hardware-dependent timing, CPU and working-set
thresholds are never qualification gates. This amendment records the repository
contract, not evidence that a hosted reusable dispatch has passed.

The package consumers use nonfloating candidate versions and isolated caches.
NuGet checks non-HTTP sources before HTTP sources; an available exact local
version therefore takes precedence over its public copy. This is source-type
precedence, not priority between two HTTP feeds. The consumers also compare
restored package hashes with the candidate bytes.

The branch's reviewed evidence policy declares applicable engine stages.
`qualify-engine.sh --list` supplies the workflow matrix; `--all-stages` adds
package and SBOM for the same candidate restoration and final receipt checks.
Declared implementations must match their native producer; missing or
inconsistent implementations fail rather than silently reduce the gate set.
Preview policy may omit the stable EF patch-range gate. Both PR and RC
engine execution retain bounded per-stage deadlines.

Native `global.json` test-runner selection remains branch-owned. Normal manual
CI uses the shared graph once; standalone drift jobs require schedule or
maintenance dispatch. These review corrections preserve the existing graph,
same-run receipts and publication controls. Local regression results alone do
not establish hosted workflow execution.

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
- Test actual first-admission retry after failed preparation, with separately
  visible admission-upload and publisher boundaries. Require the original
  archive once either boundary may have started; reject partial or duplicate
  histories and retain exact-byte/current-authority checks on restored admission.
- Test draft-only cleanup of planned empty `starter` upload residue after full
  inventory/payload validation, including ambiguous cleanup-response retry.
  Uploaded conflicts, immutable releases and missing admission history stay blocked.
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
- 2026-10-25: Separate publisher preparation from durable admission and public
  publication in the existing workflow. Preserve safe same-run retry after failed
  workflow preparation without replacing missing admission evidence.
- 2026-10-25: Reconcile documented empty GitHub `starter` upload residue only in
  a verified mutable draft; preserve completed assets and strict public readback.
- 2026-11-03: Unify complete PR and candidate qualification in one read-only
  reusable workflow, preserving release-only authority, signing, attestation,
  same-run artifacts, exact-byte recovery and existing operator actions.

### Implementation References

- `eng/release/lines.py`
- `eng/release/lines-policy.json`
- `eng/release/publication.py`
- `eng/release/schedules.py`
- `eng/release/consumer.py`
- `.github/workflows/release-candidate.yml`
- `.github/workflows/quality-gates.yml`
- `eng/testing/qualify-packages.sh`
- `eng/testing/generate-package-sbom.sh`
- `eng/testing/qualify-engine.sh`
- `eng/tests/test_release_line_consumer.py`
- `docs/operations/release-lines.md`

### Sources

- [GitHub reusable-workflow semantics](https://docs.github.com/en/actions/reference/workflows-and-actions/reusing-workflow-configurations)
  (primary source; retrieved 2026-11-03)
- [GitHub same-commit workflow references](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_iduses)
  (primary source; retrieved 2026-11-03)
- [Pinned artifact upload root selection](https://github.com/actions/upload-artifact/blob/cf430e030ddbb5b0abf93d22962f4752f3646cd9/src/shared/search.ts)
  (primary source; retrieved 2026-11-03)
- [NuGet local-source precedence](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)
  (primary source; retrieved 2026-11-03)
- [NuGet source-type order for PackageReference](https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior#nugetdefaultsconfig-settings)
  (primary source; retrieved 2026-11-03)
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
