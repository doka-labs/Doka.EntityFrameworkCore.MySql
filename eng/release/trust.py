#!/usr/bin/env python3
"""Establish the trust root a release tag must satisfy before publication.

A tag is the immutable identity a published package is attributed to. Treating
it as trustworthy because it exists and is annotated leaves three questions
unanswered: who created it, whether the commit it names ever passed through the
protected branch, and whether the branch evidence for that commit came from the
branch at all rather than from a pull request against it.

The signed tag intentionally does not exist until reversible candidate
qualification has completed. The write-capable publication job repeats these
checks before it stages a release draft or requests a NuGet credential. The
``pre-tag`` command covers only the branch and signer prerequisites that can be
verified before hosted qualification begins.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from . import qualification as release_qualification
else:  # pragma: no cover - direct execution path
    import qualification as release_qualification


class TrustRootError(RuntimeError):
    """A release tag cannot be trusted."""


# `git verify-tag --raw` writes its human-readable verdict to stderr. The SSH
# and OpenPGP backends phrase it differently, so both shapes are recognized
# rather than assuming whichever backend this repository happens to use today.
SSH_GOOD_SIGNATURE = re.compile(
    r'Good "git" signature for (?P<principal>\S+) with '
    r"(?P<keytype>\S+) key (?P<fingerprint>SHA256:\S+)"
)
GPG_GOOD_SIGNATURE = re.compile(
    r"Good signature from .*?<(?P<principal>[^>]+)>"
)
GPG_FINGERPRINT = re.compile(
    r"(?:Primary key fingerprint|using \S+ key)[: ]\s*"
    r"(?P<fingerprint>[0-9A-Fa-f][0-9A-Fa-f ]{15,})"
)

ACCEPTED_API_REASONS = frozenset({"valid"})
GITHUB_API_VERSION = "2026-03-10"


def run_git(repo: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    """Run one git command against the repository without raising."""
    return subprocess.run(
        ("git", "-C", str(repo), *arguments),
        capture_output=True,
        text=True,
        check=False,
    )


def verify_api_signature(verification: dict[str, Any]) -> dict[str, Any]:
    """Require GitHub's own verdict on the tag signature.

    The API verdict alone is not the trust root: it says the signature is
    valid, not that this repository accepts the signer. It is required because
    it is the only view that also sees what the remote actually stores.
    """
    if not isinstance(verification, dict):
        raise TrustRootError("Tag verification object is missing.")
    if verification.get("verified") is not True:
        raise TrustRootError(
            f"GitHub reports the tag signature as unverified: "
            f"{verification.get('reason')!r}."
        )
    reason = verification.get("reason")
    if reason not in ACCEPTED_API_REASONS:
        raise TrustRootError(
            f"GitHub reports tag verification reason {reason!r} rather than "
            "'valid'."
        )

    return verification


def parse_local_verification(output: str) -> dict[str, str]:
    """Extract signer identity and key fingerprint from git's verdict."""
    match = SSH_GOOD_SIGNATURE.search(output)
    if match:
        return {
            "principal": match.group("principal"),
            "keyType": match.group("keytype").lower(),
            "fingerprint": match.group("fingerprint"),
        }

    match = GPG_GOOD_SIGNATURE.search(output)
    if match:
        fingerprint = GPG_FINGERPRINT.search(output)
        return {
            "principal": match.group("principal"),
            "keyType": "openpgp",
            "fingerprint": (
                fingerprint.group("fingerprint").replace(" ", "")
                if fingerprint
                else ""
            ),
        }

    raise TrustRootError(
        "Git did not report a good signature for the release tag."
    )


def match_trusted_signer(
    observed: dict[str, str],
    signers: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    """Require the observed signer to match one registered policy entry.

    Identity and key must match the same entry. Accepting a known principal
    signed by an unknown key, or a known key claiming an unregistered
    principal, would let either half of a compromised pair through.
    """
    if not signers:
        raise TrustRootError("The evidence policy registers no trusted signers.")

    for entry in signers:
        principals = entry.get("principals") or []
        if observed["principal"] not in principals:
            continue
        if observed["fingerprint"] != entry.get("fingerprint"):
            raise TrustRootError(
                f"Tag signer '{observed['principal']}' presented fingerprint "
                f"{observed['fingerprint']} rather than the registered "
                f"{entry.get('fingerprint')}."
            )
        expected_type = str(entry.get("keyType", "")).lower()
        if expected_type and observed["keyType"] not in (
            expected_type,
            expected_type.replace("ssh-", ""),
        ):
            raise TrustRootError(
                f"Tag signer '{observed['principal']}' used key type "
                f"'{observed['keyType']}' rather than '{expected_type}'."
            )
        return entry

    raise TrustRootError(
        f"Tag signer '{observed['principal']}' is not a registered trusted "
        "signer."
    )


def verify_local_signature(
    repo: Path,
    tag: str,
    *,
    allowed_signers: Path | None,
    signers: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    """Verify the tag locally and bind the signer to the policy.

    Local verification is independent of the API: it uses the repository's own
    allowed-signers material, so a remote that reported a valid signature for a
    key this project never accepted is still rejected here.
    """
    arguments = ["verify-tag", "--raw", tag]
    if allowed_signers is not None:
        if not allowed_signers.is_file():
            raise TrustRootError(
                f"Allowed-signers file '{allowed_signers}' is missing; local "
                "signature verification cannot run."
            )
        arguments = [
            "-c",
            f"gpg.ssh.allowedSignersFile={allowed_signers}",
            *arguments,
        ]

    result = run_git(repo, *arguments)
    if result.returncode != 0:
        raise TrustRootError(
            f"Local verification of tag '{tag}' failed: "
            f"{(result.stderr or result.stdout).strip()}"
        )

    observed = parse_local_verification(f"{result.stderr}\n{result.stdout}")
    matched = match_trusted_signer(observed, signers)

    return {"observed": observed, "signer": matched}


def verify_commit_is_on_protected_branch(
    repo: Path,
    commit: str,
    protected_ref: str,
) -> None:
    """Require the tagged commit to be reachable from the protected branch.

    A tag can be created on any commit, including one that never passed review.
    Reachability is what ties the immutable identity back to the branch whose
    protection produced the evidence.
    """
    result = run_git(repo, "merge-base", "--is-ancestor", commit, protected_ref)
    if result.returncode != 0:
        raise TrustRootError(
            f"Tagged commit {commit} is not reachable from {protected_ref}."
        )


def verify_branch_evidence_origin(
    receipt: dict[str, Any],
    *,
    commit: str | None = None,
    expected_branch: str = "main",
    expected_workflow: str | None = None,
) -> None:
    """Require repository qualification to originate from a branch push.

    A pull request against the protected branch produces a check for the same
    commit without that check ever having run on the branch itself, so the
    event alone decides nothing until it is bound to the branch, the commit,
    and the workflow file that is allowed to produce this evidence.
    """
    if receipt.get("conclusion") != "success":
        raise TrustRootError(
            f"Repository qualification concluded {receipt.get('conclusion')!r}."
        )
    if receipt.get("event") != "push":
        raise TrustRootError(
            "Repository qualification originated from "
            f"{receipt.get('event')!r} rather than a push on the protected "
            "branch."
        )
    if receipt.get("headBranch") != expected_branch:
        raise TrustRootError(
            f"Repository qualification ran on branch "
            f"{receipt.get('headBranch')!r} rather than {expected_branch!r}."
        )
    if commit is not None and receipt.get("commit") != commit:
        raise TrustRootError(
            f"Repository qualification describes commit "
            f"{receipt.get('commit')!r}, not the tagged {commit}."
        )
    if expected_workflow is not None and receipt.get("workflowPath") != expected_workflow:
        raise TrustRootError(
            f"Repository qualification came from workflow "
            f"{receipt.get('workflowPath')!r} rather than {expected_workflow!r}."
        )
    for field in ("workflowRunId", "runAttempt"):
        value = receipt.get(field)
        if isinstance(value, bool) or not isinstance(value, int):
            raise TrustRootError(
                f"Repository qualification carries no usable {field}."
            )


def verify_trust_root(
    *,
    repo: Path,
    tag: str,
    commit: str,
    api_verification: dict[str, Any],
    policy: dict[str, Any],
    qualification_receipt: dict[str, Any],
    repository: str = "",
    protected_ref: str = "refs/remotes/origin/main",
    allowed_signers: Path | None = None,
) -> dict[str, Any]:
    """Run every trust check in cost order and return the recorded evidence.

    The order is deliberate: each check is cheaper than the one after it, so
    the first failure costs the least possible work.
    """
    trusted = policy.get("trustedTagSigners")
    if not isinstance(trusted, dict):
        raise TrustRootError("The evidence policy declares no trusted signers.")

    if trusted.get("requireApiVerification", True):
        verify_api_signature(api_verification)

    local: dict[str, Any] = {}
    if trusted.get("requireLocalVerification", True):
        configured = trusted.get("allowedSignersFile")
        resolved = allowed_signers
        if resolved is None and configured:
            resolved = repo / configured
        local = verify_local_signature(
            repo,
            tag,
            allowed_signers=resolved,
            signers=trusted.get("signers") or [],
        )

    verify_commit_is_on_protected_branch(repo, commit, protected_ref)
    gate = next(
        (
            entry
            for entry in policy.get("gates", [])
            if entry.get("id") in policy.get("requiredProtectedChecks", [])
        ),
        {},
    )
    verify_branch_evidence_origin(
        qualification_receipt,
        commit=commit,
        expected_branch=str(gate.get("requiredRef", "refs/heads/main")).rsplit("/", 1)[-1],
        expected_workflow=gate.get("producerWorkflow"),
    )

    canonical_policy = json.dumps(policy, sort_keys=True, separators=(",", ":"))
    return {
        "schemaVersion": 2,
        "kind": "release-tag-trust-root",
        "repository": repository,
        "tag": tag,
        "commit": commit,
        "policyDigest": hashlib.sha256(canonical_policy.encode("utf-8")).hexdigest(),
        "protectedRef": protected_ref,
        "apiVerification": {
            "verified": api_verification.get("verified"),
            "reason": api_verification.get("reason"),
        },
        "localVerification": local.get("observed", {}),
        "signerFingerprint": local.get("signer", {}).get("fingerprint"),
        "qualification": qualification_receipt,
    }

def _gh_json(*arguments: str) -> dict[str, Any]:
    """Query the authenticated GitHub API and return one JSON object."""
    result = subprocess.run(
        (
            "gh",
            "api",
            "-H",
            "Accept: application/vnd.github+json",
            "-H",
            f"X-GitHub-Api-Version: {GITHUB_API_VERSION}",
            *arguments,
        ),
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise TrustRootError(
            f"GitHub API query failed: {(result.stderr or '').strip()}"
        )
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise TrustRootError("GitHub returned invalid JSON.") from error
    if not isinstance(payload, dict):
        raise TrustRootError("GitHub returned an unexpected API shape.")

    return payload


def fetch_tag_verification(repository: str, tag: str) -> dict[str, Any]:
    """Return the remote verification object for an annotated tag.

    The tag reference points at a tag object whose own resource carries the
    signature verdict; the reference alone does not.
    """
    reference = _gh_json(f"/repos/{repository}/git/ref/tags/{tag}")
    target = reference.get("object")
    if not isinstance(target, dict) or target.get("type") != "tag":
        raise TrustRootError(f"Release tag '{tag}' is not annotated on GitHub.")

    tag_object = _gh_json(f"/repos/{repository}/git/tags/{target['sha']}")

    return tag_object.get("verification") or {}


def select_check_run(payload: dict[str, Any], check_name: str, commit: str) -> dict[str, Any]:
    """Return the named check run, refusing an ambiguous or absent one.

    A commit can carry the same check name more than once -- a rerun of a
    different suite, or a workflow that was renamed into the same name. Picking
    the first would make the trust root depend on API ordering.
    """
    matches = [
        run for run in payload.get("check_runs", []) if run.get("name") == check_name
    ]
    if not matches:
        raise TrustRootError(f"Commit {commit} carries no '{check_name}' check run.")

    suites = {(run.get("check_suite") or {}).get("id") for run in matches}
    if len(suites) > 1:
        raise TrustRootError(
            f"Commit {commit} carries '{check_name}' in {len(suites)} check "
            "suites; the trust root would not be deterministic."
        )

    return matches[0]


def qualification_receipt(
    check_run: dict[str, Any],
    workflow_run: dict[str, Any],
) -> dict[str, Any]:
    """Normalize the check run and its workflow run into one receipt.

    The check run says a check with this name concluded. Only the workflow run
    says which workflow file produced it, on which branch, from which event,
    and at which attempt -- the facts that decide whether this is branch
    evidence at all.
    """
    for field in ("id", "run_attempt", "event", "head_branch", "head_sha", "path",
                  "conclusion"):
        if field not in workflow_run:
            raise TrustRootError(
                f"Workflow run for '{check_run.get('name')}' lacks '{field}'."
            )

    return {
        "name": check_run.get("name"),
        "id": check_run.get("id"),
        "checkSuiteId": (check_run.get("check_suite") or {}).get("id"),
        "conclusion": check_run.get("conclusion"),
        "workflowPath": workflow_run["path"],
        "workflowRunId": workflow_run["id"],
        "runAttempt": workflow_run["run_attempt"],
        "event": workflow_run["event"],
        "headBranch": workflow_run["head_branch"],
        "commit": workflow_run["head_sha"],
        "workflowConclusion": workflow_run["conclusion"],
    }


def select_workflow_run(
    payload: dict[str, Any],
    *,
    check_suite_id: Any,
    commit: str,
) -> dict[str, Any]:
    """Return the single workflow run that produced this check suite."""
    runs = payload.get("workflow_runs") or []
    if not runs:
        raise TrustRootError(
            f"Check suite {check_suite_id} for commit {commit} has no workflow run."
        )
    if len(runs) > 1:
        raise TrustRootError(
            f"Check suite {check_suite_id} maps to {len(runs)} workflow runs."
        )

    return runs[0]


def fetch_qualification_receipt(
    repository: str,
    commit: str,
    check_name: str,
) -> dict[str, Any]:
    """Resolve repository qualification down to the workflow run behind it.

    The check-runs resource alone cannot answer whether the check ran on the
    protected branch: it carries no workflow path, no run attempt, and its
    event field is not the event the workflow was triggered by. The check suite
    is the link to the workflow run that does carry all of them.
    """
    check_run = select_check_run(
        _gh_json(f"/repos/{repository}/commits/{commit}/check-runs"),
        check_name,
        commit,
    )
    suite_id = (check_run.get("check_suite") or {}).get("id")
    if suite_id is None:
        raise TrustRootError(
            f"Check run '{check_name}' is not attached to a check suite."
        )
    workflow_run = select_workflow_run(
        _gh_json(f"/repos/{repository}/actions/runs?check_suite_id={suite_id}"),
        check_suite_id=suite_id,
        commit=commit,
    )

    return qualification_receipt(check_run, workflow_run)


def _frozen_protected_gate(
    manifest: dict[str, Any],
    policy: dict[str, Any],
) -> tuple[dict[str, Any], dict[str, Any]]:
    """Return the one protected check this trust-root schema records.

    The receipt schema intentionally carries one `qualification` object. A
    future policy that requires more protected checks must therefore revise
    the schema instead of silently authenticating only the first one.
    """
    identifiers = policy.get("requiredProtectedChecks")
    if not isinstance(identifiers, list) or len(identifiers) != 1:
        raise TrustRootError(
            "The tag trust root requires exactly one protected check; revise "
            "its receipt schema before changing that policy cardinality."
        )
    identifier = identifiers[0]
    policy_gates = policy.get("gates")
    manifest_entries = manifest.get("gates")
    if not isinstance(policy_gates, list) or not isinstance(manifest_entries, list):
        raise TrustRootError("Qualification policy or manifest has no gate list.")
    gates = [gate for gate in policy_gates if gate.get("id") == identifier]
    entries = [entry for entry in manifest_entries if entry.get("gate") == identifier]
    if len(gates) != 1 or gates[0].get("kind") != "protected-check" or len(entries) != 1:
        raise TrustRootError(
            f"Qualification manifest does not pin protected check {identifier!r} exactly once."
        )

    return gates[0], entries[0]


def verify_frozen_qualification_receipt(
    receipt: dict[str, Any],
    *,
    manifest: dict[str, Any],
    policy: dict[str, Any],
    repository: str,
    commit: str,
    tree_id: str,
    expected_release_tag: str,
) -> dict[str, Any]:
    """Bind an API receipt to the exact check selected during qualification."""
    try:
        release_qualification.verify_manifest(
            manifest,
            policy=policy,
            repository=repository,
            commit=commit,
            tree_id=tree_id,
            expected_release_tag=expected_release_tag,
        )
    except release_qualification.QualificationError as error:
        raise TrustRootError(f"Qualification manifest is invalid: {error}") from error

    gate, entry = _frozen_protected_gate(manifest, policy)
    expected = {
        "apiResourceId": receipt.get("id"),
        "workflowPath": receipt.get("workflowPath"),
        "workflowRunId": receipt.get("workflowRunId"),
        "runAttempt": receipt.get("runAttempt"),
        "event": receipt.get("event"),
        "conclusion": receipt.get("conclusion"),
        "commit": receipt.get("commit"),
        "treeId": tree_id,
    }
    differing = sorted(
        field for field, value in expected.items() if entry.get(field) != value
    )
    canonical = json.dumps(receipt, sort_keys=True, separators=(",", ":"))
    observed_digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    if entry.get("responseDigest") != observed_digest:
        differing.append("responseDigest")
    if differing:
        raise TrustRootError(
            "Repository qualification differs from the frozen manifest: "
            f"{', '.join(sorted(set(differing)))}."
        )
    if receipt.get("name") != gate.get("checkName", gate["id"]):
        raise TrustRootError(
            f"Frozen qualification check is {receipt.get('name')!r}, not "
            f"{gate.get('checkName', gate['id'])!r}."
        )

    verify_branch_evidence_origin(
        receipt,
        commit=commit,
        expected_branch=str(gate.get("requiredRef", "refs/heads/main")).rsplit("/", 1)[-1],
        expected_workflow=gate.get("producerWorkflow"),
    )

    return entry


def fetch_frozen_qualification_receipt(
    repository: str,
    *,
    manifest: dict[str, Any],
    policy: dict[str, Any],
    commit: str,
    tree_id: str,
    expected_release_tag: str,
) -> dict[str, Any]:
    """Read back the exact check run and attempt frozen by qualification.

    A workflow rerun retains its run ID and increments its attempt. Reading the
    unqualified run endpoint would therefore let a later rerun replace the
    evidence selected by the candidate. The attempt endpoint preserves the
    original selection.
    """
    try:
        release_qualification.verify_manifest(
            manifest,
            policy=policy,
            repository=repository,
            commit=commit,
            tree_id=tree_id,
            expected_release_tag=expected_release_tag,
        )
    except release_qualification.QualificationError as error:
        raise TrustRootError(f"Qualification manifest is invalid: {error}") from error

    _, entry = _frozen_protected_gate(manifest, policy)
    check_id = entry.get("apiResourceId")
    workflow_run_id = entry.get("workflowRunId")
    run_attempt = entry.get("runAttempt")
    for field, value in (
        ("apiResourceId", check_id),
        ("workflowRunId", workflow_run_id),
        ("runAttempt", run_attempt),
    ):
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            raise TrustRootError(
                f"Frozen repository qualification carries no usable {field}."
            )

    check_run = _gh_json(f"/repos/{repository}/check-runs/{check_id}")
    workflow_run = _gh_json(
        f"/repos/{repository}/actions/runs/{workflow_run_id}/attempts/{run_attempt}"
    )
    if check_run.get("id") != check_id or workflow_run.get("id") != workflow_run_id:
        raise TrustRootError("GitHub returned a different frozen qualification identity.")
    details_url = str(check_run.get("details_url", ""))
    expected_details = f"/actions/runs/{workflow_run_id}/job/{check_id}"
    if expected_details not in details_url:
        raise TrustRootError(
            "Frozen check run does not link to the selected workflow run."
        )
    check_suite_id = (check_run.get("check_suite") or {}).get("id")
    if check_suite_id is None or workflow_run.get("check_suite_id") != check_suite_id:
        raise TrustRootError(
            "Frozen check run and workflow attempt do not belong to the same check suite."
        )

    receipt = qualification_receipt(check_run, workflow_run)
    verify_frozen_qualification_receipt(
        receipt,
        manifest=manifest,
        policy=policy,
        repository=repository,
        commit=commit,
        tree_id=tree_id,
        expected_release_tag=expected_release_tag,
    )

    return receipt


def local_signing_fingerprint(repo: Path) -> str:
    """Return the fingerprint and algorithm of the key that would sign.

    Checking only that some signing key is configured answers a weaker
    question than the one that matters: whether the key that would sign is the
    key the policy trusts. A wrong key produces a tag the trust root rejects
    after the tag exists, which is exactly when it cannot be taken back.
    """
    configured = run_git(repo, "config", "--get", "user.signingkey")
    if configured.returncode != 0 or not configured.stdout.strip():
        raise TrustRootError("No signing key is configured for this repository.")

    key = Path(configured.stdout.strip()).expanduser()
    if not key.is_file():
        raise TrustRootError(
            f"The configured signing key '{key}' is not a readable file; a "
            "fingerprint cannot be resolved from it."
        )

    result = subprocess.run(
        ("ssh-keygen", "-lf", str(key)),
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise TrustRootError(
            f"The configured signing key '{key}' has no resolvable fingerprint."
        )
    parts = result.stdout.split()
    if len(parts) < 2 or not parts[1].startswith("SHA256:"):
        raise TrustRootError("Unexpected fingerprint output for the signing key.")

    # The key type comes from the key itself rather than from the fingerprint
    # line, whose trailing "(ED25519)" is a display form and not the algorithm
    # name the policy registers.
    try:
        algorithm = key.read_text(encoding="utf-8").split()[0]
    except (OSError, IndexError) as error:
        raise TrustRootError(
            f"The configured signing key '{key}' names no algorithm."
        ) from error

    return {"fingerprint": parts[1], "keyType": algorithm}


def match_registered_key(
    observed: dict[str, str],
    signers: Sequence[dict[str, Any]],
) -> dict[str, Any]:
    """Match a local key against the registered signers by key, not by name.

    Before a tag exists there is no signature and therefore no principal to
    check. What can be answered is whether the key that would sign is one the
    policy trusts, which is the half that decides whether creating the tag is
    worth doing at all.
    """
    for entry in signers:
        if entry.get("fingerprint") != observed["fingerprint"]:
            continue
        expected = str(entry.get("keyType", "")).lower()
        if expected and observed["keyType"].lower() != expected:
            raise TrustRootError(
                f"The configured signing key is a {observed['keyType']} key "
                f"where the policy registers {expected}."
            )
        return entry

    raise TrustRootError(
        f"The configured signing key {observed['fingerprint']} is not a "
        "registered trusted signer."
    )


def pre_tag_report(
    *,
    repo: Path,
    repository: str,
    commit: str,
    policy: dict[str, Any],
) -> list[tuple[str, str, str]]:
    """Answer whether a commit has the branch and signer prerequisites.

    This shares the trust root's implementation rather than restating it. A
    second, weaker copy of the same decision is how a preparation check ends up
    accepting a commit whose later tag would in fact be rejected.
    """
    lines: list[tuple[str, str, str]] = []

    trusted = policy.get("trustedTagSigners") or {}
    try:
        observed = local_signing_fingerprint(repo)
        match_registered_key(observed, trusted.get("signers") or [])
        lines.append(
            ("OK", "signing key is a registered signer", observed["fingerprint"])
        )
    except TrustRootError as error:
        lines.append(("FAIL", "signing key is a registered signer", str(error)))

    gate = next(
        (
            entry
            for entry in policy.get("gates", [])
            if entry.get("id") in policy.get("requiredProtectedChecks", [])
        ),
        {},
    )
    check_name = gate.get("checkName", gate.get("id", "repository-qualification"))
    try:
        receipt = fetch_qualification_receipt(repository, commit, check_name)
        verify_branch_evidence_origin(
            receipt,
            commit=commit,
            expected_branch=str(
                gate.get("requiredRef", "refs/heads/main")
            ).rsplit("/", 1)[-1],
            expected_workflow=gate.get("producerWorkflow"),
        )
        lines.append(
            (
                "OK",
                check_name,
                f"run {receipt['workflowRunId']} attempt {receipt['runAttempt']}",
            )
        )
    except TrustRootError as error:
        lines.append(("FAIL", check_name, str(error)))

    return lines


def main(argv: Sequence[str] | None = None) -> int:
    """Establish the trust root for a release tag."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("--repo", required=True, type=Path)
    verify.add_argument("--tag", required=True)
    verify.add_argument("--commit", required=True)
    verify.add_argument("--repository", required=True)
    verify.add_argument("--policy", required=True, type=Path)
    verify.add_argument("--qualification-manifest", required=True, type=Path)
    verify.add_argument("--output", required=True, type=Path)
    verify.add_argument("--protected-ref", default="refs/remotes/origin/main")
    pre_tag = subparsers.add_parser("pre-tag")
    pre_tag.add_argument("--repo", required=True, type=Path)
    pre_tag.add_argument("--commit", required=True)
    pre_tag.add_argument("--repository", required=True)
    pre_tag.add_argument("--policy", required=True, type=Path)
    arguments = parser.parse_args(argv)

    if arguments.command == "pre-tag":
        policy = json.loads(arguments.policy.read_text(encoding="utf-8"))
        failures = 0
        # One record per line, field-separated. A caller can then tell a check
        # result from anything else the process happened to write -- a warning,
        # a traceback -- and a run that produced no records is distinguishable
        # from one whose records all passed.
        for state, subject, detail in pre_tag_report(
            repo=arguments.repo,
            repository=arguments.repository,
            commit=arguments.commit,
            policy=policy,
        ):
            print(f"PRE-TAG\t{state}\t{subject}\t{detail}")
            if state == "FAIL":
                failures += 1

        return 1 if failures else 0

    try:
        policy = json.loads(arguments.policy.read_text(encoding="utf-8"))
        manifest = json.loads(
            arguments.qualification_manifest.read_text(encoding="utf-8")
        )
        if not isinstance(manifest, dict):
            raise TrustRootError("Qualification manifest must contain a JSON object.")
        tree = run_git(arguments.repo, "rev-parse", f"{arguments.commit}^{{tree}}")
        if tree.returncode != 0 or not tree.stdout.strip():
            raise TrustRootError("Candidate tree identity cannot be resolved.")
        evidence = verify_trust_root(
            repo=arguments.repo,
            tag=arguments.tag,
            commit=arguments.commit,
            api_verification=fetch_tag_verification(
                arguments.repository, arguments.tag
            ),
            policy=policy,
            qualification_receipt=fetch_frozen_qualification_receipt(
                arguments.repository,
                manifest=manifest,
                policy=policy,
                commit=arguments.commit,
                tree_id=tree.stdout.strip(),
                expected_release_tag=arguments.tag,
            ),
            repository=arguments.repository,
            protected_ref=arguments.protected_ref,
        )
    except (OSError, json.JSONDecodeError, TrustRootError) as error:
        print(f"Release tag trust root failed: {error}", file=sys.stderr)
        return 1

    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(
        json.dumps(evidence, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Release tag trust root established: {arguments.output}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
