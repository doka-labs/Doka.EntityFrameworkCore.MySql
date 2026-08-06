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

- `quality-gates`
- `repo-tests`
- `integration-smoke`
- CodeQL and every other code-scanning check required by the active `main`
  ruleset

Resolve every release blocker before continuing. Once the exact commit is
green, freeze `main` operationally until publication completes. Any later
commit makes the candidate stale, even if that commit changes only
documentation or automation.

#### 3. Create the release tag

Create one signed, annotated tag at `release_commit`. The package version,
dated changelog heading, tag, and tag message must identify the same version.
For example, after replacing the version with the next unused value:

```bash
release_version="10.0.0-rc.3"
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

#### 4. Produce the hosted candidate

1. Open GitHub Actions and select the `release-candidate` workflow.
2. Choose `Run workflow`, then select the exact value of `release_tag` in the
   branch/tag field.
3. Wait for the complete workflow DAG to succeed. A failed candidate has no
   publication authority. The DAG runs independent foundation and engine
   contracts in parallel, assembles exactly eleven required stage receipts,
   and grants OIDC and attestation permissions only to the final attestation
   job.
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

#### 5. Publish from trusted `main`

Keep `main` frozen. In GitHub Actions, manually run `nuget-publish` from
`main` with these exact inputs:

- `candidate_run_id`: the numeric ID of the successful candidate run
- `release_tag`: the exact `release_tag` selected for that run
- `confirmation`: `publish <release-tag>`

The workflow must run from `main`; selecting the release tag for this second
workflow is invalid. Approve the `nuget` environment deployment only after the
displayed candidate run ID and tag match the reviewed release.

#### 6. Verify public readback and finalize the release

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

#### 7. Recover without changing release identity

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
