# Release Publication Operations

This runbook defines the controlled release-candidate and NuGet publication
procedure. Release policy and evidence requirements remain authoritative in
[Release Governance](../release-governance.md).

## NuGet and GitHub Release-Candidate Publication

Package publication is intentionally separate from qualification. Never upload
one of the locally generated packages manually: only the hosted candidate has
the workflow identity and attestations accepted by the publication boundary.

### One-time configuration

The repository environment is named `nuget`. It contains only the environment
secret `NUGET_USER`, whose value is the personal NuGet.org username that belongs
to the `doka-labs` organization. It is restricted to the `main` branch because
the publication workflow executes reviewed tooling from trusted `main`; the
selected candidate separately proves that its release tag identifies that same
commit.

Create one NuGet.org Trusted Publishing policy after
`.github/workflows/nuget-publish.yml` is present on `main`:

| Field | Value |
|---|---|
| Policy owner | `doka-labs` |
| Repository owner | `doka-labs` |
| Repository | `Doka.EntityFrameworkCore.MySql` |
| Workflow file | `nuget-publish.yml` |
| Environment | `nuget` |

Do not create or store a long-lived NuGet API key. `NuGet/login` exchanges the
workflow's GitHub OIDC token for a one-hour key only after candidate, manifest,
package, and remote-state verification have passed.

GitHub artifact attestations and environment reviewers require a public
repository on GitHub Free, Pro, or Team. A private repository needs GitHub
Enterprise Cloud for attestations; its plan must also expose the selected
environment protections. Before the first candidate, confirm that the hosted
`release-candidate` run can create and verify attestations. When required
reviewers are available, add the maintainer who authorizes publication and
disable administrator bypass. Do not disable self-review when that maintainer
is the repository's only release operator.

### Qualification and publication procedure

This is the canonical operator sequence for every prerelease and stable
publication. In the examples below, `release_version` has no leading `v`, while
`release_tag` is the corresponding Git tag. Always select the next unused
semantic version; do not copy the example version without checking the remote
repository and NuGet.org.

#### 1. Establish the release source

1. Complete the version, dated `CHANGELOG.md` section, public API, package
   metadata, and release-note changes before selecting the reviewed release
   commit.
2. Merge the release commit into protected `main`. Independent maintainer
   approval is the normal path. A documented bootstrap or emergency bypass is
   an exceptional recovery mechanism, not a routine substitute for review.
3. Update the local `main`, confirm that the worktree is clean, and record the
   exact source commit:

   ```bash
   git fetch origin main --tags
   git switch main
   git merge --ff-only origin/main
   git status --short

   release_commit="$(git rev-parse HEAD)"
   test "${release_commit}" = "$(git rev-parse origin/main)"
   ```

   `git status --short` must produce no output, and the final comparison must
   exit successfully.

#### 2. Qualify and freeze `main`

Wait for the following checks on `release_commit` to complete successfully:

- `repository-qualification`, the aggregator required by the `main` ruleset. It
  runs with `always()` and fails when any of its dependencies failed or was
  skipped, so a green aggregator is what a release tag later imports.
- CodeQL and every other code-scanning check required by the active `main`
  ruleset

The dedicated `benchmark` workflow keeps the hosted performance baseline on
the active evidence contract. Relevant changes on `main` and the monthly
schedule start it automatically. A manual dispatch is available when an
operator needs an immediate run. The workflow compares against an accepted
baseline when possible. When the current runner pair is absent or stale, it
validates both release engines and opens one baseline review pull request.
There is no artifact download, file replacement, or second benchmark dispatch.

Review and merge that pull request through protected `main`. The automation
never approves its own proposal. A private, repository-scoped GitHub App
schedules squash auto-merge, which remains blocked until a maintainer approves
the current revision and every protected check passes. Keeping the merge
request outside `GITHUB_TOKEN` ensures that the resulting `main` push starts
the workflows which produce commit-exact release evidence. The benchmark
controller explicitly dispatches the three required checks for the exact
proposal revision; no manual workflow approval, artifact download, Run-ID
handoff, or second benchmark dispatch is required. A current proposal is
reused; if it is behind only unrelated `main` changes, automation synchronizes
it without another
scorecard. Invalid or stale evidence is remeasured on the same stable proposal
branch. Unexpected files on that branch fail before the matrix and require
explicit review instead of being overwritten by automation.

The release-candidate workflow no longer consumes that baseline. It measures
performance itself, once, as a paired comparison: a reference and the candidate
provider revision are measured alternately on one allocated runner, so the
machine cancels out of every ratio instead of having to be matched. The
benchmark workflow above remains early warning on the default branch and never
qualifies or blocks a release. See
[Performance Evidence Operations](performance-evidence.md) for the reference
acceptance procedure.

Resolve every release blocker before continuing. Once the exact commit is
green, freeze `main` operationally until publication completes. Any later
commit makes the candidate stale, even if that commit changes only
documentation or automation.

#### 3. Check that a tag would qualify

A pushed tag is immutable, so the question worth answering first is whether a
tag on this commit would qualify at all. That is now a lookup rather than a
run: the tag imports the branch evidence this commit already carries, and it
runs only the gates whose evidence the tagged commit must produce for itself.

```bash
./eng/pre-tag-check.sh
```

The check allocates no runner, writes no file, and creates no tag. It reports
one line per precondition and exits non-zero when any fails:

| Precondition | Why a tag depends on it |
|---|---|
| Reachable from protected `main` | Ties the immutable identity back to the branch whose protection produced the evidence |
| Clean worktree | A tag would otherwise not describe what was tested |
| Trusted signers registered | An unregistered signature is rejected after the tag exists, which is when it is expensive |
| Allowed-signers file present | Local verification cannot run without the key material |
| Local signing key configured | The tag must be signable before it is created |
| `repository-qualification` succeeded | This is the branch evidence the tag imports |

A failing line names its remedy. This replaces the earlier local rehearsal,
which ran the whole candidate to buy certainty before spending a version: it
could not cover the gate that kept failing, and it cost more than the tag it
was meant to protect.

#### 4. Create the release tag

Create one signed, annotated tag at `release_commit`. The package version,
dated changelog heading, tag, and tag message must identify the same version.
Replace the placeholder with the next unused version before running any
command:

```bash
release_version="<next-unused-version>"
release_tag="v${release_version}"

git tag -s "${release_tag}" "${release_commit}" \
  -m "Doka.EntityFrameworkCore.MySql ${release_version}"
git tag -v "${release_tag}"
test "$(git rev-list -n 1 "${release_tag}")" = "${release_commit}"
git push origin "refs/tags/${release_tag}"
```

Verify the signature and target before pushing. Push only the intended tag;
never use `git push --tags` for a release. A tag is immutable release identity:
never move, replace, or reuse it after it reaches the remote repository.

#### 5. Produce the hosted candidate

1. Pushing the tag starts the `release-candidate` workflow automatically. No
   dispatch is required; a manual one remains available for diagnosis only.
2. Its first step is the trust root: the tag signature is verified against the
   remote verdict and, independently, against the registered signers, and the
   tagged commit must be reachable from protected `main`. This costs seconds
   and runs before any expensive job is allocated.
3. Wait for the complete workflow DAG to succeed. A failed candidate has no
   publication authority. The DAG assembles exactly six required stage
   receipts -- migration deployment, runtime posture, both patch matrices,
   package, and SBOM -- alongside one paired performance qualification, and
   grants OIDC and attestation permissions only to the final attestation job.
4. If one job fails transiently, use GitHub's `Re-run failed jobs` or rerun that
   specific job from the existing workflow run. The stable candidate identity
   remains the numeric workflow run ID; the new run attempt may reuse only
   checksum-verified stage artifacts from that same run ID and source commit.
   Do not start a new workflow dispatch merely to recover one failed stage.
5. Inspect the workflow summary, the exact stage-selection receipts, and
   retained evidence. Record the numeric run ID from the successful run URL:

   ```text
   https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/<candidate-run-id>
   ```

The hosted workflow binds the assembled result to the tagged source commit,
attests the packages and canonical manifest, verifies the attestation, and
uploads an attempt-qualified immutable candidate artifact. Its resolver rejects
artifacts from another run, a future attempt, an expired artifact, a digest
mismatch, an ambiguous stage, an unsafe ZIP entry, or an incomplete stage set.

Assembly is where selection happens, and it happens exactly once. Each gate is
first derived into a result that states which commit and tree it describes,
which workflow produced it, under which run and attempt, and which artifact
carries the bytes -- every field read back from a receipt, a resolved artifact
listing, or the API rather than declared. The manifest then selects one result
per gate and freezes those identities, together with the digest of every file
in the published payload:

```text
artifacts/release-candidate/<run-id>/release-qualification-manifest.json
```

Later steps re-check the frozen identities but never reselect, so a rerun that
lands after assembly cannot silently change what the release was qualified on.

#### 6. Publish from trusted `main`

Keep `main` frozen. In GitHub Actions, manually run `nuget-publish` from
`main` with these exact inputs:

- `candidate_run_id`: the numeric ID of the successful candidate run
- `release_tag`: the exact `release_tag` selected for that run
- `confirmation`: `publish <release-tag>`

Publication verifies the qualification manifest against the repository, commit,
and tag it expects, and against the packages themselves: every file digest in
the manifest is recomputed from the payload about to be published, and a
missing, added, or altered file fails closed.

The workflow must run from `main`; selecting the release tag for this second
workflow is invalid. Approve the `nuget` environment deployment only after the
displayed candidate run ID and tag match the reviewed release.

#### 7. Verify public readback and finalize the release

Wait for all four publication jobs to succeed:

1. `validate-candidate` verifies the exact candidate artifact, source identity,
   packages, attestations, and pre-publication remote state without requesting
   a NuGet credential.
2. `publish` repeats the remote-state preflight, enters the protected `nuget`
   environment, obtains the short-lived OIDC credential, and performs only the
   package writes still proven necessary.
3. `readback` has no OIDC or repository-write permission. It validates both
   packages and symbols, restores them into an empty isolated consumer, and
   executes the runtime contract against the supported MySQL image.
4. `finalize-github-release` receives the workflow's only `contents: write`
   permission after readback succeeds. It creates or resumes the matching
   draft, verifies every asset by readback, and publishes the immutable GitHub
   release.

Before unfreezing `main`, confirm all of the following:

- Both primary packages and both symbol packages passed public readback.
- The isolated basic and spatial consumer contracts passed.
- The GitHub release points to `release_tag` and contains the expected assets.
- A prerelease is marked as a prerelease and is not `latest`; a stable release
  is not marked as a prerelease and is `latest`.
- `nuget-validation-evidence-attempt-<attempt>`,
  `nuget-publish-evidence-attempt-<attempt>`, and
  `nuget-readback-evidence-attempt-<attempt>` are retained.
- `github-release-evidence-<release-tag>-attempt-<attempt>` is retained and
  contains the deterministic release plan and verified public release receipt.

#### 8. Recover without changing release identity

- If one candidate job fails because of transient hosted infrastructure and no
  candidate input must change, rerun the failed or specific job from the
  existing workflow run. GitHub retains the same run ID, source SHA, and source
  ref while incrementing the run attempt. Successful stages are reused only
  after exact artifact and receipt verification.
- A new manual dispatch creates a new candidate run ID. It cannot reuse stage
  artifacts from the earlier run and must qualify the complete candidate again.
- If any source, package, documentation, configuration, dependency, or release
  automation change is required, prepare a new release commit and version,
  repeat the green-`main` gate, and create a new signed tag. Do not repair the
  old candidate by moving its tag.
- If `main` advances before publication, discard the stale candidate and
  produce a new version and candidate from the new green `main` commit.
- If NuGet or GitHub finalization fails after a partial public write, preserve
  the workflow evidence and follow the conflict-safe retry procedures below.
  Do not publish local packages or alter remote assets to make the retry pass.

The workflow rejects a candidate from another repository, commit, tag,
workflow, or failed run. Candidate assembly accepts earlier attempts only from
the same run ID and only when every required stage receipt and artifact digest
matches. Publication also rejects a candidate once `main` has advanced. Produce
a new candidate version instead of publishing stale evidence.

### GitHub release finalization and recovery

The finalization job has the workflow's only `contents: write` permission. It
does not receive the NuGet OIDC permission. Before any release mutation, it
requires the local and remote tag to be annotated and to resolve to the exact
published source commit. It never creates, moves, or replaces a tag.

Release notes are the exact dated version section from `CHANGELOG.md`. Release
assets are the checksum-bound packages and symbols, candidate manifest and
checksum, candidate summary and reconciliation, resolved package inventory,
all SBOMs, and the five successful NuGet publication receipts. A prerelease
version is published as a GitHub prerelease and never becomes `latest`; a
stable version is not a prerelease and must become `latest`.

Retries are conflict safe. An absent release becomes a draft. A matching
partial draft receives only missing assets. An already published release is an
idempotent success only when its metadata, notes, asset names, sizes, payload
hashes, immutability state, and latest-release classification all match. Any
unexpected asset, changed payload, changed notes, moved or lightweight tag, or
other metadata conflict stops the job. The helper neither deletes assets nor
uses a clobber operation.

If finalization stops on a conflict, preserve the draft and both evidence
artifacts. Diagnose the conflicting remote state before making a manual
change. Rerun the same dispatch only after the remote draft matches the sealed
candidate; otherwise create a new release-candidate version.

### NuGet retry and partial-publication recovery

NuGet package versions are immutable and the two package pushes are not an
atomic transaction. If a network or symbol-server error interrupts the run,
dispatch the same publication request again. The preflight downloads any
existing primary package and compares a canonical content digest with the
candidate. A matching provider package allows the spatial step to resume; any
same-version payload conflict stops before a new key is requested.

Only symbol uploads use `--skip-duplicate`. The NuGet symbol endpoint documents
HTTP 409 while the same ID and version are still pending, and permits another
submission after publication. Treating that pending response as idempotent does
not weaken the immutable primary-package comparison.

Symbol validation and indexing are asynchronous. NuGet documents completion as
normally taking less than 15 minutes and directs publishers to investigate a
symbol package still pending after one hour. The workflow therefore polls for
at most one hour. It derives each public symbol URL and SHA-256 header from the
candidate DLL, then requires the downloaded Portable PDB to match the checksum
sealed into that assembly. A primary package becoming visible is not sufficient
publication evidence when its symbols remain unavailable.

Never use `--skip-duplicate` to bypass a primary-package conflict. If NuGet.org
contains different bytes, preserve the failed workflow evidence, stop the
release, and select a new prerelease version after root-cause review.

### Primary sources

- NuGet, [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
  retrieved 2026-08-03.
- NuGet, [Publish packages](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package),
  retrieved 2026-08-03.
- NuGet,
  [Symbol packages](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg),
  retrieved 2026-08-03.
- NuGet,
  [Symbol package publish resource](https://learn.microsoft.com/en-us/nuget/api/symbol-package-publish-resource),
  retrieved 2026-08-03.
- .NET,
  [SSQP key conventions](https://github.com/dotnet/symstore/blob/main/docs/specs/SSQP_Key_Conventions.md),
  retrieved 2026-08-03.
- GitHub,
  [Artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations),
  retrieved 2026-08-03.
- GitHub,
  [Re-running workflows and jobs](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs),
  retrieved 2026-08-04.
- GitHub,
  [Workflow artifacts](https://docs.github.com/en/actions/concepts/workflows-and-actions/workflow-artifacts),
  retrieved 2026-08-04.
- GitHub,
  [OpenID Connect reference](https://docs.github.com/en/actions/reference/security/oidc),
  retrieved 2026-08-04.
- GitHub,
  [OIDC security hardening](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-cloud-providers),
  retrieved 2026-08-04.
- GitHub,
  [Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments),
  retrieved 2026-08-03.
- GitHub,
  [Immutable releases](https://docs.github.com/en/repositories/releasing-projects-on-github/immutable-releases),
  retrieved 2026-08-04.
- GitHub CLI,
  [`gh release create`](https://cli.github.com/manual/gh_release_create),
  retrieved 2026-08-04.
