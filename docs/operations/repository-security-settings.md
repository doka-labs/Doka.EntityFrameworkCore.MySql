# Repository security settings

Part of this repository's security model lives in GitHub settings rather than
in version control. A reviewer cannot confirm those controls by reading the
tree, and a restored fork or a transferred repository does not inherit them.
This page records the expected state, why each control matters, and how to
verify it.

Settings marked **required** are load-bearing for a control that the repository
files assume. Settings marked **recommended** raise the posture further.

Every command on this page acts on repository settings, so a token with the
`repo` scope is sufficient and the account must hold the repository Admin role.
Scope and role are separate: the scope authorizes the token, the role
authorizes the account.

No command here needs `admin:org`. That scope covers organization-level policy,
which this page does not configure. Grant it only if you extend this page with
organization settings, and prefer a fine-grained token limited to this
repository over a classic token wherever your workflow allows it.

Reading the code-scanning configuration also works with the narrower
`security_events` scope if you want a token that cannot change settings.

## Verify the current state

```bash
repo=doka-labs/Doka.EntityFrameworkCore.MySql

gh api "repos/${repo}" --jq '.security_and_analysis'
gh api "repos/${repo}/actions/permissions"
gh api "repos/${repo}/actions/permissions/workflow"
gh api "repos/${repo}/actions/permissions/selected-actions"
gh api "repos/${repo}/rulesets" --jq '.[] | {id, name, target, enforcement}'
gh api "repos/${repo}/environments" --jq '.environments[] | {name, protection_rules}'
gh api "repos/${repo}/code-scanning/default-setup"
```

## Required

### Actions must not be able to approve pull requests

`can_approve_pull_request_reviews` must be `false`.

The baseline-proposal job in `benchmark.yml` holds `pull-requests: write` and
opens a pull request that it then enables auto-merge on. The merge is gated by
the branch ruleset's one-approval requirement. If workflows may submit
approving reviews, that requirement becomes reachable from inside the same
automation it is meant to gate. The current workflow does not attempt it; this
setting removes the possibility rather than relying on the code staying that
way.

```bash
gh api --method PUT "repos/${repo}/actions/permissions/workflow" \
  --field default_workflow_permissions=read \
  --field can_approve_pull_request_reviews=false
```

UI: Settings -> Actions -> General -> Workflow permissions -> clear "Allow
GitHub Actions to create and approve pull requests".

### The nuget environment must require a human reviewer

The `nuget` environment currently carries only a branch policy. The publish job
in `nuget-publish.yml` requests the short-lived NuGet trusted-publishing token
and pushes packages to nuget.org. Package publication is irreversible: a
version, once pushed, cannot be replaced.

Add at least one required reviewer so the credential boundary opens only after
a person confirms the run.

UI: Settings -> Environments -> `nuget` -> Deployment protection rules ->
enable "Required reviewers" and add the maintainers.

```bash
gh api --method PUT "repos/${repo}/environments/nuget" \
  --raw-field 'reviewers=[{"type":"User","id":<USER_ID>}]' \
  --field 'deployment_branch_policy=null'
```

Resolve `<USER_ID>` with `gh api users/<login> --jq .id`. Confirm afterwards
that `protection_rules` contains a `required_reviewers` entry.

### Newly added actions must enter the allowlist

`allowed_actions` is `selected` with `github_owned_allowed: true` and one
pattern, `NuGet/login@*`. `ossf/scorecard-action` is neither GitHub-owned nor
listed, so `scorecard.yml` cannot run until the pattern is added.

```bash
gh api --method PUT "repos/${repo}/actions/permissions/selected-actions" \
  --field github_owned_allowed=true \
  --field verified_allowed=false \
  --raw-field 'patterns_allowed=["NuGet/login@*","ossf/scorecard-action@*"]'
```

`actionlint` and `zizmor` deliberately do not appear here. They run as tools
inside `eng/quality/lint-workflows.sh` rather than as actions, so they add no
entry to the action allowlist and stay identical locally and in CI. When the
script hydrates them it verifies digests: actionlint against a checksum pinned
in the script, zizmor through `pip --require-hashes` against
`eng/quality/zizmor-requirements.txt`. A copy already present on `PATH` is used
as the contributor installed it and is not digest-checked.

### Required status checks must match the current lanes

ADR D-025 moved specification conformance and the coverage gate into the
per-event lane. Until the ruleset lists them, they run on every pull request
but do not block a merge.

Expected required checks on `main`:

- `quality-gates`
- `repo-tests`
- `integration-smoke`
- `spec-test-suite (mysql84)`
- `spec-test-suite (mariadb114)`
- `spec-test-suite (mariadb118)`
- `coverage-gate`
- `dependency-review`

UI: Settings -> Rules -> `main` -> Require status checks to pass. Add each
check after it has reported once on a pull request, otherwise the name cannot
be selected.

Keep `strict_required_status_checks_policy` enabled so a stale branch must
merge `main` before its checks count.

## Recommended

### Require code-owner review

`CODEOWNERS` is present and valid (`gh api "repos/${repo}/codeowners/errors"`
returns an empty error list), but the ruleset sets
`require_code_owner_review: false`, so the file has no effect on merges today.
Either enable the requirement or delete the file; a valid CODEOWNERS that
enforces nothing misleads a reviewer about who must approve.

UI: Settings -> Rules -> `main` -> Require a pull request before merging ->
enable "Require review from Code Owners".

### Extend secret scanning

`secret_scanning` and `secret_scanning_push_protection` are enabled.
`secret_scanning_non_provider_patterns` and `secret_scanning_validity_checks`
are disabled and are free for public repositories. The first detects
credentials that do not match a known provider format; the second reports
whether a detected token is still live, which changes how urgently a leak must
be rotated.

```bash
gh api --method PATCH "repos/${repo}" \
  --raw-field 'security_and_analysis={
    "secret_scanning_non_provider_patterns": {"status": "enabled"},
    "secret_scanning_validity_checks": {"status": "enabled"}
  }'
```

### Keep CodeQL on default setup

Code scanning runs through GitHub's default setup for `csharp`, `python`, and
`actions`, using `build-mode: none`. The `actions` language is what scans the
workflow files themselves.

Do not add a repository-local `codeql.yml`. Advanced setup replaces default
setup, and the replacement would have to reproduce the current language set,
schedule, and threat model before it matched what runs today. Revisit only if a
configuration is needed that default setup cannot express, which ADR D-025
records as a re-evaluation trigger.

## Fork pull requests

The repository is public, so pull requests arrive from forks. GitHub already
withholds secrets from fork-triggered runs and issues a read-only token, and
the workflows add `persist-credentials: false` so no checkout leaves a usable
credential in the job.

Confirm in Settings -> Actions -> General that "Require approval for all
external contributors" is selected under Fork pull request workflows, so a
first-time contributor's workflow run needs a maintainer click before it
executes.
