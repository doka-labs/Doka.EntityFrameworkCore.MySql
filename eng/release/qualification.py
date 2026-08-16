#!/usr/bin/env python3
"""Select and freeze the evidence that qualifies one release tag.

Every gate a release depends on produces evidence for the candidate commit, so
selection is a question about identity rather than about policy: which single
result, among the runs and rerun attempts that exist for this commit, does the
canonical manifest pin.

The question is not trivial because reruns are legitimate. A workflow rerun
keeps its run identifier and increments its attempt, and a conditionally
skipped job reports success. A selector that ignored either would happily pin
a skipped job or an older attempt and call the release verified.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any, Sequence


POLICY_PATH = Path(__file__).resolve().parent / "evidence-policy.json"

MANIFEST_KIND = "release-qualification-manifest"
RECEIPT_KIND = "protected-check-receipt"
GATE_MANIFEST_KIND = "gate-evidence-manifest"


class QualificationError(RuntimeError):
    """Evidence cannot qualify a release."""


def load_policy(path: Path | None = None) -> dict[str, Any]:
    """Load and structurally validate the versioned evidence policy."""
    resolved = path or POLICY_PATH
    try:
        policy = json.loads(resolved.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise QualificationError(f"Evidence policy is unreadable: {error}") from error

    for field in ("schemaVersion", "policyVersion", "selectionRule", "gates",
                  "trustedTagSigners", "requiredProtectedChecks"):
        if field not in policy:
            raise QualificationError(f"Evidence policy field '{field}' is required.")
    if not isinstance(policy["gates"], list) or not policy["gates"]:
        raise QualificationError("Evidence policy declares no gates.")
    seen = set()
    for gate in policy["gates"]:
        for field in ("id", "kind", "producerWorkflow", "boundIdentities"):
            if field not in gate:
                raise QualificationError(
                    f"Evidence policy gate is missing '{field}'."
                )
        if gate["kind"] not in ("protected-check", "candidate-produced"):
            raise QualificationError(
                f"Evidence policy gate '{gate['id']}' has unknown kind "
                f"'{gate['kind']}'."
            )
        if gate["id"] in seen:
            raise QualificationError(
                f"Evidence policy declares gate '{gate['id']}' twice."
            )
        seen.add(gate["id"])

    return policy


def policy_digest(policy: dict[str, Any]) -> str:
    """Return a stable digest of the policy that produced a decision.

    The digest travels in every manifest so a later verification can prove the
    evidence was selected under the same rules it is being checked against.
    """
    canonical = json.dumps(policy, sort_keys=True, separators=(",", ":"))

    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def protected_check_receipt(
    response: dict[str, Any],
    *,
    gate: dict[str, Any],
    commit: str,
    repository: str,
    digest_source: str,
) -> dict[str, Any]:
    """Normalize an authenticated API response into immutable evidence.

    Repository qualification and code scanning run on GitHub rather than in
    this repository, so no repository-owned artifact exists to hash. Without
    this step the per-file digest contract would simply not apply to them, and
    the strongest checks in the release would be the least bound.
    """
    for field in ("id", "name", "conclusion", "head_sha"):
        if field not in response:
            raise QualificationError(
                f"Protected-check response for '{gate['id']}' lacks '{field}'."
            )

    expected_name = gate.get("checkName", gate["id"])
    if response["name"] != expected_name:
        raise QualificationError(
            f"Protected-check response is '{response['name']}', not "
            f"'{expected_name}'."
        )
    if response["head_sha"] != commit:
        raise QualificationError(
            f"Protected-check '{gate['id']}' describes commit "
            f"{response['head_sha']}, not the candidate {commit}."
        )
    if response["conclusion"] != "success":
        raise QualificationError(
            f"Protected-check '{gate['id']}' concluded "
            f"'{response['conclusion']}'."
        )

    required_event = gate.get("requiredEvent")
    if required_event and response.get("event") != required_event:
        raise QualificationError(
            f"Protected-check '{gate['id']}' originated from "
            f"'{response.get('event')}' rather than '{required_event}'. A "
            "pull-request result for the same commit is not branch evidence."
        )
    required_ref = gate.get("requiredRef")
    if required_ref and response.get("ref") != required_ref:
        raise QualificationError(
            f"Protected-check '{gate['id']}' originated from "
            f"'{response.get('ref')}' rather than '{required_ref}'."
        )

    return {
        "schemaVersion": 1,
        "kind": RECEIPT_KIND,
        "gate": gate["id"],
        "repository": repository,
        "commit": commit,
        "apiResourceId": response["id"],
        "responseDigest": hashlib.sha256(
            digest_source.encode("utf-8")
        ).hexdigest(),
        "workflowPath": gate["producerWorkflow"],
        "workflowRunId": response.get("workflow_run_id"),
        "runAttempt": response.get("run_attempt"),
        "event": response.get("event"),
        "conclusion": response["conclusion"],
    }


def eligible_results(
    results: Sequence[dict[str, Any]],
    *,
    gate: dict[str, Any],
    commit: str,
    repository: str,
    digest: str,
) -> list[dict[str, Any]]:
    """Filter the results that may represent this gate for this commit.

    Eligibility is deliberately exhaustive rather than convenient: a result
    that matches on four of five identities is not nearly eligible, it is a
    different measurement.
    """
    eligible = []
    for result in results:
        if result.get("gate") != gate["id"]:
            continue
        if result.get("commit") != commit:
            continue
        if result.get("repository") != repository:
            continue
        if result.get("workflowPath") != gate["producerWorkflow"]:
            continue
        if result.get("policyDigest") != digest:
            continue
        if result.get("conclusion") != "success":
            continue
        eligible.append(result)

    return eligible


def select_result(
    results: Sequence[dict[str, Any]],
    *,
    gate: dict[str, Any],
    commit: str,
    repository: str,
    digest: str,
    assembling_attempt: int | None = None,
) -> dict[str, Any]:
    """Select exactly one result per gate under the versioned ordering rule.

    A candidate-produced gate belongs to the run that is assembling the candidate, so
    its attempt may not be newer than the assembling attempt. Without that
    bound, a later rerun of one gate job could be pinned by a manifest that was
    already assembled, and the manifest would describe evidence that did not
    exist when it was written.
    """
    eligible = eligible_results(
        results, gate=gate, commit=commit, repository=repository, digest=digest
    )
    if gate["kind"] == "candidate-produced" and assembling_attempt is not None:
        eligible = [
            result
            for result in eligible
            if _ordering_value(result, "runAttempt", gate) <= assembling_attempt
        ]

    if not eligible:
        raise QualificationError(
            f"No eligible evidence for gate '{gate['id']}' at commit {commit}."
        )

    ordered = sorted(
        eligible,
        key=lambda result: (
            _ordering_value(result, "workflowRunId", gate),
            _ordering_value(result, "runAttempt", gate),
        ),
    )
    selected = ordered[-1]
    key = (
        _ordering_value(selected, "workflowRunId", gate),
        _ordering_value(selected, "runAttempt", gate),
    )
    tied = [
        result
        for result in ordered
        if (
            _ordering_value(result, "workflowRunId", gate),
            _ordering_value(result, "runAttempt", gate),
        )
        == key
    ]
    if len(tied) > 1:
        identities = {result.get("artifactId") for result in tied}
        raise QualificationError(
            f"Gate '{gate['id']}' has {len(tied)} results at run-and-attempt "
            f"{key} with artifact identities {sorted(map(str, identities))}; "
            "the selection would not be deterministic."
        )

    return selected


def _ordering_value(result: dict[str, Any], field: str, gate: dict[str, Any]) -> int:
    """Return one ordering component as an integer.

    Run identifiers and attempts are ordered numerically. A value that cannot
    be ordered is invalid evidence rather than a reason to fall back to string
    comparison, which would place attempt 10 before attempt 9.
    """
    value = result.get(field)
    if isinstance(value, bool) or not isinstance(value, (int, str)):
        raise QualificationError(
            f"Gate '{gate['id']}' result has unorderable {field} {value!r}."
        )
    try:
        return int(value)
    except (TypeError, ValueError) as error:
        raise QualificationError(
            f"Gate '{gate['id']}' result has unorderable {field} {value!r}."
        ) from error


def assemble_manifest(
    results: Sequence[dict[str, Any]],
    *,
    commit: str,
    tree_id: str,
    repository: str,
    expected_release_tag: str,
    release_version: str,
    assembling_attempt: int,
    policy: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Select every required gate once and freeze the chosen identities.

    Selection happens exactly here. Later verification re-checks the pinned
    identities and digests but never reselects, so a rerun that lands after
    assembly cannot silently change what the release was qualified on.
    """
    if expected_release_tag != f"v{release_version}":
        raise QualificationError(
            "Expected release tag does not match the candidate version."
        )

    resolved = policy or load_policy()
    digest = policy_digest(resolved)

    selections = []
    for gate in resolved["gates"]:
        selected = select_result(
            results,
            gate=gate,
            commit=commit,
            repository=repository,
            digest=digest,
            assembling_attempt=assembling_attempt,
        )
        entry = {"gate": gate["id"], "kind": gate["kind"]}
        for field in gate["boundIdentities"]:
            if field not in selected:
                raise QualificationError(
                    f"Gate '{gate['id']}' evidence lacks bound identity "
                    f"'{field}'."
                )
            entry[field] = selected[field]
        selections.append(entry)

    return {
        "schemaVersion": 2,
        "kind": MANIFEST_KIND,
        "policyVersion": resolved["policyVersion"],
        "policyDigest": digest,
        "selectionRuleVersion": resolved["selectionRule"]["version"],
        "repository": repository,
        "commit": commit,
        "treeId": tree_id,
        "expectedReleaseTag": expected_release_tag,
        "releaseVersion": release_version,
        "assemblingRunAttempt": assembling_attempt,
        "requiredProtectedChecks": list(resolved["requiredProtectedChecks"]),
        "gates": selections,
    }


def verify_manifest(
    manifest: dict[str, Any],
    *,
    policy: dict[str, Any] | None = None,
    repository: str | None = None,
    commit: str | None = None,
    tree_id: str | None = None,
    expected_release_tag: str | None = None,
) -> None:
    """Re-check a manifest without reselecting any evidence.

    Verification is exhaustive on purpose. A manifest is the single document a
    publication decision reads, so every property the assembly established has
    to be re-established here: a check that only ran at assembly time protects
    nothing once the manifest travels between jobs.
    """
    resolved = policy or load_policy()
    if manifest.get("kind") != MANIFEST_KIND:
        raise QualificationError("Manifest kind is not a release qualification.")
    if manifest.get("policyVersion") != resolved["policyVersion"]:
        raise QualificationError(
            f"Manifest declares policy version {manifest.get('policyVersion')!r}, "
            f"not {resolved['policyVersion']!r}."
        )
    if manifest.get("policyDigest") != policy_digest(resolved):
        raise QualificationError(
            "Manifest was assembled under a different evidence policy."
        )
    if manifest.get("selectionRuleVersion") != resolved["selectionRule"]["version"]:
        raise QualificationError(
            "Manifest was assembled under a different selection rule version."
        )

    if manifest.get("schemaVersion") != 2:
        raise QualificationError("Manifest schema version is not supported.")

    for field, expected_value in (
        ("repository", repository),
        ("commit", commit),
        ("treeId", tree_id),
        ("expectedReleaseTag", expected_release_tag),
    ):
        if not manifest.get(field):
            raise QualificationError(f"Manifest carries no {field}.")
        if expected_value is not None and manifest[field] != expected_value:
            raise QualificationError(
                f"Manifest {field} is {manifest[field]!r}, not {expected_value!r}."
            )

    if manifest.get("expectedReleaseTag") != f"v{manifest.get('releaseVersion', '')}":
        raise QualificationError(
            "Manifest expected release tag does not match its candidate version."
        )

    entries = manifest.get("gates")
    if not isinstance(entries, list) or not entries:
        raise QualificationError("Manifest pins no gates.")

    # A repeated gate is not a harmless duplicate: two entries for one gate can
    # name two different runs, and every consumer that reads the first would
    # disagree with one that reads the last.
    seen: set[str] = set()
    for entry in entries:
        identifier = entry.get("gate")
        if identifier in seen:
            raise QualificationError(f"Manifest pins gate '{identifier}' twice.")
        seen.add(identifier)

    declared = {gate["id"]: gate for gate in resolved["gates"]}
    missing = sorted(set(declared) - seen)
    if missing:
        raise QualificationError(
            f"Manifest is missing required gates: {', '.join(missing)}."
        )
    extra = sorted(seen - set(declared))
    if extra:
        raise QualificationError(
            f"Manifest pins gates the policy does not declare: {', '.join(extra)}."
        )

    for entry in entries:
        gate = declared[entry["gate"]]
        if entry.get("kind") != gate["kind"]:
            raise QualificationError(
                f"Manifest pins gate '{gate['id']}' as {entry.get('kind')!r} "
                f"rather than {gate['kind']!r}."
            )
        for field in gate["boundIdentities"]:
            if field not in entry:
                raise QualificationError(
                    f"Manifest gate '{gate['id']}' lacks bound identity "
                    f"'{field}'."
                )
            if entry[field] in (None, ""):
                raise QualificationError(
                    f"Manifest gate '{gate['id']}' has an empty '{field}'."
                )
        if commit is not None and entry.get("commit") != commit:
            raise QualificationError(
                f"Manifest gate '{gate['id']}' pins commit "
                f"{entry.get('commit')!r} rather than {commit!r}."
            )

    required = manifest.get("requiredProtectedChecks")
    if sorted(required or []) != sorted(resolved["requiredProtectedChecks"]):
        raise QualificationError(
            "Manifest records a different set of required protected checks than "
            "the policy declares."
        )
    protected = {
        gate["id"] for gate in resolved["gates"] if gate["kind"] == "protected-check"
    }
    unpinned = sorted(set(resolved["requiredProtectedChecks"]) - protected)
    if unpinned:
        raise QualificationError(
            f"Required protected check(s) have no gate: {', '.join(unpinned)}."
        )


def verify_file_digests(
    root: Path,
    inventory: Sequence[dict[str, str]],
) -> None:
    """Recompute every canonical digest and fail on any divergence.

    The download action reports a digest mismatch as a warning, which is not a
    release gate. Verification therefore happens here, and a missing, extra, or
    differing file all fail closed.
    """
    expected = {entry["path"]: entry["sha256"] for entry in inventory}
    observed = {}
    for path in sorted(root.rglob("*")):
        if path.is_file():
            relative = path.relative_to(root).as_posix()
            observed[relative] = hashlib.sha256(path.read_bytes()).hexdigest()

    missing = sorted(set(expected) - set(observed))
    if missing:
        raise QualificationError(
            f"Evidence is missing file(s): {', '.join(missing)}."
        )
    additional = sorted(set(observed) - set(expected))
    if additional:
        raise QualificationError(
            f"Evidence carries unrecorded file(s): {', '.join(additional)}."
        )
    differing = sorted(
        path for path, digest in expected.items() if observed[path] != digest
    )
    if differing:
        raise QualificationError(
            f"Evidence file digest mismatch: {', '.join(differing)}."
        )

def file_inventory(root: Path) -> list[dict[str, str]]:
    """Return the canonical digest of every file under the release root."""
    return [
        {
            "path": path.relative_to(root).as_posix(),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        }
        for path in sorted(root.rglob("*"))
        if path.is_file()
    ]


def _assemble(arguments: argparse.Namespace) -> dict[str, Any]:
    """Select every gate once and seal the result with its file inventory."""
    results: list[dict[str, Any]] = []
    for path in arguments.result:
        payload = json.loads(Path(path).read_text(encoding="utf-8"))
        results.extend(payload if isinstance(payload, list) else [payload])

    manifest = assemble_manifest(
        results,
        commit=arguments.commit,
        tree_id=arguments.tree_id,
        repository=arguments.repository,
        expected_release_tag=arguments.expected_release_tag,
        release_version=arguments.release_version,
        assembling_attempt=arguments.assembling_attempt,
        policy=load_policy(arguments.policy),
    )

    # The inventory covers the payload that is actually published, not the
    # whole candidate tree. Summaries, receipts, and the manifest itself are
    # written into that tree after assembly, so an inventory over it could
    # never be verified afterwards -- and the bytes worth binding are the
    # packages a consumer downloads.
    if arguments.root is not None:
        manifest["fileInventory"] = file_inventory(arguments.root)
        manifest["fileInventoryRoot"] = arguments.inventory_root_name

    # Assembly verifies its own output. A manifest that the verifier would
    # reject must never reach the artifact store, where the next job would
    # treat its existence as evidence.
    verify_manifest(
        manifest,
        policy=load_policy(arguments.policy),
        repository=arguments.repository,
        commit=arguments.commit,
        tree_id=arguments.tree_id,
        expected_release_tag=arguments.expected_release_tag,
    )

    return manifest


def main(argv: Sequence[str] | None = None) -> int:
    """Assemble or verify a canonical qualification manifest."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    assemble = subparsers.add_parser("assemble")
    assemble.add_argument("--result", required=True, action="append", default=[])
    assemble.add_argument("--commit", required=True)
    assemble.add_argument("--tree-id", required=True)
    assemble.add_argument("--repository", required=True)
    assemble.add_argument("--expected-release-tag", required=True)
    assemble.add_argument("--release-version", required=True)
    assemble.add_argument("--assembling-attempt", required=True, type=int)
    assemble.add_argument("--root", type=Path)
    assemble.add_argument("--inventory-root-name", default="packages")
    assemble.add_argument("--policy", type=Path)
    assemble.add_argument("--output", required=True, type=Path)

    verify = subparsers.add_parser("verify")
    verify.add_argument("--manifest", required=True, type=Path)
    verify.add_argument("--root", type=Path)
    verify.add_argument("--policy", type=Path)
    verify.add_argument("--expected-repository")
    verify.add_argument("--expected-commit")
    verify.add_argument("--expected-tree-id")
    verify.add_argument("--expected-release-tag")
    arguments = parser.parse_args(argv)

    try:
        if arguments.command == "assemble":
            manifest = _assemble(arguments)
            arguments.output.parent.mkdir(parents=True, exist_ok=True)
            arguments.output.write_text(
                json.dumps(manifest, indent=2, sort_keys=True) + "\n",
                encoding="utf-8",
            )
            print(
                f"Release qualification assembled: {arguments.output}",
                file=sys.stderr,
            )

            return 0

        manifest = json.loads(arguments.manifest.read_text(encoding="utf-8"))
        policy = load_policy(arguments.policy)
        verify_manifest(
            manifest,
            policy=policy,
            repository=arguments.expected_repository,
            commit=arguments.expected_commit,
            tree_id=arguments.expected_tree_id,
            expected_release_tag=arguments.expected_release_tag,
        )
        if arguments.root is not None:
            inventory = manifest.get("fileInventory")
            if not isinstance(inventory, list) or not inventory:
                raise QualificationError("Manifest carries no file inventory.")
            verify_file_digests(arguments.root, inventory)
    except (OSError, json.JSONDecodeError) as error:
        print(f"Qualification manifest is unreadable: {error}", file=sys.stderr)
        return 1
    except QualificationError as error:
        print(f"Release qualification failed: {error}", file=sys.stderr)
        return 1

    print(
        f"Release qualification verified: {arguments.manifest}", file=sys.stderr
    )

    return 0


if __name__ == "__main__":
    sys.exit(main())
