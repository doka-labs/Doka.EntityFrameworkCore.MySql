"""Materialize and validate portable SLSA provenance for release assets."""

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import json
import sys
from collections.abc import Iterable
from pathlib import Path
from typing import Any

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


def verify_portable_bundle(
    path: Path,
    subjects: Iterable[Path],
    *,
    require_exact: bool = True,
) -> None:
    """Validate one single-record portable JSONL bundle and its subjects."""
    validate_bundle(
        read_bundle(path, json_lines=True),
        subjects,
        require_exact=require_exact,
    )


def materialize_bundle(source: Path, output: Path, subjects: Iterable[Path]) -> None:
    """Validate an action bundle and persist its canonical portable JSONL form."""
    bundle = read_bundle(source, json_lines=False)
    validate_bundle(bundle, subjects, require_exact=True)

    if output.name != PORTABLE_PROVENANCE_NAME:
        raise ProvenanceError(
            f"Portable provenance must be named {PORTABLE_PROVENANCE_NAME}."
        )

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(bundle, sort_keys=True, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    verify_portable_bundle(output, subjects)


def build_parser() -> argparse.ArgumentParser:
    """Create the portable provenance command-line contract."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    materialize = subparsers.add_parser("materialize")
    materialize.add_argument("--bundle", type=Path, required=True)
    materialize.add_argument("--output", type=Path, required=True)
    add_release_layout_arguments(materialize)

    verify = subparsers.add_parser("verify")
    verify.add_argument("--bundle", type=Path, required=True)
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
        if args.command == "materialize":
            materialize_bundle(args.bundle, args.output, subjects)
        else:
            verify_portable_bundle(args.bundle, subjects)
    except ProvenanceError as exception:
        print(f"Release provenance failed: {exception}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
