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

### Actions must be able to create the reviewed baseline proposal

`default_workflow_permissions` must remain `read`, while
`can_approve_pull_request_reviews` must be `true`.

GitHub couples pull-request creation and approving reviews in one repository
setting; it does not expose a create-only variant. The baseline-proposal job in
`benchmark.yml` needs that setting to open its single-file proposal. The job
receives explicit `pull-requests: write` and `contents: write` permissions only
on the trusted `main` workflow path.

Enabling the setting does not itself approve a pull request. The workflow has
no review command, refuses any proposal branch that changes more than the
canonical baseline, and never performs an approval or administrative bypass.
The `main` ruleset must still require an independent maintainer approval and
the protected checks. Those controls are the review boundary because GitHub's
combined setting cannot make creation available while technically forbidding
approval.

```bash
gh api --method PUT "repos/${repo}/actions/permissions/workflow" \
  --field default_workflow_permissions=read \
  --field can_approve_pull_request_reviews=true
```

UI: Settings -> Actions -> General -> Workflow permissions -> enable "Allow
GitHub Actions to create and approve pull requests".

### Baseline auto-merge must use the organization GitHub App

`GITHUB_TOKEN` remains responsible for the baseline branch, pull-request
maintenance, and the restricted CI dispatch. That prevents the automation
branch from starting an additional full pull-request workflow fan-out. It must
not register auto-merge: GitHub suppresses workflow runs for events caused by
that token, which can leave the merged `main` revision without commit-exact
release qualification.

The private GitHub App owned by `doka-labs` supplies the separate merge
identity. Its registration and installation must retain this contract:

- installation visibility: only the owning organization;
- repository access: only `Doka.EntityFrameworkCore.MySql`;
- repository permissions: `Contents: Read and write`, `Pull requests: Read and
  write`, and the implicit read-only metadata permission;
- organization and account permissions: none;
- webhooks and event subscriptions: disabled;
- ruleset bypass: none.

Organization Actions configuration exposes the App credentials only to this
repository:

- variable `DOKA_AUTOMATION_APP_CLIENT_ID`: the App Client ID;
- secret `DOKA_AUTOMATION_APP_PRIVATE_KEY`: the complete generated PEM private
  key;
- repository access for both values: `Doka.EntityFrameworkCore.MySql` only.

`benchmark.yml` uses the full-SHA pinned official
`actions/create-github-app-token` action. The cheap resolver first requests an
unused preflight token and lets the action revoke it before scorecard runners
are allocated. After a proposal update, a separate fresh token repeats the
repository and permission restrictions and is consumed only by
`gh pr merge --auto --squash`. It never reaches checkout, branch push,
pull-request creation, review, or the CI dispatch. The action revokes each
token when its job completes.

The workflow distinguishes GitHub's two Actions identities deliberately:
`github-actions[bot]` is the commit-bot name, while an existing auto-merge
request reports the Actor login `app/github-actions`. That legacy Actor is
rebound to the dedicated App, and the workflow reads GitHub's resulting Actor
back before it reports success.

Keep `DOKA_AUTOMATION_APP_PRIVATE_KEY` as an organization secret restricted to
this repository. Do not add a nominal environment only to satisfy static
auditing: without protection rules it creates no independent authorization
boundary, while a required-review environment would add a second manual gate
to every baseline-maintenance cycle. The independent authorization boundary
is the protected workflow change itself, followed by the repository-only App
installation and the job-level permission narrowing above. Reconsider an
environment boundary only if GitHub provides a non-interactive protection rule
that can authorize this exact maintenance job without weakening that model or
adding another operator handoff.

### The nuget environment must require a human reviewer

The `nuget` environment must carry both its `main`-only branch policy and a
required reviewer. The publish job in `nuget-publish.yml` requests the
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

ADR D-026 adds a stable aggregator for the commit-exact release inputs. The
individual specification and coverage jobs remain visible, but
`repository-qualification` is the fail-closed contract: it runs with
`always()`, inspects every dependency result, and fails when one was skipped or
failed. The three inexpensive checks remain individually required because the
restricted baseline-proposal profile deliberately runs only those checks.

Expected required checks on `main`:

- `repository-qualification`
- `quality-gates`
- `repo-tests`
- `integration-smoke`
- `dependency-review`

CodeQL remains enforced through the ruleset's `code_scanning` rule rather than
as another status-check context.

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
        {context: "repository-qualification", integration_id: 15368},
        {context: "quality-gates", integration_id: 15368},
        {context: "repo-tests", integration_id: 15368},
        {context: "integration-smoke", integration_id: 15368},
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
- GitHub, [Registering a GitHub App][github-app-registration],
  [Installing your own GitHub App][github-app-installation], and
  [Choosing permissions for a GitHub App][github-app-permissions]
  (retrieved 2026-08-14).
- GitHub, [`actions/create-github-app-token`][github-app-token-action],
  including explicit repository and permission scoping and automatic token
  revocation (retrieved 2026-08-14).
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
[github-app-registration]:
  https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app
[github-app-installation]:
  https://docs.github.com/en/apps/using-github-apps/installing-your-own-github-app
[github-app-permissions]:
  https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/choosing-permissions-for-a-github-app
[github-app-token-action]:
  https://github.com/actions/create-github-app-token
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
