# Release Lines

Doka uses one release process for independently maintained framework lines.
[D-030](../decisions/D-030-release-lines-and-monotonic-publication.md) records
the decision. The [workflow overview](workflow-overview.md) maps PR checks,
schedules and publication; [release governance](../release-governance.md)
retains the existing evidence, signing and three-package requirements.

## Current configuration

The reviewed authority is
[`eng/release/lines-policy.json`](../../eng/release/lines-policy.json):

| Exact branch | Role | Package major | Initial state |
| --- | --- | --- | --- |
| `main` | Main | 10 | `active` |
| `maintenance/10.x` | Maintenance | 10 | `inactive` |
| `preview/11.x` | Platform preview | 11 | `inactive` |

The `x` is literal, not a wildcard. `feature/dotnet-11` remains a development
branch; this implementation does not rename it or activate preview publication.
Every authorized line uses the existing `release-candidate.yml` workflow and
`nuget` environment. There is no new environment or credential migration.

The existing single-maintainer approval arrangement and administrator bypass
remain unchanged. Release-line tooling neither changes those settings nor
requires bypass to be disabled. Branch rules, exact deployment grants and any
later activation require separate approval; local tests do not prove hosted
configuration. This document creates no branches, grants, tags or packages.

## Normal release

The operator keeps the existing release actions:

1. Merge the release-preparation PR into its protected, authorized line.
2. Open `release-candidate.yml`, select that branch and enter its unused
   canonical version, for example `10.6.2` or `11.0.0-rc.1`.
3. Leave `qualification_only=false`. Review the qualified source SHA, version,
   tag and evidence in the run summary.
4. Create and push the personally signed annotated tag on that qualified SHA.
5. Approve the existing protected publisher. It rechecks the candidate and
   current line, publishes the coherent set, reads it back and decides Latest.

The version suffix selects RC or stable; there is no separate conflicting RC
flag. Qualification happens once for that candidate. No signing key enters
Actions, and no manual run/digest inventory or policy edit is needed for an
ordinary release. The [publication runbook](release-publication.md#qualification-and-publication-procedure)
contains the detailed commands.

Use `qualification_only=true` to prepare a protected line without publication.
It performs the complete applicable qualification but requests no publisher
environment, credentials or signed tag and makes no publication writes.

## Line eligibility and exact evidence

| State | Allowed behavior |
| --- | --- |
| `inactive` | Ordinary nonpublishing CI only; no release qualification or publication. |
| `prepared` | Full own-line qualification without publication. |
| `active` | Qualification and publication for the authorized major and channel. |

Unknown refs, wrong majors and wrong channels fail. Preview accepts prereleases
only. Main and maintenance use the approved GA toolchain. A future GA adoption
must not inherit RC dependencies or another major's patch policy. Each source
uses its own SDK, target framework, EF graph and test runner; qualification
requires its own successful PR base, tested merge tree and workflow identity.

Qualification freezes those identities and the observed policy revision/digest.
Publication independently reads current protected-main authority after approval
and inside the shared `doka-publication` exclusion. Candidate-local policy is
not a fallback when current authority is unavailable. An inactive or reassigned
line cannot publish merely because its candidate qualified earlier.

The common queue serializes publishers, not operator policy edits. Complete an
affected publication before changing its line's major or disabling that line.
The feature does not inventory arbitrary historical workflow runs, revoke old
workflow generations or manage a framework-transition state machine. New-code
checks cannot change code executed by old-SHA reruns or override administrator
access. Historical verification still uses the original signed tag, schema,
source ref and provenance, not today's main major; a complete older release
need not remain Latest. See [Verifying a Release](../security/release-verification.md).

## Prepare or activate a line

Line setup is a reviewed configuration change, not part of every release:

1. Prepare the branch with its own toolchain, line-aware workflows, required PR
   checks and branch protections. Use the binding exact name, not a wildcard.
2. Set its reviewed entry to `prepared` and merge a real preparation PR targeting
   that branch. Qualify its untagged merge commit with `qualification_only=true`;
   another line's receipt is invalid.
3. For publication, separately approve activation and the necessary exact branch
   grant in the existing `nuget` environment. Preserve reviewer, self-review and
   administrator-bypass settings. Verify the existing Trusted Publishing
   identity and package permissions; create no new credential generation.
4. Set the approved entry to `active`. Releases then use the normal flow above;
   schedules and dependency targets follow active entries. Preview need not be
   activated to maintain 10.x or adopt GA.

When main adopts 11.x, preserve qualified 10.x source on `maintenance/10.x` and
qualify each line against its own real PR/toolchain. Review the new GA dependency
and specification patch policy. Keep at most one active publication owner per
package major. The same convention supports maintenance/10.x, maintenance/11.x
and main/12. Real hosted dispatch/publication evidence remains separate from
local source and scripted-boundary verification.

## Failed publication and retry

Use **Re-run failed jobs** on the original run, retaining the same candidate
bytes and admission. The pipeline checks the existing identities; the operator
does not copy run IDs or hashes. Reconcile partial packages, symbols and release
assets before treating publication as complete.

**Re-run all jobs is not an exact-byte resume** if it rebuilds the candidate.
Changed bytes, foreign identities, conflicting public content, missing evidence
or expired indispensable artifacts do not permit a replacement admission under
an old tag. Retention and original-run retry availability still apply. Resolve
that release before disabling or reassigning its line; there is no special
allowlist mode. Never delete published packages, rewrite signed tags or replace
frozen evidence to make a retry succeed.

The pre-existing `eng/release/check-publication-readiness.sh` remains the
clean-build/finalizer gate used by ordinary qualification. It is unrelated to
the withdrawn framework-transition inventory/report subsystem.

## Stable status and Latest

A 10.x maintenance release remains stable after stable 11.x exists. Stability
does not automatically confer GitHub Latest. While 11.x is prerelease, stable
10.x remains eligible; preview is never Latest.

The common publisher compares canonical SemVer precedence across majors and
patches. If 11.0.2 completes before 11.0.1, the later-finishing 11.0.1 may publish
but must not replace 11.0.2 as Latest. The same applies to older-major releases.
Equal versions are idempotent only for the same verified tag, source and
content; contradictory or unavailable remote state fails.

## Public platform RC consumption

The optional platform branch, Doka RC version and exact upstream SDK/EF RC
graph are separate identities. Doka's RC number need not match Microsoft's.
Use existing package IDs and normal signed publication, not preview IDs, local
project references or private CI-artifact dependencies. Preview stays inactive
until adopted.

### Exact public RC consumer

After an approved RC is published and its evidence is verified:

```bash
python3 -m eng.release.consumer \
  --version '<Doka-RC-version>' \
  --sdk-version '<exact-upstream-SDK-RC-version>' \
  --ef-version '<exact-upstream-EF-RC-version>' \
  --target-framework net11.0 \
  --readback '<verified-public-NuGet-readback.json>' \
  --readback-sha256 '<independently-verified-receipt-SHA256>' \
  --output-dir '<empty-directory-outside-the-repository>'
```

The command installs no SDK/tool and publishes nothing. It materializes
[`PublicRcConsumer`](../../examples/PublicRcConsumer), selects the exact
installed SDK and package ranges, and performs isolated NuGet.org-only restore,
build and model/SQL/spatial/cache assertions. It rejects local-feed or project
substitutions and content differing from authenticated public readback. Hashing
an untrusted receipt does not authenticate it. The generated project opens in
Rider and does not replace live/runtime qualification. SafeMigrations owns its
own RC and can consume published Doka without creating a dependency cycle.

## Maintenance and security coverage

Schedules run the default active line once and dispatch every other active
line's own workflow/ref. Each child uses its SDK, EF graph and runner. The
coordinator checks actual source, correlation and successful declared producers,
not just successful dispatch. Ordinary manual feature CI remains nonpublishing.

Dependabot version-update targets follow active lines and their SDK/EF-major
limits. Non-default targets do not receive default-branch security-update PRs;
daily branch-native [dependency audits](../security/dependency-audits.md) cover
that gap. Missing producers, incomplete advisory data and known vulnerabilities
remain visible failures. Fixes need reviewed, line-specific ports and tests.

Doka plans 10.x servicing within Microsoft's .NET 10 support window through
**2028-11-14**, on serviced patches, as verified on 2026-10-22 and subject to
the best-effort [support policy](../../SUPPORT.md). Refresh upstream policy
before support/activation decisions; RC go-live is not Doka stable qualification.
At support end, complete affected publication, set the line inactive and remove
its scheduled/dependency/publication grants through separately approved changes
while preserving historical releases.

## Primary sources

Retained research from 2026-10-22; this scope correction changes no platform API:

- [GitHub workflow concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)
- [GitHub schedules and dispatch refs](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)
- [GitHub workflow reruns](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)
- [GitHub environment protection](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)
- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
- [NuGet exact version ranges](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning)
- [Dependabot target branches](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference#target-branch)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
