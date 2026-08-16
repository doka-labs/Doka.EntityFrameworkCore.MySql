#!/usr/bin/env python3
"""Derive one evidence result per gate from what the run actually produced.

The qualification manifest selects between results; it does not create them.
Something has to state, for each gate, which commit and tree it describes,
which workflow produced it, under which run and attempt, and which artifact
carries the bytes. That statement is this module.

Deriving it rather than declaring it is the point. Every field is read back
from a receipt, a resolved artifact listing, or the API, so a gate that did not
run cannot be described as one that did.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any, Sequence

if __package__:
    from .qualification import QualificationError, load_policy, policy_digest
    from .trust import (
        TrustRootError,
        fetch_qualification_receipt,
        run_git,
    )
else:  # pragma: no cover - direct execution path
    from qualification import QualificationError, load_policy, policy_digest
    from trust import TrustRootError, fetch_qualification_receipt, run_git


GATE_RESULT_KIND = "gate-evidence-result"


def digest_files(paths: Sequence[Path]) -> str:
    """Return one digest over an ordered set of files.

    A gate whose evidence is several files still needs a single identity. The
    path is folded in alongside the bytes so two files that swapped names do
    not produce the same digest.
    """
    if not paths:
        raise QualificationError("No evidence files to digest.")

    accumulator = hashlib.sha256()
    root = Path(*_common_parts(paths))
    for path in sorted(paths):
        # The path is folded in relative to the shared root, so two legs that
        # each wrote a `resolved-packages.json` stay distinguishable and the
        # digest does not depend on where the tree happens to be checked out.
        accumulator.update(path.relative_to(root).as_posix().encode("utf-8"))
        accumulator.update(b"\0")
        accumulator.update(path.read_bytes())

    return accumulator.hexdigest()


def _common_parts(paths: Sequence[Path]) -> tuple[str, ...]:
    """Return the longest directory prefix every path shares."""
    parts = [path.parent.parts for path in paths]
    shared: list[str] = []
    for position in range(min(len(entry) for entry in parts)):
        candidates = {entry[position] for entry in parts}
        if len(candidates) != 1:
            break
        shared.append(candidates.pop())

    return tuple(shared)


def tree_id(repository: Path, commit: str) -> str:
    """Return the tree the commit points at.

    Two commits can carry identical trees, and a rebase produces a new commit
    for an unchanged tree. Recording both means evidence can be tied to the
    content as well as to the history that produced it.
    """
    result = run_git(repository, "rev-parse", f"{commit}^{{tree}}")
    if result.returncode != 0:
        raise QualificationError(f"Commit {commit} has no resolvable tree.")

    return result.stdout.strip()


def selected_artifact(selection: dict[str, Any], stage: str) -> dict[str, Any]:
    """Return the resolved artifact for one stage.

    The resolver already refused an ambiguous stage, so anything other than
    exactly one entry here means the selection document was edited or truncated
    between the restore and this derivation.
    """
    matches = [
        artifact
        for artifact in selection.get("artifacts", [])
        if artifact.get("stage") == stage
    ]
    if len(matches) != 1:
        raise QualificationError(
            f"Artifact selection carries {len(matches)} entries for stage "
            f"'{stage}'; exactly one is required."
        )

    return matches[0]


def candidate_produced_result(
    *,
    gate: dict[str, Any],
    checkpoint: dict[str, Any],
    artifact: dict[str, Any],
    selection: dict[str, Any],
    repository: str,
    commit: str,
    tree: str,
    digest: str,
    extra: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Describe one gate the candidate run produced evidence for."""
    if checkpoint.get("sourceCommit") != commit:
        raise QualificationError(
            f"Stage receipt for '{gate['id']}' describes commit "
            f"{checkpoint.get('sourceCommit')}, not the candidate {commit}."
        )

    result = {
        "schemaVersion": 1,
        "kind": GATE_RESULT_KIND,
        "gate": gate["id"],
        "repository": repository,
        "commit": commit,
        "treeId": tree,
        "sourceHash": digest,
        "workflowPath": gate["producerWorkflow"],
        "workflowRunId": selection["workflowRunId"],
        "runAttempt": artifact["attempt"],
        "conclusion": "success",
        "artifactId": artifact["id"],
        "artifactDigest": artifact["sha256"],
        "policyDigest": None,
    }
    result.update(extra or {})

    return result


def protected_check_result(
    *,
    gate: dict[str, Any],
    receipt: dict[str, Any],
    repository: str,
    commit: str,
    tree: str,
) -> dict[str, Any]:
    """Describe one gate whose evidence lives on the forge rather than here.

    Repository qualification runs on GitHub, so there is no repository-owned
    artifact to hash. The response the API returned is digested instead, which
    is what makes the per-file digest contract apply to it at all.
    """
    if receipt.get("commit") != commit:
        raise QualificationError(
            f"Protected check for '{gate['id']}' describes commit "
            f"{receipt.get('commit')}, not the candidate {commit}."
        )
    if receipt.get("conclusion") != "success":
        raise QualificationError(
            f"Protected check '{gate['id']}' concluded "
            f"{receipt.get('conclusion')!r}."
        )

    canonical = json.dumps(receipt, sort_keys=True, separators=(",", ":"))

    return {
        "schemaVersion": 1,
        "kind": GATE_RESULT_KIND,
        "gate": gate["id"],
        "repository": repository,
        "commit": commit,
        "treeId": tree,
        "workflowPath": gate["producerWorkflow"],
        "workflowRunId": receipt["workflowRunId"],
        "runAttempt": receipt["runAttempt"],
        "event": receipt["event"],
        "conclusion": receipt["conclusion"],
        "apiResourceId": receipt["id"],
        "responseDigest": hashlib.sha256(canonical.encode("utf-8")).hexdigest(),
        "policyDigest": None,
    }


STAGE_BY_GATE = {
    "migration-deployment": "migration-deployment",
    "runtime-posture": "runtime",
    "efcore-patch-matrix": "efcore-patch-matrix",
    "mysqlconnector-patch-matrix": "mysqlconnector-patch-matrix",
}

# The gates whose evidence resolves a floating dependency leg. Their identity
# has to include the graph that was actually resolved, or a rerun on a day the
# upstream published a new patch would be represented by an older result.
DEPENDENCY_SNAPSHOT_GATES = frozenset(
    {"efcore-patch-matrix", "mysqlconnector-patch-matrix"}
)


def derive(arguments: argparse.Namespace) -> list[dict[str, Any]]:
    """Derive every gate result the policy declares for this run."""
    policy = load_policy(arguments.policy)
    digest = policy_digest(policy)
    repository_path = Path(arguments.repo)
    commit = arguments.commit
    tree = tree_id(repository_path, commit)
    selection = json.loads(Path(arguments.selection).read_text(encoding="utf-8"))
    checkpoint_directory = Path(arguments.checkpoint_directory)
    evidence_root = Path(arguments.evidence_root)

    results: list[dict[str, Any]] = []
    for gate in policy["gates"]:
        identifier = gate["id"]
        if gate["kind"] == "protected-check":
            results.append(
                protected_check_result(
                    gate=gate,
                    receipt=fetch_qualification_receipt(
                        arguments.repository,
                        commit,
                        gate.get("checkName", identifier),
                    ),
                    repository=arguments.repository,
                    commit=commit,
                    tree=tree,
                )
            )
            continue

        stage = STAGE_BY_GATE[identifier]
        checkpoint = json.loads(
            (checkpoint_directory / f"{stage}.json").read_text(encoding="utf-8")
        )
        artifact = selected_artifact(selection, stage)
        stage_evidence = sorted((evidence_root / stage).rglob("*")) if (
            evidence_root / stage
        ).is_dir() else []
        files = [path for path in stage_evidence if path.is_file()]
        extra: dict[str, Any] = {}
        if identifier in DEPENDENCY_SNAPSHOT_GATES:
            # Each matrix gate resolves several legs, one per pinned dependency
            # version, and each leg writes its own resolved graph into its own
            # directory. The identity therefore spans every leg: digesting one
            # of them would describe a fraction of what the gate proved.
            snapshots = sorted(
                (evidence_root / stage).rglob("resolved-packages.json")
            )
            if not snapshots:
                raise QualificationError(
                    f"Gate '{identifier}' resolved a floating dependency leg but "
                    "recorded no resolved-packages.json."
                )
            extra["dependencySnapshotDigest"] = digest_files(snapshots)
            extra["dependencySnapshotCount"] = len(snapshots)

        results.append(
            candidate_produced_result(
                gate=gate,
                checkpoint=checkpoint,
                artifact=artifact,
                selection=selection,
                repository=arguments.repository,
                commit=commit,
                tree=tree,
                digest=digest_files(files),
                extra=extra,
            )
        )

    for result in results:
        result["policyDigest"] = digest

    return results


def main(argv: Sequence[str] | None = None) -> int:
    """Derive gate results for the candidate commit."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    derive_parser = subparsers.add_parser("derive")
    derive_parser.add_argument("--repo", required=True)
    derive_parser.add_argument("--repository", required=True)
    derive_parser.add_argument("--commit", required=True)
    derive_parser.add_argument("--selection", required=True)
    derive_parser.add_argument("--checkpoint-directory", required=True)
    derive_parser.add_argument("--evidence-root", required=True)
    derive_parser.add_argument("--assembling-attempt", required=True, type=int)
    derive_parser.add_argument("--policy", type=Path)
    derive_parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args(argv)

    try:
        results = derive(arguments)
    except (OSError, json.JSONDecodeError) as error:
        print(f"Gate evidence is unreadable: {error}", file=sys.stderr)
        return 1
    except (QualificationError, TrustRootError) as error:
        print(f"Gate evidence derivation failed: {error}", file=sys.stderr)
        return 1

    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(
        json.dumps(results, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Gate evidence derived: {arguments.output}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
