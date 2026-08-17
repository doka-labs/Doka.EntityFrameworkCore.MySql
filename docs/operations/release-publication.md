# Release Publication Operations

This runbook defines the only supported path from a reviewed `main` commit to
NuGet.org and an immutable GitHub release. Release evidence requirements remain
authoritative in [Release Governance](../release-governance.md).

## One-time configuration

The GitHub environment is named `nuget`. It is restricted to deployments from
`main`, requires maintainer approval, and contains only `NUGET_USER`, the
NuGet.org user that owns the packages. Administrator bypass is disabled when a
second release maintainer is available.

Create one NuGet.org Trusted Publishing policy for this repository:

| Field | Value |
|---|---|
| Policy owner | `doka-labs` |
| Repository owner | `doka-labs` |
| Repository | `Doka.EntityFrameworkCore.MySql` |
| Workflow file | `release-candidate.yml` |
| Environment | `nuget` |

Do not store a long-lived NuGet API key. `NuGet/login` exchanges the protected
job's GitHub OIDC token for a short-lived key only after candidate, tag,
attestation, package, draft-release, and remote-state checks pass.

Before the first publication, verify these controls in the hosted repository:

- `release-candidate.yml` can create and verify artifact attestations;
- repository Settings -> General -> Releases has **Enable release
  immutability** selected; the setting applies only to releases published after
  it is enabled;
- the `nuget` environment accepts `main` and rejects other deployment refs;
- the environment review is requested only for the `publish` job; and
- the NuGet Trusted Publishing policy names the exact workflow and environment
  above.

Verify the repository-level release control with an administrator token before
starting a candidate. A successful response contains `"enabled": true`; GitHub
returns `404` when repository-level immutability is not enabled:

```bash
repo=doka-labs/Doka.EntityFrameworkCore.MySql
gh api \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2026-03-10" \
  "repos/${repo}/immutable-releases"
```

## Qualification and publication procedure

`release_version` never includes a leading `v`. `release_tag` is the same
version with a leading `v`. Select an unused version before starting.

### 1. Establish a release-ready main commit

Complete the dated `CHANGELOG.md` section, public API files, package metadata,
and release notes. Prerelease candidates keep new declarations in
`PublicAPI.Unshipped.txt`; the first stable release moves them to
`PublicAPI.Shipped.txt` in this reviewed preparation commit before candidate
dispatch. Merge through protected `main`, wait for `repository-qualification`
and required code scanning to pass, then update the local checkout:

```bash
git fetch origin main --tags
git switch main
git merge --ff-only origin/main
git status --short

release_commit="$(git rev-parse HEAD)"
test "${release_commit}" = "$(git rev-parse origin/main)"
```

`git status --short` must print nothing. Candidate dispatch deliberately
requires the exact current `main` commit. A later merge does not invalidate the
candidate as long as the qualified commit remains on protected `main` history;
the workflow still publishes only the recorded candidate bytes and tag.

Benchmarks are independent engineering evidence. They are not invoked or read
by release qualification and cannot block a release.

### 2. Verify the pre-tag branch trust root

Run the read-only lookup after the exact commit is green:

```bash
./eng/pre-tag-check.sh
```

It verifies the clean source, protected-main reachability, configured signing
material, and commit-exact `repository-qualification`. It creates no tag and
allocates no hosted runner.

### 3. Start one untagged candidate run

In GitHub Actions, run `release-candidate` with the ref selector set to `main`
and provide:

```text
version: <release_version>
```

The workflow refuses a side branch, stale `main`, malformed version, dirty
candidate identity, or any semantic version tag already pointing at the source
commit. It then performs the complete reversible qualification phase:

Assembly requires exactly six required stage receipts: package, SBOM, migration
deployment, runtime posture, EF Core patch matrix, and MySqlConnector patch
matrix.

- migration deployment;
- runtime posture;
- EF Core floor-graph and full latest-patch qualification, with an early exact
  specification-contract preflight;
- full MySqlConnector floor/latest patch matrices;
- package and symbol generation;
- locked dependency restore and SBOM generation;
- isolated consumer build against the exact local `.nupkg` files;
- basic and spatial runtime execution against the pinned MySQL 8.4 image;
- publication completeness rebuilt on the clean finalization runner against
  the exact EF Core and MySqlConnector patches selected by the matrices;
- canonical evidence assembly; and
- GitHub artifact attestations bound to `refs/heads/main` and the exact commit.

The runtime-posture stage requires clean release source before it allocates its
database, restores the host RID into an isolated artifacts and lock directory,
and rejects any source-tree change before writing evidence. Finalization then
validates that exact evidence instead of trusting the runtime job conclusion.
The same real Linux RID path runs inside the commit-exact `integration-smoke`
job, so pull requests and `main` exercise the failure boundary before
release-candidate execution without allocating another CI runner.

The final `publish` job waits for approval on the `nuget` environment. Do not
approve it yet. Review the completed qualification jobs, candidate summary,
qualification manifest, local-package receipts, and attestations. Record the
workflow run URL and confirm its SHA equals `release_commit`.

If a qualification job fails before a tag exists, rerun the failed jobs in the
same workflow run. A new dispatch creates a new candidate identity and repeats
the complete qualification.

### 4. Create the signed immutable identity

Only after all reversible qualification and attestation jobs are green, create
one signed, annotated tag on the exact candidate SHA:

```bash
release_version="<release_version>"
release_tag="v${release_version}"

test "$(git rev-parse HEAD)" = "${release_commit}"
test "$(git rev-parse origin/main)" = "${release_commit}"
git tag -s "${release_tag}" "${release_commit}" \
  -m "Doka.EntityFrameworkCore.MySql ${release_version}"
git tag -v "${release_tag}"
test "$(git rev-list -n 1 "${release_tag}")" = "${release_commit}"
git push origin "refs/tags/${release_tag}"
```

Push only that tag; never use `git push --tags`. The tag push does not start a
second workflow. Never move, replace, or reuse a published release tag.

### 5. Approve protected publication

Return to the waiting `publish` job. Confirm that the displayed run SHA,
version, expected tag, and environment all match the reviewed candidate, then
approve the `nuget` environment deployment.

The same workflow run now:

1. revalidates the exact qualified checkout, its continued reachability from
   current `main`, the candidate manifest, local package bytes, and same-run
   attestations;
2. verifies the signed annotated tag, registered signer, protected-main
   qualification, version, and commit by reading the exact check-run ID and
   workflow-run attempt frozen in the qualification manifest;
3. binds the qualification manifest, candidate receipt, and tag-trust receipt
   by SHA-256;
4. creates or resumes a matching GitHub release draft and reads every staged
   identity asset back before NuGet publication;
5. checks that every package and symbol is absent or byte-identical to the
   candidate;
6. requests the short-lived NuGet key;
7. publishes provider, provider symbols, spatial extension, and spatial
   symbols in dependency order;
8. immediately publishes and reads back the already complete GitHub draft as
   an immutable release; and
9. polls NuGet.org and the symbol server, compares public bytes, verifies NuGet
   repository signatures, and binds the results into workflow completion
   evidence.

The isolated consumer correctness test happens before the first NuGet push
against the exact package bytes that will be uploaded. Public readback after a
push measures availability, repository signing, and byte identity; it cannot
make an irreversible publication safe retroactively.

Draft reconciliation enumerates the authenticated, paginated release
inventory and matches the exact tag. GitHub's release-by-tag REST endpoint is
documented for published releases and therefore cannot discover a draft.
Draft creation and asset upload each use bounded readback polling before the
workflow crosses the NuGet publication boundary.

Post-publication observations are deliberately not GitHub release assets. They
can contain retry-specific timestamps and remote-state transitions. Keeping
them in the retained workflow artifact lets a failed completion probe be rerun
without attempting to alter an immutable release.

### 6. Complete the operator readback

Before considering the release complete, confirm:

- all workflow jobs are green;
- both primary and symbol packages have matching public readback;
- both public packages carry valid NuGet repository signatures;
- the GitHub release points at `release_tag` and is immutable;
- prereleases are marked prerelease and are not `latest`;
- stable releases are not prereleases and are `latest`; and
- `release-publication-<version>-attempt-<attempt>` contains candidate binding,
  tag trust, both preflights, public readback, signature verification, staged
  release plan, GitHub release readback, and publication completion receipt.

## Recovery contract

NuGet package versions are immutable and NuGet.org does not permanently delete
packages. Unlisting hides a package from search, but an exact version remains
downloadable. Recovery must therefore preserve identity rather than overwrite
it.

- Before any primary package push, a failed `publish` job may be rerun. The
  matching draft and staged assets are reconciled without replacement.
- After a partial NuGet write, rerun only the failed `publish` job from the same
  workflow run. The preflight accepts an existing primary package only when its
  canonical payload matches the sealed candidate. It publishes only a missing
  package or symbol.
- A later rerun of `repository-qualification` for the same commit does not
  replace candidate evidence. Publication reads the exact check-run and
  attempt recorded during candidate assembly and rejects any identity or
  response-digest difference.
- If the provider package exists and the spatial package does not, retry the
  same version so the missing spatial package can be added. Never repush or
  replace the provider package.
- Primary packages never use `--skip-duplicate`. Symbol uploads use it only for
  the symbol endpoint's documented pending/duplicate behavior.
- A conflicting same-version package, unexpected GitHub release asset, moved
  tag, changed notes, or candidate removed from current `main` history fails
  closed. Preserve all evidence and investigate; do not clobber remote state.
- Multiple drafts for one release tag also fail closed because tag-based asset
  operations would be ambiguous. Inspect their IDs and contents; remove only
  an unambiguously empty orphan under explicit operator control. The workflow
  never chooses or deletes a duplicate automatically.
- If publication succeeded but final workflow readback was interrupted, rerun
  the failed `publish` job. A matching immutable GitHub release is accepted
  only after complete metadata and asset digest verification.
- Do not rerun the entire workflow after the tag exists. Candidate preparation
  intentionally refuses tagged source. Use `Re-run failed jobs` so
  the same qualified candidate reaches the resumable publication job.
- Any source, package, configuration, dependency, or release-tool change
  requires a new reviewed `main` commit and a new unused release version.

Symbols can take time to become available. The workflow uses bounded polling
and retains a red completion state when availability or signature verification
does not complete. That state does not roll back already published packages;
it forces the same-identity recovery path above.

## Primary sources

- NuGet, [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing),
  retrieved 2026-08-16.
- NuGet, [Publish packages](https://learn.microsoft.com/nuget/nuget-org/publish-a-package),
  retrieved 2026-08-16.
- NuGet, [Deleting packages](https://learn.microsoft.com/nuget/nuget-org/policies/deleting-packages),
  retrieved 2026-08-16.
- NuGet, [Symbol packages](https://learn.microsoft.com/nuget/create-packages/symbol-packages-snupkg),
  retrieved 2026-08-16.
- NuGet, [Symbol package publish resource](https://learn.microsoft.com/nuget/api/symbol-package-publish-resource),
  retrieved 2026-08-16.
- NuGet, [`NuGet/login`](https://github.com/NuGet/login), retrieved 2026-08-16.
- GitHub, [Managing environments for deployment](https://docs.github.com/actions/deployment/targeting-different-environments/managing-environments-for-deployment),
  retrieved 2026-08-16.
- GitHub, [Artifact attestations](https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations),
  retrieved 2026-08-16.
- GitHub, [Immutable releases](https://docs.github.com/code-security/supply-chain-security/end-to-end-supply-chain/securing-builds#immutable-releases),
  retrieved 2026-08-16.
- GitHub, [Preventing changes to your releases](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/establish-provenance-and-integrity/prevent-release-changes),
  retrieved 2026-08-16.
- GitHub, [Re-running workflows and jobs](https://docs.github.com/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs),
  retrieved 2026-08-16.
- GitHub, [REST API endpoints for releases](https://docs.github.com/en/rest/releases/releases),
  retrieved 2026-08-17.
- GitHub, [Workflow-run attempt REST API](https://docs.github.com/en/rest/actions/workflow-runs#get-a-workflow-run-attempt),
  retrieved 2026-08-16.
- Git, [`git-tag`](https://git-scm.com/docs/git-tag), retrieved 2026-08-16.
