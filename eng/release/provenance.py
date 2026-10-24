"""Materialize and validate portable SLSA provenance for release assets."""

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import json
import os
import re
import sys
from collections.abc import Iterable
from pathlib import Path
from typing import Any

if __package__:
    from . import lines as release_lines
else:  # pragma: no cover - direct execution path
    import lines as release_lines

PORTABLE_PROVENANCE_NAME = "release-provenance.intoto.jsonl"
SIGSTORE_BUNDLE_MEDIA_TYPE = "application/vnd.dev.sigstore.bundle.v0.3+json"
IN_TOTO_PAYLOAD_TYPE = "application/vnd.in-toto+json"
IN_TOTO_STATEMENT_TYPE = "https://in-toto.io/Statement/v1"
SLSA_PROVENANCE_TYPE = "https://slsa.dev/provenance/v1"


class ProvenanceError(RuntimeError):
    """Raised when portable release provenance is incomplete or ambiguous."""


def sha256_file(path: Path) -> str:
    """Hash one regular subject without following symbolic links."""
    if not path.is_file() or path.is_symlink():
        raise ProvenanceError(f"Provenance subject is missing or non-regular: {path}")

    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as exception:
        raise ProvenanceError(
            f"Provenance subject is unreadable: {path}"
        ) from exception

    return digest.hexdigest()


def read_bundle(path: Path, *, json_lines: bool) -> dict[str, Any]:
    """Read one regular Sigstore bundle in JSON or single-record JSONL form."""
    if not path.is_file() or path.is_symlink():
        raise ProvenanceError(f"Provenance bundle is missing or non-regular: {path}")

    try:
        text = path.read_text(encoding="utf-8")
        if json_lines:
            lines = [line for line in text.splitlines() if line.strip()]
            if len(lines) != 1:
                raise ProvenanceError(
                    "Portable provenance must contain exactly one JSONL record."
                )
            value = json.loads(lines[0])
        else:
            value = json.loads(text)
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise ProvenanceError(f"Provenance bundle is unreadable: {path}") from exception

    if not isinstance(value, dict):
        raise ProvenanceError("Provenance bundle must contain one JSON object.")

    return value


def statement_from_bundle(bundle: dict[str, Any]) -> dict[str, Any]:
    """Decode and validate the signed in-toto statement envelope."""
    envelope = bundle.get("dsseEnvelope")
    verification_material = bundle.get("verificationMaterial")
    if (
        bundle.get("mediaType") != SIGSTORE_BUNDLE_MEDIA_TYPE
        or not isinstance(envelope, dict)
        or not isinstance(verification_material, dict)
        or not verification_material
        or envelope.get("payloadType") != IN_TOTO_PAYLOAD_TYPE
        or not isinstance(envelope.get("signatures"), list)
        or not envelope["signatures"]
        or not isinstance(envelope.get("payload"), str)
    ):
        raise ProvenanceError("Sigstore bundle envelope is invalid.")

    try:
        payload = base64.b64decode(envelope["payload"], validate=True)
        statement = json.loads(payload)
    except (binascii.Error, UnicodeError, json.JSONDecodeError) as exception:
        raise ProvenanceError("Sigstore bundle payload is invalid.") from exception

    if (
        not isinstance(statement, dict)
        or statement.get("_type") != IN_TOTO_STATEMENT_TYPE
        or statement.get("predicateType") != SLSA_PROVENANCE_TYPE
        or not isinstance(statement.get("predicate"), dict)
    ):
        raise ProvenanceError("Bundle does not contain SLSA build provenance.")

    return statement


def subject_digests(bundle: dict[str, Any]) -> dict[str, str]:
    """Return the unique SHA-256 subject inventory from one SLSA statement."""
    subjects = statement_from_bundle(bundle).get("subject")
    if not isinstance(subjects, list) or not subjects:
        raise ProvenanceError("SLSA provenance has no subjects.")

    inventory: dict[str, str] = {}
    for subject in subjects:
        if not isinstance(subject, dict):
            raise ProvenanceError("SLSA provenance contains an invalid subject.")

        name = subject.get("name")
        digest = subject.get("digest")
        sha256 = digest.get("sha256") if isinstance(digest, dict) else None
        if (
            not isinstance(name, str)
            or not name
            or Path(name).name != name
            or not isinstance(sha256, str)
            or len(sha256) != 64
            or any(character not in "0123456789abcdef" for character in sha256)
            or name in inventory
        ):
            raise ProvenanceError("SLSA provenance subject inventory is invalid.")

        inventory[name] = sha256

    return inventory


def required_subject_digests(subjects: Iterable[Path]) -> dict[str, str]:
    """Build the expected unique subject inventory from local files."""
    expected: dict[str, str] = {}
    seen_paths: set[Path] = set()
    for subject in subjects:
        path = subject.resolve()
        if path in seen_paths:
            continue
        seen_paths.add(path)

        if path.name in expected:
            raise ProvenanceError(
                f"Provenance subject names must be unique: {path.name}"
            )
        expected[path.name] = sha256_file(path)

    if not expected:
        raise ProvenanceError("At least one provenance subject is required.")

    return expected


def release_subjects(
    candidate_root: Path,
    publication_evidence: Path,
    checkpoint_root: Path,
) -> list[Path]:
    """Resolve the complete release layout attested by the hosted workflow."""
    packages_root = candidate_root / "packages"
    primary_packages = sorted(packages_root.glob("*.nupkg"))
    symbol_packages = sorted(packages_root.glob("*.snupkg"))
    if len(primary_packages) != 3 or len(symbol_packages) != 3:
        raise ProvenanceError(
            "Release provenance requires three primary and three symbol packages."
        )

    checkpoints = sorted(checkpoint_root.glob("*.json"))
    if not checkpoints:
        raise ProvenanceError("Release provenance requires stage checkpoints.")

    subjects = [
        *primary_packages,
        *symbol_packages,
        candidate_root / "release-candidate-evidence.json",
        candidate_root / "release-candidate-evidence.sha256",
        publication_evidence / "candidate-receipt.json",
        publication_evidence / "candidate-publication-preflight.json",
        publication_evidence / "symbol-readback-manifest.json",
        *checkpoints,
    ]
    required_subject_digests(subjects)

    return subjects


def validate_bundle(
    bundle: dict[str, Any],
    subjects: Iterable[Path],
    *,
    require_exact: bool,
) -> None:
    """Require local subjects to occur with their exact SHA-256 digests."""
    actual = subject_digests(bundle)
    expected = required_subject_digests(subjects)
    for name, digest in expected.items():
        if actual.get(name) != digest:
            raise ProvenanceError(
                f"SLSA provenance does not bind the required subject: {name}"
            )

    if require_exact and set(actual) != set(expected):
        raise ProvenanceError(
            "SLSA provenance subject inventory does not match its selected inputs."
        )


def positive_attempt(value: int | str, subject: str) -> int:
    """Read one canonical attempt identity without accepting bools or floats."""
    if type(value) not in (int, str) or re.fullmatch(r"[1-9][0-9]*", str(value)) is None:
        raise ProvenanceError(f"{subject} must be a positive workflow attempt.")

    return int(value)


def hosted_run_attempt(manifest: dict[str, Any]) -> int | None:
    """Observe the current hosted attempt, retaining offline verification."""
    if os.environ.get("GITHUB_ACTIONS") != "true":
        return None

    run_id = os.environ.get("GITHUB_RUN_ID", "")
    workflow = manifest.get("workflow") or {}
    if re.fullmatch(r"[1-9][0-9]*", run_id) is None or run_id != str(workflow.get("runId", "")):
        raise ProvenanceError("Hosted provenance belongs to a different workflow run.")

    return positive_attempt(os.environ.get("GITHUB_RUN_ATTEMPT", ""), "Hosted publication attempt")


def validate_workflow_identity(
    bundle: dict[str, Any],
    manifest: dict[str, Any],
    *,
    signer_run_attempt: int | str | None = None,
    publication_run_attempt: int | str | None = None,
) -> None:
    """Bind portable caller/source/run identity independently of signer checks.

    WHY: SLSA externalParameters describes the invocation, not proof of its
    cryptographic signer. Even with one public entry, certificate binding stays
    in the gh-attestation signer-workflow/signer-digest check.
    """
    schema = manifest.get("schemaVersion")
    source = manifest.get("source") or {}
    workflow = manifest.get("workflow") or {}
    repository = workflow.get("repository")
    source_ref = source.get("ref")
    commit = source.get("commit")
    if (
        type(schema) is not int
        or schema not in (3, 4)
        or workflow.get("provider") != "github-actions"
        or not isinstance(repository, str)
        or re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) is None
        or not isinstance(commit, str)
        or re.fullmatch(r"[0-9a-f]{40}", commit) is None
    ):
        raise ProvenanceError("Provenance release workflow identity is invalid.")

    caller_path = ".github/workflows/release-candidate.yml"
    if schema == 4:
        line = manifest.get("releaseLine")
        try:
            release_lines.validate_snapshot(
                line, source_ref=source_ref, version=manifest.get("releaseVersion"), repository=repository
            )
        except release_lines.LinePolicyError as error:
            raise ProvenanceError(f"Provenance release line is invalid: {error}") from error

        caller_path = line["workflowPath"]
        signer_ref = f"{repository}/.github/workflows/release-candidate.yml@{source_ref}"
        if workflow.get("signerWorkflowRef") != signer_ref or workflow.get("signerDigest") != commit:
            raise ProvenanceError("Provenance reusable signer identity is invalid.")
    elif source_ref != "refs/heads/main" or "releaseLine" in manifest:
        raise ProvenanceError("Historical provenance release identity is main-only.")

    expected_ref = f"{repository}/{caller_path}@{source_ref}"
    if workflow.get("workflowRef") != expected_ref:
        raise ProvenanceError("Provenance caller workflow identity is invalid.")

    predicate = statement_from_bundle(bundle)["predicate"]
    definition = predicate.get("buildDefinition") or {}
    parameters = definition.get("externalParameters") or {}
    expected_workflow = {
        "repository": f"https://github.com/{repository}",
        "path": caller_path,
        "ref": source_ref,
    }
    actual_workflow = parameters.get("workflow")
    if not isinstance(actual_workflow, dict) or any(
        actual_workflow.get(field) != value for field, value in expected_workflow.items()
    ):
        raise ProvenanceError("SLSA provenance describes a different caller workflow.")

    dependencies = definition.get("resolvedDependencies")
    expected_uri = f"git+https://github.com/{repository}@{source_ref}"
    selected = [
        entry for entry in dependencies
        if isinstance(entry, dict) and entry.get("uri") == expected_uri
    ] if isinstance(dependencies, list) else []
    if len(selected) != 1 or (selected[0].get("digest") or {}).get("gitCommit") != commit:
        raise ProvenanceError("SLSA provenance does not bind the exact release source commit.")

    run_id = str(workflow.get("runId", ""))
    if re.fullmatch(r"[1-9][0-9]*", run_id) is None:
        raise ProvenanceError("Provenance workflow run identity is invalid.")

    qualification_attempt = positive_attempt(workflow.get("runAttempt", ""), "Frozen qualification attempt")
    invocation = ((predicate.get("runDetails") or {}).get("metadata") or {}).get("invocationId")
    prefix = f"https://github.com/{repository}/actions/runs/{run_id}/attempts/"
    match = re.fullmatch(re.escape(prefix) + r"([1-9][0-9]*)", invocation) if isinstance(invocation, str) else None
    if match is None:
        raise ProvenanceError("SLSA provenance describes a different workflow run.")

    signed_attempt = int(match.group(1))
    # WHY: rerunning failed jobs can attest an unchanged earlier qualification.
    # The signed invocation freezes the attester without rewriting sealed proof.
    if signed_attempt < qualification_attempt:
        raise ProvenanceError("SLSA attesting attempt precedes qualification.")

    if signer_run_attempt is not None and signed_attempt != positive_attempt(
        signer_run_attempt, "Expected attesting attempt"
    ):
        raise ProvenanceError("SLSA invocation differs from the exact attesting attempt.")

    if publication_run_attempt is not None and signed_attempt > positive_attempt(
        publication_run_attempt, "Current publication attempt"
    ):
        raise ProvenanceError("SLSA attesting attempt exceeds the current publication attempt.")


def verify_portable_bundle(
    path: Path,
    subjects: Iterable[Path],
    *,
    require_exact: bool = True,
    release_manifest: dict[str, Any] | None = None,
    publication_run_attempt: int | str | None = None,
) -> None:
    """Validate one single-record portable JSONL bundle and its subjects."""
    bundle = read_bundle(path, json_lines=True)
    validate_bundle(
        bundle,
        subjects,
        require_exact=require_exact,
    )
    if release_manifest is not None:
        hosted_attempt = hosted_run_attempt(release_manifest)
        if publication_run_attempt is None:
            publication_run_attempt = hosted_attempt
        elif hosted_attempt is not None and positive_attempt(
            publication_run_attempt, "Publication attempt"
        ) > hosted_attempt:
            raise ProvenanceError("The publication attempt exceeds the current hosted attempt.")

        validate_workflow_identity(
            bundle, release_manifest, publication_run_attempt=publication_run_attempt
        )


def materialize_bundle(
    source: Path,
    output: Path,
    subjects: Iterable[Path],
    *,
    release_manifest: dict[str, Any] | None = None,
    signer_run_attempt: int | str | None = None,
) -> None:
    """Validate an action bundle and persist its canonical portable JSONL form."""
    bundle = read_bundle(source, json_lines=False)
    validate_bundle(bundle, subjects, require_exact=True)
    if release_manifest is not None:
        hosted_attempt = hosted_run_attempt(release_manifest)
        if signer_run_attempt is None:
            signer_run_attempt = hosted_attempt
        elif hosted_attempt is not None and positive_attempt(signer_run_attempt, "Attesting attempt") != hosted_attempt:
            raise ProvenanceError("The attesting attempt differs from the current hosted attempt.")

        validate_workflow_identity(
            bundle, release_manifest, signer_run_attempt=signer_run_attempt,
            publication_run_attempt=hosted_attempt,
        )

    if output.name != PORTABLE_PROVENANCE_NAME:
        raise ProvenanceError(
            f"Portable provenance must be named {PORTABLE_PROVENANCE_NAME}."
        )

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(bundle, sort_keys=True, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    verify_portable_bundle(
        output, subjects, release_manifest=release_manifest,
        publication_run_attempt=signer_run_attempt,
    )


def build_parser() -> argparse.ArgumentParser:
    """Create the portable provenance command-line contract."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    materialize = subparsers.add_parser("materialize")
    materialize.add_argument("--bundle", type=Path, required=True)
    materialize.add_argument("--output", type=Path, required=True)
    materialize.add_argument("--signer-run-attempt")
    add_release_layout_arguments(materialize)

    verify = subparsers.add_parser("verify")
    verify.add_argument("--bundle", type=Path, required=True)
    verify.add_argument("--publication-run-attempt")
    add_release_layout_arguments(verify)

    return parser


def add_release_layout_arguments(parser: argparse.ArgumentParser) -> None:
    """Add the shared hosted release-layout inputs to one command."""
    parser.add_argument("--candidate-root", type=Path, required=True)
    parser.add_argument("--publication-evidence", type=Path, required=True)
    parser.add_argument("--checkpoint-root", type=Path, required=True)


def main() -> int:
    """Run one provenance operation with a stable failure boundary."""
    args = build_parser().parse_args()
    try:
        subjects = release_subjects(
            args.candidate_root,
            args.publication_evidence,
            args.checkpoint_root,
        )
        manifest = json.loads(
            (args.candidate_root / "release-candidate-evidence.json").read_text(encoding="utf-8")
        )
        if args.command == "materialize":
            materialize_bundle(
                args.bundle, args.output, subjects, release_manifest=manifest,
                signer_run_attempt=args.signer_run_attempt,
            )
        else:
            verify_portable_bundle(
                args.bundle, subjects, release_manifest=manifest,
                publication_run_attempt=args.publication_run_attempt,
            )
    except (ProvenanceError, OSError, json.JSONDecodeError) as exception:
        print(f"Release provenance failed: {exception}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
