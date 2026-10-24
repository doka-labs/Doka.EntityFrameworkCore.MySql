# Dependency Security Audits

Dependabot version-update PRs can target maintained branches, but its automatic
security-update PRs use only the repository's default branch. An older
maintenance dependency can therefore be vulnerable even while main is clean.
Version-update ignore rules and a later version's existence are not proof
that a maintained graph is safe.

## Active-line execution

The daily/manual workflow is
[`security-audit.yml`](../../.github/workflows/security-audit.yml). Its daily
schedule is `35 4 * * *` UTC. GitHub schedules are not an exact-time SLA.

- The native default-branch job audits main only when its current authority
  entry is `active` and its source commit matches the independently observed
  current main head. A queued run for an older source cannot stand in for the new line.
- The shared coordinator dispatches every other active exact source ref to
  that branch's own security workflow. It records the expected commit and
  returned child run ID, then checks the actual source, workflow, correlation
  and successful `dependency-vulnerability-audit` producer.
- Inactive and prepared lines receive no new scheduled
  audit dispatch. Explicit manual, nonpublishing audits remain possible on
  development branches without granting release authority.
- Missing branches or audit producers, skipped jobs, changed commits,
  cancellation and timeouts are visible failures. Successful siblings remain
  recorded even when another line fails. Main is not dispatched to itself.

The producer uses its own `global.json` SDK and checked-out engineering scripts;
it does not execute main's .NET/test-runner commands around a foreign checkout.
It needs no database, publisher environment, NuGet key or write permission.
Only the coordinator has the Actions permission required to dispatch children.
This scheduled/manual path adds no PR gate and does not repeat expensive
provider, benchmark or six-engine qualification.

Before activating a new line, include this workflow, its runner and regression
contracts on that branch. An old workflow without the security producer cannot
be treated as an audited active line.

## Scope and freshness

Run the same audit locally, without installing a tool:

```bash
bash eng/quality/audit-dependencies.sh
```

An optional `--output-dir <directory>` selects the evidence base. Each execution
creates a fresh child directory; it does not replace a previous report or
delete contributor caches.

The runner discovers source `.csproj` files under `src`, `tests`, `benchmarks`,
`eng/tools` and `examples`. It excludes generated `bin`, `obj` and `artifacts`
directories. Source-only `.csproj.template` fixtures are not executable project
graphs. Each project's evaluated direct and transitive NuGet dependencies are
checked, including test/build tooling and ordinary executable examples.

The runner uses an explicit NuGet.org package source and audit source, with a
new run-owned HTTP cache. Fresh restore uses `--force --no-http-cache`, enables
all dependency depths and severity levels, and makes audit warnings fatal.
Existing product lock files use locked mode; `--force-evaluate` is not combined
with it and the audit must not rewrite reviewed dependency locks. Package
content can be reused without treating old HTTP advisory responses as fresh.

The second check retains `dotnet package list --vulnerable --include-transitive`
JSON schema 1 and validates it independently of the command's exit status.
Sources and project identities must be present and correct; warning/error
`problems`, malformed evidence and any reported vulnerability fail the run.
A legitimate clean NuGet report may omit framework nodes. Failed restore or
advisory retrieval is an incomplete audit, never evidence of zero findings.

The scope is **known NuGet advisories for the evaluated project graphs**. This
is not a scanner for unknown vulnerabilities, every conditional platform/RID
graph, installed SDK/runtime vulnerabilities, standalone tool-manifest
packages, GitHub Action implementations or container operating-system images.
Those still require upstream advisory review, their existing update and
qualification policies, and applicable manual security response. The check
neither installs tools nor automatically changes dependency versions.

## Evidence and response

The job retains `artifacts/security-audit`, including project inventory,
SDK/source context, restore logs, raw audit reports and per-project/final
results. Failure diagnostics are uploaded with `always()` and the workflow
summary reports the result. The coordinator separately retains each child run
identity and conclusion. Artifact retention is 30 days; it is diagnostic
history, not standing publication authority or proof of future safety.

A failed check is visible in GitHub Actions. Personal notifications depend
on GitHub notification preferences; this implementation does not create
issues, send a separate notification or open repair PRs automatically.

For each finding:

1. Identify the affected branch, exact package/version, advisory and direct
   or transitive dependency path in that line's reports.
2. Distinguish a real advisory from unavailable data or incomplete restore.
   Restore source availability before claiming a clean result.
3. Review a compatible dependency update or necessary package-floor change
   on every affected maintained major. A clean main does not exempt an older
   line; conversely, a finding in one line does not prove all lines are affected.
4. Link reviewed backport/forward-port PRs and record non-applicability where
   appropriate. Do not silence the audit, add a blanket suppression or merge
   the platform upgrade merely to remove a maintenance failure.
5. Rerun the branch-owned audit and applicable provider/release qualification.
   A green dependency audit alone is not release or runtime compatibility evidence.

At end of support, deactivate schedules and dependency targets with the reviewed
[release-line lifecycle](../operations/release-lines.md). GitHub/NuGet grants
and branch creation/activation remain separately approved operational actions.

## Primary sources

Verified on 2026-10-22:

- [Dependabot target-branch and security-update limits](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference#target-branch)
- [NuGet auditing, sources and warnings](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
- [dotnet package list JSON and dependency-depth options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list)
- [dotnet restore freshness and locked-mode options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-restore)
- [NU1512: force-evaluate overrides locked mode](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1512)
