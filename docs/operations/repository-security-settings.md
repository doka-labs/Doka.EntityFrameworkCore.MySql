# Repository security settings

Part of this repository's security model lives in GitHub settings rather than
in version control. A reviewer cannot confirm those controls by reading the
tree, and a restored fork or a transferred repository does not inherit them.
This page records the expected state, why each control matters, and how to
verify it.

Settings marked **required** are load-bearing for a control that the repository
files assume. Settings marked **recommended** raise the posture further.

The commands fall into two groups with different token requirements.

**Repository settings -- every mutation on this page.** Scope and role are
separate: the scope authorizes the token, the role authorizes the account. The
account must hold the repository Admin role in every case.

Prefer a fine-grained personal access token limited to this repository, with
`Administration: read and write` for the mutations and `Administration: read`
for the verification commands. With a classic token, `public_repo` is
sufficient for this public repository; `repo` is only required if the same
token must also reach private repositories.

Do not reach for `security_events` as a read-only alternative. GitHub
documents it as granting "read and write access to security events in the code
scanning API", so it is neither read-only nor the documented scope for reading
this repository's settings.

**Organization policy -- read-only checks only.** This page configures nothing
at the organization level, but an organization policy can restrict the same
surface further, so two commands read it for diagnosis. Those need `admin:org`:

```bash
gh auth refresh -h github.com -s admin:org
```

Without that scope those two reads return `403`, which means "not verified",
never "not restricted". Every other command works without it.

## Known identifiers

These values are stable and are referenced by the commands below.

| Identifier | Value |
| --- | --- |
| Repository | `doka-labs/Doka.EntityFrameworkCore.MySql` |
| Branch ruleset on `main` | `16526347` |
| Tag ruleset for `v*` | `20342589` |
| Maintainer user id | `54367198` (`kdominic89`) |
| GitHub Actions integration id | `15368` (used in status-check contexts) |

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

The API form replaces the environment configuration, so the existing branch
policy has to be restated or the `main`-only restriction is lost:

`--field` types only `true`, `false`, `null`, and integers; `--raw-field`
always sends a string. Neither can express an array or an object, so a typed
body is built with `jq` and piped in through `--input`.

```bash
jq -n '{
  reviewers: [{type: "User", id: 54367198}],
  deployment_branch_policy: {
    protected_branches: false,
    custom_branch_policies: true
  }
}' | gh api --method PUT "repos/${repo}/environments/nuget" --input -
```

Verify both halves afterwards:

```bash
gh api "repos/${repo}/environments/nuget" --jq '.protection_rules[].type'
# expected: required_reviewers and branch_policy
gh api "repos/${repo}/environments/nuget/deployment-branch-policies" \
  --jq '.branch_policies[].name'
# expected: main
```

Resolve a different reviewer id with `gh api users/<login> --jq .id`.

### Every non-GitHub action must be on the allowlist

`allowed_actions` is `selected` with `github_owned_allowed: true`, so
`actions/*` and `github/*` resolve without an entry. Every other publisher
needs an explicit pattern before a workflow referencing it can run. The current
set is `NuGet/login@*` and `ossf/scorecard-action@*`.

Add the pattern before merging a workflow that introduces a new publisher, not
after the first red run:

```bash
jq -n '{
  github_owned_allowed: true,
  verified_allowed: false,
  patterns_allowed: ["NuGet/login@*", "ossf/scorecard-action@*"]
}' | gh api --method PUT \
  "repos/${repo}/actions/permissions/selected-actions" --input -
```

The `PUT` replaces the whole pattern list, so restate every entry that must
survive.

An organization-level policy can restrict the same surface further, and the
repository-level read above does not reveal it. Checking it needs the
`admin:org` scope:

```bash
gh api "orgs/doka-labs/actions/permissions"
gh api "orgs/doka-labs/actions/permissions/selected-actions"
```

Treat a `403` from those two calls as "not verified", not as "not restricted".

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

A check becomes selectable only after it has reported once, so this step
follows the first complete run rather than preceding it.

UI: Settings -> Rules -> `main` -> Require status checks to pass.

Editing the ruleset through the API replaces the whole rule array. The branch
ruleset carries six rule types -- `deletion`, `non_fast_forward`,
`required_linear_history`, `pull_request`, `required_status_checks`, and
`code_scanning` -- and a naive `PUT` of only the status checks removes the
other five, including the review requirement. Read, modify, and write back:

```bash
gh api "repos/${repo}/rulesets/16526347" > /tmp/ruleset.json

jq '{
  name, target, enforcement, conditions, bypass_actors,
  rules: (.rules | map(
    if .type == "required_status_checks" then
      .parameters.required_status_checks = [
        {context: "quality-gates", integration_id: 15368},
        {context: "repo-tests", integration_id: 15368},
        {context: "integration-smoke", integration_id: 15368},
        {context: "spec-test-suite (mysql84)", integration_id: 15368},
        {context: "spec-test-suite (mariadb114)", integration_id: 15368},
        {context: "spec-test-suite (mariadb118)", integration_id: 15368},
        {context: "coverage-gate", integration_id: 15368},
        {context: "dependency-review", integration_id: 15368}
      ]
    else . end))
}' /tmp/ruleset.json > /tmp/ruleset-update.json

jq '[.rules[].type]' /tmp/ruleset-update.json

gh api --method PUT "repos/${repo}/rulesets/16526347" --input /tmp/ruleset-update.json
```

The intermediate `jq '[.rules[].type]'` call is the safeguard: it must still
list all six types before the `PUT` runs.

Verify:

```bash
gh api "repos/${repo}/rulesets/16526347" --jq \
  '[.rules[].type], [.rules[]
   | select(.type=="required_status_checks")
   | .parameters.required_status_checks[].context]'
```

The specification job names its matrix legs explicitly, so the contexts read
`spec-test-suite (mysql84)` rather than the generated `spec-test-suite
(mysql84, mysql84)`.

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
jq -n '{
  security_and_analysis: {
    secret_scanning_non_provider_patterns: {status: "enabled"},
    secret_scanning_validity_checks: {status: "enabled"}
  }
}' | gh api --method PATCH "repos/${repo}" --input -
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
