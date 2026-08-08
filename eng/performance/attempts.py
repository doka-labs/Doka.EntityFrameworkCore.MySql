#!/usr/bin/env python3
"""Record and select bounded performance-measurement attempts."""

from __future__ import annotations

import shutil
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Sequence

from .contract import (
    MEASUREMENT_QUALITY_EXIT_CODE,
    MeasurementQualityError,
    PerformanceEvidenceError,
    load_json,
    required_commit,
    required_positive_integer,
    required_sha256,
    required_string,
    sha256,
    write_json,
)

ATTEMPT_SCHEMA_VERSION = 1
ATTEMPT_KIND = "performance-attempt-receipt"
SELECTION_KIND = "performance-attempt-selection"
IMPORT_KIND = "performance-attempt-import"
MAXIMUM_ATTEMPTS = 2
VALID_STATUSES = {"passed", "inconclusive", "failed"}


def classify_exit_code(exit_code: int) -> str:
    """Map a benchmark exit code to its non-overlapping workflow state."""
    if exit_code == 0:
        return "passed"
    if exit_code == MEASUREMENT_QUALITY_EXIT_CODE:
        return "inconclusive"

    return "failed"


def _relative_regular_file(path: Path, root: Path, label: str) -> str:
    """Bind one regular evidence file to its artifact root."""
    if path.is_symlink():
        raise PerformanceEvidenceError(f"{label} '{path}' must be a regular file.")

    resolved_root = root.resolve()
    resolved_path = path.resolve()
    try:
        relative_path = resolved_path.relative_to(resolved_root)
    except ValueError as error:
        raise PerformanceEvidenceError(
            f"{label} '{path}' must be below artifact root '{root}'."
        ) from error
    if not resolved_path.is_file():
        raise PerformanceEvidenceError(f"{label} '{path}' must be a regular file.")

    return relative_path.as_posix()


def _validate_evaluation_identity(
    evaluation_path: Path,
    *,
    target: str,
    profile: str,
    run_id: str,
    commit: str,
    source_hash: str,
    runner_class: str,
) -> dict[str, Any]:
    """Prove that a passed attempt owns its claimed evaluation."""
    evaluation = load_json(evaluation_path)
    expected = {
        "schemaVersion": 3,
        "kind": "performance-evaluation",
        "success": True,
        "target": target,
        "profile": profile,
        "runId": run_id,
        "commit": commit,
        "sourceHash": source_hash,
        "runnerClass": runner_class,
    }
    for key, expected_value in expected.items():
        actual_value = evaluation.get(key)
        if actual_value != expected_value:
            raise PerformanceEvidenceError(
                f"Performance evaluation {key} is '{actual_value}', "
                f"expected '{expected_value}'."
            )

    return evaluation


def record_attempt(
    *,
    artifact_root: Path,
    report_directory: Path,
    output: Path,
    target: str,
    profile: str,
    attempt: int,
    run_id: str,
    commit: str,
    source_hash: str,
    runner_class: str,
    exit_code: int,
) -> dict[str, Any]:
    """Persist one immutable classification receipt for a benchmark attempt."""
    if attempt < 1 or attempt > MAXIMUM_ATTEMPTS:
        raise PerformanceEvidenceError(
            f"Attempt must be between 1 and {MAXIMUM_ATTEMPTS}."
        )
    if exit_code < 0:
        raise PerformanceEvidenceError("Attempt exit code must be non-negative.")

    status = classify_exit_code(exit_code)
    artifact_root = artifact_root.resolve()
    report_directory = report_directory.resolve()
    output = output.resolve()
    try:
        report_relative_path = report_directory.relative_to(artifact_root).as_posix()
        output.relative_to(artifact_root)
    except ValueError as error:
        raise PerformanceEvidenceError(
            "Attempt reports and receipt must remain below the artifact root."
        ) from error
    if status == "passed" and (
        not report_directory.is_dir() or report_directory.is_symlink()
    ):
        raise PerformanceEvidenceError(
            f"Attempt report directory '{report_directory}' does not exist."
        )

    evaluation_relative_path: str | None = None
    evaluation_sha256: str | None = None
    if status == "passed":
        evaluation_path = (
            report_directory / "evidence" / "performance-evaluation.json"
        )
        _validate_evaluation_identity(
            evaluation_path,
            target=target,
            profile=profile,
            run_id=run_id,
            commit=commit,
            source_hash=source_hash,
            runner_class=runner_class,
        )
        evaluation_relative_path = _relative_regular_file(
            evaluation_path,
            artifact_root,
            "Performance evaluation",
        )
        evaluation_sha256 = sha256(evaluation_path)

    payload: dict[str, Any] = {
        "schemaVersion": ATTEMPT_SCHEMA_VERSION,
        "kind": ATTEMPT_KIND,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace(
            "+00:00",
            "Z",
        ),
        "target": target,
        "profile": profile,
        "attempt": attempt,
        "runId": run_id,
        "commit": commit,
        "sourceHash": source_hash,
        "runnerClass": runner_class,
        "exitCode": exit_code,
        "status": status,
        "reportRelativePath": report_relative_path,
        "evaluationRelativePath": evaluation_relative_path,
        "evaluationSha256": evaluation_sha256,
    }
    write_json(output, payload)
    return payload


def _validate_receipt(path: Path) -> dict[str, Any]:
    """Load one receipt and validate all selection-critical fields."""
    if path.is_symlink() or not path.is_file():
        raise PerformanceEvidenceError(
            f"Attempt receipt '{path}' must be a regular file."
        )

    receipt = load_json(path)
    if receipt.get("schemaVersion") != ATTEMPT_SCHEMA_VERSION:
        raise PerformanceEvidenceError(
            f"Attempt receipt '{path}' has an unsupported schema version."
        )
    if receipt.get("kind") != ATTEMPT_KIND:
        raise PerformanceEvidenceError(
            f"Attempt receipt '{path}' has an unsupported kind."
        )

    required_string(receipt, "target", "attempt receipt")
    required_string(receipt, "profile", "attempt receipt")
    required_string(receipt, "runId", "attempt receipt")
    required_commit(receipt, "commit", "attempt receipt")
    required_sha256(receipt, "sourceHash", "attempt receipt")
    required_string(receipt, "runnerClass", "attempt receipt")
    attempt = required_positive_integer(receipt, "attempt", "attempt receipt")
    if attempt > MAXIMUM_ATTEMPTS:
        raise PerformanceEvidenceError(
            f"Attempt receipt '{path}' exceeds the bounded attempt count."
        )
    status = receipt.get("status")
    if status not in VALID_STATUSES:
        raise PerformanceEvidenceError(
            f"Attempt receipt '{path}' has invalid status '{status}'."
        )
    exit_code = receipt.get("exitCode")
    if not isinstance(exit_code, int) or isinstance(exit_code, bool) or exit_code < 0:
        raise PerformanceEvidenceError(
            f"Attempt receipt '{path}' has an invalid exit code."
        )
    if classify_exit_code(exit_code) != status:
        raise PerformanceEvidenceError(
            f"Attempt receipt '{path}' status does not match its exit code."
        )

    return receipt


def _bound_receipt_file(
    artifact_root: Path,
    relative_path: object,
    label: str,
) -> Path:
    """Resolve a receipt-owned file without accepting path traversal."""
    if not isinstance(relative_path, str) or not relative_path:
        raise PerformanceEvidenceError(f"{label} must be a non-empty path.")

    candidate = Path(relative_path)
    if candidate.is_absolute() or ".." in candidate.parts:
        raise PerformanceEvidenceError(
            f"{label} '{relative_path}' must remain below its artifact root."
        )

    path = artifact_root / candidate
    canonical_relative_path = _relative_regular_file(path, artifact_root, label)
    if canonical_relative_path != candidate.as_posix():
        raise PerformanceEvidenceError(
            f"{label} '{relative_path}' is not a canonical artifact path."
        )

    return path


def _copy_artifact_tree(source: Path, destination: Path) -> None:
    """Copy one selected artifact tree without following symbolic links."""
    if destination.exists():
        raise PerformanceEvidenceError(
            f"Selected artifact destination '{destination}' already exists."
        )
    for entry in source.rglob("*"):
        if entry.is_symlink():
            raise PerformanceEvidenceError(
                f"Attempt artifact '{entry}' must not be a symbolic link."
            )

    shutil.copytree(source, destination)


def _validate_artifact_directory(path: Path, label: str) -> Path:
    """Reject missing directories and symbolic links at evidence boundaries."""
    if path.is_symlink() or not path.is_dir():
        raise PerformanceEvidenceError(
            f"{label} '{path}' must be a regular directory."
        )
    for entry in path.rglob("*"):
        if entry.is_symlink():
            raise PerformanceEvidenceError(
                f"{label} entry '{entry}' must not be a symbolic link."
            )

    return path.resolve()


def select_attempt(
    *,
    receipt_paths: Sequence[Path],
    destination: Path,
) -> dict[str, Any]:
    """Select one conclusive attempt without allowing retries to mask failures."""
    if not receipt_paths or len(receipt_paths) > MAXIMUM_ATTEMPTS:
        raise PerformanceEvidenceError(
            f"Selection requires between 1 and {MAXIMUM_ATTEMPTS} receipts."
        )

    loaded = [
        (path.resolve(), _validate_receipt(path.resolve()))
        for path in receipt_paths
    ]
    loaded.sort(key=lambda item: item[1]["attempt"])
    attempts = [receipt["attempt"] for _, receipt in loaded]
    if attempts != list(range(1, len(loaded) + 1)):
        raise PerformanceEvidenceError(
            "Attempt receipts must form the consecutive sequence starting at 1."
        )

    identity_keys = ("target", "profile", "commit", "sourceHash", "runnerClass")
    first = loaded[0][1]
    for _, receipt in loaded[1:]:
        for key in identity_keys:
            if receipt[key] != first[key]:
                raise PerformanceEvidenceError(
                    f"Attempt receipt identity mismatch for '{key}'."
                )

    selected_index: int | None = None
    if first["status"] == "passed":
        if len(loaded) != 1:
            raise PerformanceEvidenceError(
                "A passing first benchmark attempt must not be followed by a retry."
            )
        selected_index = 0
    elif first["status"] == "failed":
        raise PerformanceEvidenceError(
            "The first benchmark attempt failed a correctness or budget gate; "
            "a retry cannot mask that failure."
        )
    elif len(loaded) == 1:
        raise MeasurementQualityError(
            "The first benchmark attempt was inconclusive and no bounded retry "
            "receipt was supplied."
        )
    else:
        second = loaded[1][1]
        if second["status"] == "passed":
            selected_index = 1
        elif second["status"] == "failed":
            raise PerformanceEvidenceError(
                "The bounded retry failed a correctness or budget gate."
            )
        else:
            raise MeasurementQualityError(
                "Both benchmark attempts were inconclusive on independent runners."
            )

    selected_path, selected = loaded[selected_index]
    artifact_root = selected_path.parent
    evaluation_relative_path = selected.get("evaluationRelativePath")
    evaluation_path = _bound_receipt_file(
        artifact_root,
        evaluation_relative_path,
        "Selected performance evaluation",
    )
    _validate_evaluation_identity(
        evaluation_path,
        target=selected["target"],
        profile=selected["profile"],
        run_id=selected["runId"],
        commit=selected["commit"],
        source_hash=selected["sourceHash"],
        runner_class=selected["runnerClass"],
    )
    if sha256(evaluation_path) != selected.get("evaluationSha256"):
        raise PerformanceEvidenceError(
            "Selected performance evaluation digest does not match its receipt."
        )

    destination = destination.resolve()
    _copy_artifact_tree(artifact_root, destination)
    receipt_directory = destination / "attempt-receipts"
    if receipt_directory.exists():
        raise PerformanceEvidenceError(
            "Selected attempt artifacts must not own the canonical receipt directory."
        )
    receipt_directory.mkdir()

    receipt_bindings: list[dict[str, Any]] = []
    for path, receipt in loaded:
        receipt_name = f"attempt-{receipt['attempt']}.json"
        receipt_destination = receipt_directory / receipt_name
        shutil.copy2(path, receipt_destination)
        receipt_bindings.append(
            {
                "attempt": receipt["attempt"],
                "status": receipt["status"],
                "relativePath": f"attempt-receipts/{receipt_name}",
                "sha256": sha256(receipt_destination),
            }
        )

    payload = {
        "schemaVersion": ATTEMPT_SCHEMA_VERSION,
        "kind": SELECTION_KIND,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace(
            "+00:00",
            "Z",
        ),
        "target": selected["target"],
        "profile": selected["profile"],
        "commit": selected["commit"],
        "sourceHash": selected["sourceHash"],
        "runnerClass": selected["runnerClass"],
        "selectedAttempt": selected["attempt"],
        "selectedRunId": selected["runId"],
        "evaluationRelativePath": evaluation_relative_path,
        "evaluationSha256": selected["evaluationSha256"],
        "receipts": receipt_bindings,
    }
    return payload


def verify_selection(
    *,
    artifact_root: Path,
    selection_path: Path,
) -> dict[str, Any]:
    """Verify a selected attempt before another workflow consumes it."""
    artifact_root = _validate_artifact_directory(
        artifact_root,
        "Selected performance artifact",
    )
    selection_relative_path = _relative_regular_file(
        selection_path,
        artifact_root,
        "Performance attempt selection",
    )
    selection_path = _bound_receipt_file(
        artifact_root,
        selection_relative_path,
        "Performance attempt selection",
    )
    selection = load_json(selection_path)
    if selection.get("schemaVersion") != ATTEMPT_SCHEMA_VERSION:
        raise PerformanceEvidenceError(
            "Performance attempt selection has an unsupported schema version."
        )
    if selection.get("kind") != SELECTION_KIND:
        raise PerformanceEvidenceError(
            "Performance attempt selection has an unsupported kind."
        )

    identity = {
        "target": required_string(selection, "target", "attempt selection"),
        "profile": required_string(selection, "profile", "attempt selection"),
        "commit": required_commit(selection, "commit", "attempt selection"),
        "sourceHash": required_sha256(
            selection,
            "sourceHash",
            "attempt selection",
        ),
        "runnerClass": required_string(
            selection,
            "runnerClass",
            "attempt selection",
        ),
    }
    selected_attempt = required_positive_integer(
        selection,
        "selectedAttempt",
        "attempt selection",
    )
    if selected_attempt > MAXIMUM_ATTEMPTS:
        raise PerformanceEvidenceError(
            "Performance attempt selection exceeds the bounded attempt count."
        )
    selected_run_id = required_string(
        selection,
        "selectedRunId",
        "attempt selection",
    )
    evaluation_sha256 = required_sha256(
        selection,
        "evaluationSha256",
        "attempt selection",
    )

    bindings = selection.get("receipts")
    if not isinstance(bindings, list) or not 1 <= len(bindings) <= MAXIMUM_ATTEMPTS:
        raise PerformanceEvidenceError(
            "Performance attempt selection must bind one or two receipts."
        )

    receipts: list[dict[str, Any]] = []
    for expected_attempt, binding in enumerate(bindings, start=1):
        if not isinstance(binding, dict):
            raise PerformanceEvidenceError(
                "Performance attempt selection receipt bindings must be objects."
            )
        if binding.get("attempt") != expected_attempt:
            raise PerformanceEvidenceError(
                "Performance attempt selection receipts must be consecutive."
            )
        status = binding.get("status")
        if status not in VALID_STATUSES:
            raise PerformanceEvidenceError(
                "Performance attempt selection contains an invalid receipt status."
            )
        receipt_path = _bound_receipt_file(
            artifact_root,
            binding.get("relativePath"),
            "Selected attempt receipt",
        )
        binding_sha256 = required_sha256(
            binding,
            "sha256",
            "attempt selection receipt binding",
        )
        if sha256(receipt_path) != binding_sha256:
            raise PerformanceEvidenceError(
                "Selected attempt receipt digest does not match its binding."
            )
        receipt = _validate_receipt(receipt_path)
        if receipt["attempt"] != expected_attempt or receipt["status"] != status:
            raise PerformanceEvidenceError(
                "Selected attempt receipt does not match its binding."
            )
        for key, expected_value in identity.items():
            if receipt[key] != expected_value:
                raise PerformanceEvidenceError(
                    f"Selected attempt receipt identity mismatch for '{key}'."
                )
        receipts.append(receipt)

    first = receipts[0]
    if first["status"] == "passed":
        if len(receipts) != 1 or selected_attempt != 1:
            raise PerformanceEvidenceError(
                "A passing first benchmark attempt cannot own a retry."
            )
    elif first["status"] == "failed":
        raise PerformanceEvidenceError(
            "A failed first benchmark attempt cannot produce a selection."
        )
    elif (
        len(receipts) != 2
        or receipts[1]["status"] != "passed"
        or selected_attempt != 2
    ):
        raise PerformanceEvidenceError(
            "An inconclusive first attempt requires one passing bounded retry."
        )

    selected_receipt = receipts[selected_attempt - 1]
    if selected_receipt["runId"] != selected_run_id:
        raise PerformanceEvidenceError(
            "Selected attempt run identifier does not match its receipt."
        )
    if selected_receipt.get("evaluationSha256") != evaluation_sha256:
        raise PerformanceEvidenceError(
            "Selected evaluation digest does not match its attempt receipt."
        )

    evaluation_path = _bound_receipt_file(
        artifact_root,
        selection.get("evaluationRelativePath"),
        "Selected performance evaluation",
    )
    _validate_evaluation_identity(
        evaluation_path,
        target=identity["target"],
        profile=identity["profile"],
        run_id=selected_run_id,
        commit=identity["commit"],
        source_hash=identity["sourceHash"],
        runner_class=identity["runnerClass"],
    )
    if sha256(evaluation_path) != evaluation_sha256:
        raise PerformanceEvidenceError(
            "Selected performance evaluation digest does not match the selection."
        )

    return selection


def import_selection(
    *,
    artifact_root: Path,
    selection_path: Path,
    destination: Path,
    expected_target: str | None = None,
    expected_commit: str | None = None,
) -> dict[str, Any]:
    """Import qualified scorecard evidence into a release-candidate stage."""
    artifact_root = artifact_root.resolve()
    selection = verify_selection(
        artifact_root=artifact_root,
        selection_path=selection_path,
    )
    if expected_target is not None and selection["target"] != expected_target:
        raise PerformanceEvidenceError(
            "Qualified performance target does not match the release stage."
        )
    if expected_commit is not None and selection["commit"] != expected_commit:
        raise PerformanceEvidenceError(
            "Qualified performance commit does not match the release candidate."
        )
    destination = destination.resolve()
    if destination.exists():
        raise PerformanceEvidenceError(
            f"Performance import destination '{destination}' already exists."
        )

    destination.mkdir(parents=True)
    qualified_artifact = destination / "qualified-artifact"
    _copy_artifact_tree(artifact_root, qualified_artifact)

    source_evaluation = _bound_receipt_file(
        artifact_root,
        selection["evaluationRelativePath"],
        "Selected performance evaluation",
    )
    source_report = _validate_artifact_directory(
        source_evaluation.parent.parent,
        "Selected performance report",
    )
    shutil.copytree(source_report, destination, dirs_exist_ok=True)

    selection_relative_path = _relative_regular_file(
        selection_path,
        artifact_root,
        "Performance attempt selection",
    )
    imported_evaluation = destination / "evidence" / "performance-evaluation.json"
    receipt = {
        "schemaVersion": ATTEMPT_SCHEMA_VERSION,
        "kind": IMPORT_KIND,
        "generatedUtc": datetime.now(timezone.utc).isoformat().replace(
            "+00:00",
            "Z",
        ),
        "target": selection["target"],
        "profile": selection["profile"],
        "commit": selection["commit"],
        "sourceHash": selection["sourceHash"],
        "runnerClass": selection["runnerClass"],
        "selectedAttempt": selection["selectedAttempt"],
        "selectedRunId": selection["selectedRunId"],
        "selectionRelativePath": (
            f"qualified-artifact/{selection_relative_path}"
        ),
        "selectionSha256": sha256(selection_path),
        "evaluationRelativePath": "evidence/performance-evaluation.json",
        "qualifiedEvaluationRelativePath": (
            "qualified-artifact/"
            f"{selection['evaluationRelativePath']}"
        ),
        "evaluationSha256": sha256(imported_evaluation),
    }
    write_json(destination / "import-receipt.json", receipt)
    return receipt


def verify_imported_selection(
    *,
    destination: Path,
    expected_target: str | None = None,
    expected_commit: str | None = None,
) -> dict[str, Any]:
    """Verify the complete qualified-evidence chain after RC import."""
    destination = _validate_artifact_directory(
        destination,
        "Imported performance evidence",
    )
    receipt_path = destination / "import-receipt.json"
    receipt = load_json(receipt_path)
    if receipt.get("schemaVersion") != ATTEMPT_SCHEMA_VERSION:
        raise PerformanceEvidenceError(
            "Performance import receipt has an unsupported schema version."
        )
    if receipt.get("kind") != IMPORT_KIND:
        raise PerformanceEvidenceError(
            "Performance import receipt has an unsupported kind."
        )

    identity = {
        "target": required_string(receipt, "target", "performance import"),
        "profile": required_string(receipt, "profile", "performance import"),
        "commit": required_commit(receipt, "commit", "performance import"),
        "sourceHash": required_sha256(
            receipt,
            "sourceHash",
            "performance import",
        ),
        "runnerClass": required_string(
            receipt,
            "runnerClass",
            "performance import",
        ),
    }
    if expected_target is not None and identity["target"] != expected_target:
        raise PerformanceEvidenceError(
            "Imported performance target does not match the release stage."
        )
    if expected_commit is not None and identity["commit"] != expected_commit:
        raise PerformanceEvidenceError(
            "Imported performance commit does not match the release candidate."
        )
    selected_attempt = required_positive_integer(
        receipt,
        "selectedAttempt",
        "performance import",
    )
    selected_run_id = required_string(
        receipt,
        "selectedRunId",
        "performance import",
    )
    expected_evaluation_sha256 = required_sha256(
        receipt,
        "evaluationSha256",
        "performance import",
    )

    selection_path = _bound_receipt_file(
        destination,
        receipt.get("selectionRelativePath"),
        "Imported performance attempt selection",
    )
    if sha256(selection_path) != required_sha256(
        receipt,
        "selectionSha256",
        "performance import",
    ):
        raise PerformanceEvidenceError(
            "Imported performance attempt selection digest does not match."
        )
    qualified_artifact = destination / "qualified-artifact"
    selection = verify_selection(
        artifact_root=qualified_artifact,
        selection_path=selection_path,
    )
    for key, expected_value in identity.items():
        if selection[key] != expected_value:
            raise PerformanceEvidenceError(
                f"Imported performance identity mismatch for '{key}'."
            )
    if (
        selection["selectedAttempt"] != selected_attempt
        or selection["selectedRunId"] != selected_run_id
    ):
        raise PerformanceEvidenceError(
            "Imported performance selection does not match its receipt."
        )

    evaluation_path = _bound_receipt_file(
        destination,
        receipt.get("evaluationRelativePath"),
        "Imported performance evaluation",
    )
    qualified_evaluation_path = _bound_receipt_file(
        destination,
        receipt.get("qualifiedEvaluationRelativePath"),
        "Qualified performance evaluation",
    )
    for path in (evaluation_path, qualified_evaluation_path):
        if sha256(path) != expected_evaluation_sha256:
            raise PerformanceEvidenceError(
                "Imported performance evaluation digest does not match."
            )
    _validate_evaluation_identity(
        evaluation_path,
        target=identity["target"],
        profile=identity["profile"],
        run_id=selected_run_id,
        commit=identity["commit"],
        source_hash=identity["sourceHash"],
        runner_class=identity["runnerClass"],
    )

    return receipt
