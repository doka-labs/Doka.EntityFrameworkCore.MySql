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
gh api \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2026-03-10" \
  "repos/${repo}/immutable-releases"
```

## Required

### Release immutability must be enabled before the first publication

Repository Settings -> General -> Releases must have **Enable release
immutability** selected. GitHub applies the control only to releases published
after it is enabled. Once published, an immutable release locks its tag and
assets; the release workflow therefore creates a draft, attaches and reads back
the complete stable asset set, and only then publishes it.

The verification request above returns `200` with `"enabled": true` when the
repository-level control is active and `404` when it is not. It requires
repository Administration read permission, which is deliberately unavailable
to `GITHUB_TOKEN`; verification is an administrator setup responsibility rather
than a workflow check that could never receive the required permission.

Primary source: [Preventing changes to your releases](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/establish-provenance-and-integrity/prevent-release-changes)
and [REST API endpoints for repositories](https://docs.github.com/en/rest/repos/repos?apiVersion=2026-03-10#check-if-immutable-releases-are-enabled-for-a-repository).

### Actions workflow permissions must remain read-only

`default_workflow_permissions` must remain `read`. No current workflow creates,
approves, or merges pull requests, so `can_approve_pull_request_reviews` is not
required by repository automation and should remain disabled.

Benchmark budgets are ordinary reviewed repository changes. The scheduled and
manual benchmark workflow has only `contents: read`; it uploads raw artifacts
and never opens or promotes a proposal.

### The nuget environment must require a human reviewer

The `nuget` environment must carry both its `main`-only branch policy and a
required reviewer. The publish job in `release-candidate.yml` requests the
short-lived NuGet trusted-publishing token and pushes packages to nuget.org.
Package publication is irreversible: a version, once pushed, cannot be
replaced.

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

### Automatic dependency submission must remain enabled

The pull-request dependency gate deliberately has no second graph producer.
GitHub Automatic Dependency Submission must therefore remain enabled for
NuGet. It resolves the graph for automation-created branches and `main`
commits. For trusted pull requests, `dependency-review.yml` first requires
successful `submit-nuget` checks from the `github-actions` App for both exact
SHAs. It then grants the dependency graph a separate 15-minute propagation
window in which the exact comparison must become warning-free. Disabling
autosubmission makes trusted pull requests fail closed in the producer phase.
Producer retries cap at ten seconds; propagation retries cap at 30 seconds.
That keeps the worst-case preflight at 72 REST requests while retaining prompt
producer detection. A normal ready run makes only the base check, head check,
and exact comparison requests.

UI: Settings -> Advanced Security -> Dependency graph -> Automatic dependency
submission -> Enabled.

Confirm that a recent branch revision and the current `main` revision each
show a successful `submit-nuget` job under "Automatic Dependency Submission
(NuGet)" in the Actions tab. The repository preflight binds the underlying
check name, exact SHA, and App slug rather than the presentation-level workflow
title. Its second phase then requires the warning-free exact comparison returned
by GitHub's dependency-review API.

GitHub does not document `submit-nuget` as a stable check name. If the producer
phase reports that the exact check is missing while Automatic Dependency
Submission is visibly green, inspect the exact revision through the Check Runs
API. Change the registered name only after confirming its SHA, App slug, and
successful dependency submission; update D-025 and the workflow contract tests
in the same pull request.

The successful job summary reports the base and head producer check IDs plus
the observed duration of each wait phase. If the producer phase fails, inspect
the exact SHA's Automatic Dependency Submission run. If the propagation phase
fails, a later re-run may confirm eventual recovery, but it does not by itself
justify changing the registered budget. Recalibration requires multiple
observed summaries and an update to D-025 in the same pull request.

### Required status checks must match the current lanes

The CI workflow exposes a stable aggregator for the exact pull-request head.
The individual specification and coverage jobs remain visible, but
`repository-qualification` is the fail-closed contract: it runs with
`always()`, inspects every dependency result, and fails when one was skipped or
failed. Release trust later accepts it only when that qualified Git tree equals
the merged `main` tree.

After the simplified workflow has reported hosted success, the intended
required checks on `main` are:

- `repository-qualification`
- `dependency-review`

CodeQL remains enforced through the ruleset's `code_scanning` rule rather than
as another status-check context.

A check becomes selectable only after it has reported once. Updating the
external ruleset therefore remains a separately approved operation after a
complete hosted run; repository changes alone do not authorize it.

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
        {context: "repository-qualification", integration_id: 15368},
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

## Primary Sources

Unless noted otherwise, sources were retrieved on 2026-08-10.

- GitHub,
  [Managing GitHub Actions settings for a repository][github-actions-settings],
  including the combined create-and-approve setting and fork-workflow approval.
- GitHub, [GITHUB_TOKEN][github-token],
  including permissions, lifetime, and workflow-trigger behavior.
- GitHub, [Rules available for rulesets][github-ruleset-reviews], including
  stale-review dismissal and approval of the most recent reviewable push
  (retrieved 2026-08-14).
- GitHub, [Deployments and environments][github-deployment-environments],
  including required-review behavior and environment-secret availability
  (retrieved 2026-08-14).
- GitHub, [Automatic dependency submission][github-automatic-submission],
  including the .NET detector and execution model (retrieved 2026-08-14).
- GitHub, [Dependency review][github-dependency-review], including the
  snapshot-warning header and exponential-backoff guidance (retrieved
  2026-08-14).
- GitHub, [REST API rate limits][github-rest-rate-limits], including the
  `GITHUB_TOKEN` limit of 1,000 requests per hour per repository (retrieved
  2026-08-15).

[github-actions-settings]:
  https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository
[github-token]:
  https://docs.github.com/en/actions/concepts/security/github_token
[github-ruleset-reviews]:
  https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets
[github-deployment-environments]:
  https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments
[github-automatic-submission]:
  https://docs.github.com/en/code-security/reference/supply-chain-security/automatic-dependency-submission
[github-dependency-review]:
  https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-review
[github-rest-rate-limits]:
  https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api
