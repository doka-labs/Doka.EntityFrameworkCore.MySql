"""Bind publication permission to protected-main policy and immutable attempts.

Candidate evidence answers what was tested. This module independently answers
whether those exact bytes may still be published now. No candidate checkout is
ever used as the current authorization authority.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import html
import io
import json
import os
import re
import subprocess
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import quote

from eng.release import artifacts, lines


AUTHORITY_KIND = "release-line-authority-observation"
ADMISSION_KIND = "release-publication-admission"
POLICY_PATH = "eng/release/lines-policy.json"
PUBLICATION_LOCK = "doka-publication"
ADMISSION_STEP_NAME = "Freeze publication admission for same-run recovery"
PUBLISHER_STEP_NAME = "Publish qualified immutable release"
SHA1 = re.compile(r"[0-9a-f]{40}\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
ADMISSION_ARTIFACT = re.compile(r"publication-admission-attempt-([1-9][0-9]*)\Z")
IDENTITY_KEYS = frozenset({
    "repository", "sourceRef", "sourceCommit", "releaseVersion", "releaseTag",
    "workflowPath", "workflowRunId", "candidateRunAttempt",
    "originalPublicationAttempt", "manifestSha256", "candidateReceiptSha256",
    "releaseLineSha256",
})


class PublicationAdmissionError(RuntimeError):
    """Indicate that current publication permission cannot be proven."""


class GitHubClient:
    """Read GitHub through the already authenticated official CLI."""

    def request(self, endpoint: str) -> Any:
        """Return a JSON API response without exposing authentication material."""
        try:
            result = subprocess.run(
                ["gh", "api", "--method", "GET", endpoint], capture_output=True, check=False, timeout=60,
            )
        except subprocess.TimeoutExpired as exception:
            raise PublicationAdmissionError("GitHub API read timed out; evidence is unavailable.") from exception

        if result.returncode != 0:
            status = re.search(rb"\(HTTP ([0-9]{3})\)", result.stderr)

            raise PublicationAdmissionError(
                f"GitHub API request failed for {endpoint} (status {int(status[1]) if status else None})."
            )

        try:
            return json.loads(result.stdout)
        except (ValueError, UnicodeError) as exception:
            raise PublicationAdmissionError("GitHub returned invalid JSON.") from exception

    def download(self, endpoint: str) -> bytes:
        """Download one API-addressed archive; callers verify its digest."""
        try:
            result = subprocess.run(
                ["gh", "api", "-H", "Accept: application/octet-stream", endpoint],
                capture_output=True, check=False, timeout=60,
            )
        except subprocess.TimeoutExpired as exception:
            raise PublicationAdmissionError("GitHub artifact download timed out; evidence is unavailable.") from exception
        if result.returncode != 0:
            raise PublicationAdmissionError("GitHub artifact download failed.")

        return result.stdout

    def pages(self, endpoint: str, collection: str) -> list[dict[str, Any]]:
        """Read complete, duplicate-free collections instead of trusting page one."""
        entries: list[dict[str, Any]] = []
        page = 1
        expected: int | None = None
        seen: set[str] = set()
        separator = "&" if "?" in endpoint else "?"
        while True:
            response = self.request(f"{endpoint}{separator}per_page=100&page={page}")
            values = response.get(collection) if isinstance(response, dict) else response
            if not isinstance(values, list) or any(not isinstance(item, dict) for item in values):
                raise PublicationAdmissionError("GitHub returned an invalid paginated collection.")

            if isinstance(response, dict) and "total_count" in response:
                total = response["total_count"]
                if type(total) is not int or total < 0 or (expected is not None and total != expected):
                    raise PublicationAdmissionError("GitHub collection count changed or is invalid.")

                expected = total

            for item in values:
                key = str(item["id"]) if "id" in item else canonical_digest(item)
                if key in seen:
                    raise PublicationAdmissionError("GitHub collection contains duplicate identities.")

                seen.add(key)

            entries.extend(values)
            if expected is not None and len(entries) > expected:
                raise PublicationAdmissionError("GitHub collection inventory is incomplete or inconsistent.")

            # WHY: an exact full page is not proof of exhaustion, even when a
            # stale total_count claims it is. Retries need the whole inventory.
            if len(values) < 100:
                if expected is not None and len(entries) != expected:
                    raise PublicationAdmissionError("GitHub collection inventory is incomplete.")

                return entries

            page += 1


def canonical_digest(value: Any) -> str:
    """Return the SHA-256 of canonical, ASCII JSON evidence."""
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("ascii")

    return hashlib.sha256(encoded).hexdigest()


def read_json(path: Path) -> dict[str, Any]:
    """Read a regular JSON object without following a substituted symlink."""
    if path.is_symlink() or not path.is_file():
        raise PublicationAdmissionError(f"Evidence must be a regular file: {path.name}")

    try:
        result = json.loads(path.read_text(encoding="utf-8"))
    except (ValueError, UnicodeError, OSError) as exception:
        raise PublicationAdmissionError(f"Evidence is not valid JSON: {path.name}") from exception

    if not isinstance(result, dict):
        raise PublicationAdmissionError(f"Evidence must contain an object: {path.name}")

    return result


def write_json(path: Path, value: dict[str, Any]) -> None:
    """Persist ASCII evidence and refuse writes through substituted links."""
    if path.is_symlink():
        raise PublicationAdmissionError("Evidence output must not be a symlink.")

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True) + "\n", encoding="ascii")


def utc_now() -> str:
    """Return an explicit UTC timestamp for authorization observations."""
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def require_timestamp(value: Any) -> None:
    """Reject missing, naive, or malformed observation timestamps."""
    if not isinstance(value, str) or not value.endswith("Z"):
        raise PublicationAdmissionError("Evidence requires an explicit UTC timestamp.")

    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exception:
        raise PublicationAdmissionError("Evidence timestamp is invalid.") from exception


def require_repository(value: Any) -> str:
    """Validate an exact owner/repository identity before constructing API paths."""
    if not isinstance(value, str) or not REPOSITORY.fullmatch(value):
        raise PublicationAdmissionError("Repository identity is invalid.")

    return value


def validate_authority(value: dict[str, Any]) -> dict[str, Any]:
    """Validate a downloaded protected-main observation, including its policy."""
    if not isinstance(value, dict):
        raise PublicationAdmissionError("Authorization observation must be an object.")

    try:
        policy = lines.validate_policy(value.get("policy"))
    except (lines.LinePolicyError, TypeError) as exception:
        raise PublicationAdmissionError(f"Authorization policy is invalid: {exception}") from exception

    if (
        type(value.get("schemaVersion")) is not int or value.get("schemaVersion") != 1
        or value.get("kind") != AUTHORITY_KIND
        or value.get("repository") != policy["repository"]
        or not SHA1.fullmatch(str(value.get("controlCommit", "")))
        or value.get("authorizationRevision") != policy["revision"]
        or value.get("authorizationDigest") != lines.policy_digest(policy)
    ):
        raise PublicationAdmissionError("Authorization observation identity is invalid.")

    require_repository(value["repository"])
    require_timestamp(value.get("observedUtc"))

    return value


def validate_qualification_authority(
    authority: dict[str, Any],
    snapshot: dict[str, Any],
    *,
    repository: str | None = None,
    source_ref: str | None = None,
    version: str | None = None,
    workflow_path: str | None = None,
) -> None:
    """Bind frozen qualification to the independently downloaded authority."""
    validate_authority(authority)
    try:
        lines.validate_snapshot(
            snapshot, repository=repository, source_ref=source_ref,
            version=version, workflow_path=workflow_path,
        )
    except lines.LinePolicyError as exception:
        raise PublicationAdmissionError(str(exception)) from exception

    if (
        snapshot.get("authorizationRevision") != authority["authorizationRevision"]
        or snapshot.get("authorizationDigest") != authority["authorizationDigest"]
        or snapshot.get("repository") != authority["repository"]
    ):
        raise PublicationAdmissionError("Qualification uses a different authorization revision.")

    try:
        line = lines.select_line(authority["policy"], snapshot["sourceRef"], version, purpose="qualification")
    except lines.LinePolicyError as exception:
        raise PublicationAdmissionError(str(exception)) from exception

    for key in ("role", "packageMajor", "upstreamStage", "workflowPath", "publisherEnvironment"):
        if snapshot.get(key) != line.get(key):
            raise PublicationAdmissionError(f"Qualification authorization disagrees on {key}.")

    if snapshot.get("stateAtQualification") != line["state"]:
        raise PublicationAdmissionError("Qualification state disagrees with its observed authorization.")


def fetch_current_authority(repository: str, client: GitHubClient | None = None) -> dict[str, Any]:
    """Read current policy from an exact protected-main commit, never checkout."""
    require_repository(repository)
    api = client or GitHubClient()
    branch = api.request(f"/repos/{repository}/branches/main")
    commit = branch.get("commit") if isinstance(branch, dict) else None
    commit = commit.get("sha") if isinstance(commit, dict) else None
    if (
        not isinstance(branch, dict) or branch.get("name") != "main"
        or branch.get("protected") is not True or not isinstance(commit, str)
        or not SHA1.fullmatch(commit)
    ):
        raise PublicationAdmissionError("Current authority requires a protected main branch.")

    entry = api.request(f"/repos/{repository}/contents/{POLICY_PATH}?ref={commit}")
    if (
        not isinstance(entry, dict) or entry.get("type") != "file"
        or entry.get("path") != POLICY_PATH or entry.get("encoding") != "base64"
        or not isinstance(entry.get("content"), str)
        or not isinstance(entry.get("sha"), str) or not SHA1.fullmatch(entry["sha"])
    ):
        raise PublicationAdmissionError("Protected-main policy is absent or is not a regular file.")

    try:
        raw = base64.b64decode("".join(entry["content"].split()), validate=True)
        policy = lines.parse_policy(raw)
    except (ValueError, TypeError, lines.LinePolicyError) as exception:
        raise PublicationAdmissionError("Protected-main policy cannot be verified.") from exception

    # WHY: bind policy bytes to this exact Git blob, not just mutable API metadata.
    blob_sha = hashlib.sha1(f"blob {len(raw)}\0".encode("ascii") + raw).hexdigest()
    if blob_sha != entry["sha"]:
        raise PublicationAdmissionError("Protected-main policy bytes disagree with their Git blob.")

    if policy["repository"] != repository:
        raise PublicationAdmissionError("Protected-main policy belongs to another repository.")

    observation = {
        "schemaVersion": 1,
        "kind": AUTHORITY_KIND,
        "repository": repository,
        "controlCommit": commit,
        "authorizationRevision": policy["revision"],
        "authorizationDigest": lines.policy_digest(policy),
        "policy": policy,
        "observedUtc": utc_now(),
    }

    validate_authority(observation)

    return observation


def validate_environment(
    repository: str, line: dict[str, Any], policy: dict[str, Any], client: GitHubClient,
) -> None:
    """Verify the existing reviewer/branch gate without changing maintainer settings.

    WHY: administrator bypass and self-review are operator choices. Neither is
    a prerequisite for release lines, and this read-only check must not change
    them or invent another credential environment.
    """
    name = line["publisherEnvironment"]
    environment = client.request(f"/repos/{repository}/environments/{quote(name, safe='')}")
    protections = environment.get("protection_rules") if isinstance(environment, dict) else None
    branch_policy = environment.get("deployment_branch_policy") if isinstance(environment, dict) else None
    if (
        not isinstance(environment, dict) or environment.get("name") != name
        or not isinstance(protections, list)
        or not any(
            isinstance(rule, dict) and rule.get("type") == "required_reviewers"
            and isinstance(rule.get("reviewers"), list) and rule["reviewers"]
            and all(isinstance(reviewer, dict) for reviewer in rule["reviewers"])
            for rule in protections
        )
        or not isinstance(branch_policy, dict)
        or branch_policy.get("custom_branch_policies") is not True
        or branch_policy.get("protected_branches") is not False
    ):
        raise PublicationAdmissionError(
            "Publisher environment must already exist with reviewers and exact branch policies."
        )

    grants = client.pages(
        f"/repos/{repository}/environments/{quote(name, safe='')}/deployment-branch-policies", "branch_policies",
    )

    actual: set[str] = set()
    for entry in grants:
        branch = entry.get("name")
        if entry.get("type") != "branch" or not isinstance(branch, str) or any(value in branch for value in "*?["):
            raise PublicationAdmissionError("Publisher environment contains a wildcard or non-branch grant.")

        actual.add(f"refs/heads/{branch}")

    expected = {entry["sourceRef"] for entry in policy["lines"] if entry["state"] == "active"}
    if actual != expected or line["sourceRef"] not in actual:
        raise PublicationAdmissionError("Publisher environment branch grants disagree with current authority.")

    source = quote(line["sourceRef"].removeprefix("refs/heads/"), safe='')
    branch = client.request(f"/repos/{repository}/branches/{source}")
    if not isinstance(branch, dict) or branch.get("protected") is not True:
        raise PublicationAdmissionError("Publication source branch is not protected.")


def validate_admission(value: dict[str, Any]) -> dict[str, Any]:
    """Validate immutable admission identity independently of current rights."""
    if not isinstance(value, dict):
        raise PublicationAdmissionError("Admission must be an object.")

    identity = value.get("identity")
    if not isinstance(identity, dict) or set(identity) != IDENTITY_KEYS:
        raise PublicationAdmissionError("Admission identity fields are invalid.")

    if (
        type(value.get("schemaVersion")) is not int or value.get("schemaVersion") != 1
        or value.get("kind") != ADMISSION_KIND
        or value.get("admissionId") != canonical_digest(identity)
        or not SHA1.fullmatch(str(identity.get("sourceCommit", "")))
        or not str(identity.get("sourceRef", "")).startswith("refs/heads/")
        or identity.get("releaseTag") != f"v{identity.get('releaseVersion')}"
    ):
        raise PublicationAdmissionError("Admission identity or digest is invalid.")

    require_repository(identity["repository"])
    for key in ("manifestSha256", "candidateReceiptSha256", "releaseLineSha256"):
        if not SHA256.fullmatch(str(identity.get(key, ""))):
            raise PublicationAdmissionError(f"Admission {key} is invalid.")

    for key in ("workflowRunId", "candidateRunAttempt", "originalPublicationAttempt"):
        number = identity.get(key)
        if not isinstance(number, str) or not re.fullmatch(r"[1-9][0-9]*", number):
            raise PublicationAdmissionError(f"Admission {key} is invalid.")

    if int(identity["candidateRunAttempt"]) > int(identity["originalPublicationAttempt"]):
        raise PublicationAdmissionError("Admission precedes its qualification attempt.")

    if not re.fullmatch(r"\.github/workflows/[A-Za-z0-9_.-]+\.ya?ml", str(identity.get("workflowPath", ""))):
        raise PublicationAdmissionError("Admission workflow path is invalid.")

    require_timestamp(value.get("admittedUtc"))
    authority = value.get("authority")
    if (
        not isinstance(authority, dict)
        or not SHA1.fullmatch(str(authority.get("controlCommit", "")))
        or not SHA256.fullmatch(str(authority.get("authorizationDigest", "")))
        or not isinstance(authority.get("authorizationRevision"), str)
    ):
        raise PublicationAdmissionError("Admission original authority is invalid.")

    return value


def validate_bound_publication(
    admission: dict[str, Any],
    authority: dict[str, Any],
    *,
    repository: str,
    source_ref: str,
    source_commit: str,
    version: str,
    tag: str,
    candidate_receipt_sha256: str | None = None,
    manifest_sha256: str | None = None,
) -> None:
    """Reject changed identities or current grants that no longer allow writes."""
    validate_admission(admission)
    validate_authority(authority)
    identity = admission["identity"]
    expected = {
        "repository": repository, "sourceRef": source_ref, "sourceCommit": source_commit,
        "releaseVersion": version, "releaseTag": tag,
    }

    if candidate_receipt_sha256 is not None:
        expected["candidateReceiptSha256"] = candidate_receipt_sha256

    if manifest_sha256 is not None:
        expected["manifestSha256"] = manifest_sha256

    if any(identity.get(key) != value for key, value in expected.items()) or authority["repository"] != repository:
        raise PublicationAdmissionError("Publication disagrees with its immutable admission identity.")

    try:
        line = lines.select_line(authority["policy"], source_ref, version, purpose="publication")
    except lines.LinePolicyError as exception:
        raise PublicationAdmissionError(str(exception)) from exception

    if line["workflowPath"] != identity["workflowPath"]:
        raise PublicationAdmissionError("Publication entry point is no longer authorized.")


def require_lock(environment: dict[str, str] | None = None) -> None:
    """Require the trusted workflow's fixed shared-publication-lock context."""
    context = os.environ if environment is None else environment
    if (
        context.get("GITHUB_ACTIONS") != "true"
        or context.get("DOKA_PUBLICATION_LOCK_GROUP") != PUBLICATION_LOCK
        or context.get("DOKA_PUBLICATION_LOCK_HELD") != "1"
    ):
        raise PublicationAdmissionError("Publication requires the shared doka-publication workflow lock.")


def require_hosted_identity(identity: dict[str, Any], environment: dict[str, str] | None = None) -> None:
    """Bind an admission to its original run and nondecreasing retry chain."""
    context = os.environ if environment is None else environment
    require_lock(context)
    expected = {
        "GITHUB_REPOSITORY": identity["repository"], "GITHUB_REF": identity["sourceRef"],
        "GITHUB_SHA": identity["sourceCommit"], "GITHUB_RUN_ID": identity["workflowRunId"],
        "GITHUB_WORKFLOW_REF": f"{identity['repository']}/{identity['workflowPath']}@{identity['sourceRef']}",
    }

    if any(context.get(key) != value for key, value in expected.items()):
        raise PublicationAdmissionError("Publication is not executing in the admitted same-run workflow context.")

    attempt = context.get("GITHUB_RUN_ATTEMPT", "")
    if not re.fullmatch(r"[1-9][0-9]*", attempt) or int(attempt) < int(identity["originalPublicationAttempt"]):
        raise PublicationAdmissionError("Publication retry attempt is invalid.")


def candidate_identity(candidate_root: Path, candidate_receipt: Path, original_attempt: str) -> dict[str, Any]:
    """Bind the sealed manifest, frozen line, and exact candidate receipt bytes."""
    receipt = read_json(candidate_receipt)
    manifest_path = candidate_root / "release-candidate-evidence.json"
    manifest = read_json(manifest_path)
    snapshot = receipt.get("releaseLine") or manifest.get("releaseLine")
    if not isinstance(snapshot, dict):
        raise PublicationAdmissionError("Candidate does not contain a frozen release-line identity.")

    version = receipt.get("releaseVersion")
    try:
        lines.validate_snapshot(
            snapshot, repository=receipt.get("repository"), source_ref=receipt.get("sourceRef"), version=version
        )
    except lines.LinePolicyError as exception:
        raise PublicationAdmissionError(str(exception)) from exception

    source = manifest.get("source") or {}
    if (
        source.get("commit") != receipt.get("sourceCommit")
        or source.get("ref") != receipt.get("sourceRef")
        or manifest.get("releaseVersion") != version
        or manifest.get("expectedReleaseTag") != receipt.get("expectedReleaseTag")
    ):
        raise PublicationAdmissionError("Candidate manifest and receipt identities disagree.")

    return {
        "repository": receipt.get("repository"), "sourceRef": receipt.get("sourceRef"),
        "sourceCommit": receipt.get("sourceCommit"), "releaseVersion": version,
        "releaseTag": receipt.get("expectedReleaseTag"), "workflowPath": snapshot.get("workflowPath"),
        "workflowRunId": str(receipt.get("candidateRunId", "")),
        "candidateRunAttempt": str(receipt.get("candidateRunAttempt", "")),
        "originalPublicationAttempt": original_attempt,
        "manifestSha256": artifacts.sha256(manifest_path),
        "candidateReceiptSha256": artifacts.sha256(candidate_receipt),
        "releaseLineSha256": canonical_digest(snapshot),
    }


def create_admission(identity: dict[str, Any], authority: dict[str, Any]) -> dict[str, Any]:
    """Create a new admission only while its exact line is currently active."""
    validate_authority(authority)

    admission = {
        "schemaVersion": 1, "kind": ADMISSION_KIND,
        "admissionId": canonical_digest(identity), "identity": identity,
        "admittedUtc": utc_now(),
        "authority": {key: authority[key] for key in ("controlCommit", "authorizationRevision", "authorizationDigest")},
    }

    validate_bound_publication(
        admission, authority, repository=identity["repository"], source_ref=identity["sourceRef"],
        source_commit=identity["sourceCommit"], version=identity["releaseVersion"], tag=identity["releaseTag"],
    )

    return admission


def restore_admission(
    repository: str,
    run_id: str,
    source_commit: str,
    current_attempt: int,
    client: GitHubClient,
) -> tuple[dict[str, Any], dict[str, bytes]] | None:
    """Restore exact admission bytes from the original run before any retry."""
    inventory = client.pages(f"/repos/{repository}/actions/runs/{run_id}/artifacts", "artifacts")

    restored: tuple[dict[str, Any], dict[str, bytes]] | None = None
    for entry in inventory:
        match = ADMISSION_ARTIFACT.fullmatch(str(entry.get("name", "")))
        if match is None:
            continue

        attempt = int(match[1])
        run = entry.get("workflow_run") or {}
        if attempt > current_attempt or str(run.get("id")) != run_id or run.get("head_sha") != source_commit:
            raise PublicationAdmissionError("Admission artifact belongs to another run or attempt chain.")

        if entry.get("expired") is not False:
            raise PublicationAdmissionError(
                "Prior publication admission evidence expired; refusing a replacement admission."
            )

        artifact_id = entry.get("id")
        if isinstance(artifact_id, bool) or not isinstance(artifact_id, int) or artifact_id <= 0:
            raise PublicationAdmissionError("Admission artifact ID is invalid.")

        archive = client.download(f"/repos/{repository}/actions/artifacts/{artifact_id}/zip")
        if f"sha256:{hashlib.sha256(archive).hexdigest()}" != entry.get("digest"):
            raise PublicationAdmissionError("Admission artifact archive digest is invalid.")

        try:
            with zipfile.ZipFile(io.BytesIO(archive)) as package:
                members = package.infolist()
                names = [str(artifacts.validate_archive_member(member)) for member in members]
                required = {"publication-admission.json", "authorization-observation.json"}
                allowed = required | {"release-publication-receipt.json"}
                if (
                    len(names) != len(set(names)) or not required.issubset(names)
                    or not set(names).issubset(allowed)
                    or any(member.file_size > 131072 for member in members)
                ):
                    raise PublicationAdmissionError(
                        "Admission archive must contain its exact bounded receipt and initial authority."
                    )

                contents = {name: package.read(name) for name in names}
                value = json.loads(contents["publication-admission.json"])
        except (ValueError, zipfile.BadZipFile, artifacts.ArtifactResolutionError) as exception:
            raise PublicationAdmissionError("Admission artifact archive is invalid.") from exception

        validate_admission(value)
        initial_authority = json.loads(contents["authorization-observation.json"])
        validate_authority(initial_authority)
        original_authority = {
            key: initial_authority[key]
            for key in ("controlCommit", "authorizationRevision", "authorizationDigest")
        }

        if value["authority"] != original_authority:
            raise PublicationAdmissionError("Admission archive replaced the initial authorization observation.")

        identity = value["identity"]
        if (
            identity["repository"] != repository or identity["workflowRunId"] != run_id
            or identity["sourceCommit"] != source_commit
            or int(identity["originalPublicationAttempt"]) > attempt
        ):
            raise PublicationAdmissionError("Admission artifact payload disagrees with its hosted identity.")

        if restored is not None:
            for name in set(restored[1]) & set(contents):
                if restored[1][name] != contents[name]:
                    raise PublicationAdmissionError("Prior attempts contain conflicting immutable admission bytes.")

            contents = {**restored[1], **contents}

        restored = value, contents

    return restored


def prove_no_prior_admission(
    repository: str, run_id: str, current_attempt: int, client: GitHubClient
) -> None:
    """Require exact earlier attempts to prove publication never started.

    WHY: a deleted or expired admission is not permission to mint replacement
    candidate bytes. This is ordinary same-run recovery, not a handoff inventory.
    """
    for attempt in range(1, current_attempt):
        previous = client.request(f"/repos/{repository}/actions/runs/{run_id}/attempts/{attempt}")
        if (
            not isinstance(previous, dict) or str(previous.get("id")) != run_id
            or previous.get("run_attempt") != attempt or previous.get("status") != "completed"
        ):
            raise PublicationAdmissionError("Earlier publication attempt is incomplete or ambiguous.")

        jobs = client.pages(f"/repos/{repository}/actions/runs/{run_id}/attempts/{attempt}/jobs?filter=all", "jobs")
        if not jobs:
            raise PublicationAdmissionError("Earlier attempt has no retained job history proving non-admission.")

        for job in jobs:
            steps = job.get("steps")
            if str(job.get("run_id")) != run_id or not isinstance(steps, list) or any(
                not isinstance(step, dict) for step in steps
            ):
                raise PublicationAdmissionError("Earlier publication job identity or step history is unavailable.")

            boundaries = [
                [step for step in steps if step.get("name") == name]
                for name in (ADMISSION_STEP_NAME, PUBLISHER_STEP_NAME)
            ]
            if not any(boundaries):
                if "publi" in str(job.get("name", "")).casefold() and job.get("conclusion") != "skipped":
                    raise PublicationAdmissionError("Earlier publisher has no unambiguous admission boundary.")

                continue

            # WHY: The fixed workflow exposes upload and publication separately.
            # A skipped publisher cannot excuse an upload that may have frozen
            # admission. Partial or duplicate history cannot prove either absent.
            if any(len(matches) != 1 for matches in boundaries):
                raise PublicationAdmissionError(
                    "Earlier publisher boundary evidence is missing or ambiguous; refusing replacement identity."
                )

            # WHY: an ended attempt summary is not proof that its publisher
            # job ended. A waiting/running job cannot mint a replacement identity.
            if job.get("status") != "completed" and job.get("conclusion") != "skipped":
                raise PublicationAdmissionError(
                    "Prior publisher is unfinished; refusing replacement identity."
                )

            for matches in boundaries:
                step = matches[0]
                # WHY: GitHub can timestamp skipped steps before evaluating
                # their condition; a timestamp alone is not proof of execution.
                never_started = step.get("started_at") is None and step.get("status") in {"pending", "queued"}
                if step.get("conclusion") != "skipped" and not never_started:
                    raise PublicationAdmissionError(
                        "Prior admission upload or publisher started but its admission archive is missing; "
                        "refusing replacement identity."
                    )


def append_outputs(path: Path | None, values: dict[str, str]) -> None:
    """Append single-line trusted outputs for downstream workflow scheduling."""
    if path is None:
        return

    if any("\n" in value or "\r" in value for value in values.values()):
        raise PublicationAdmissionError("Workflow outputs must be single-line values.")

    with path.open("a", encoding="ascii") as output:
        for key, value in values.items():
            output.write(f"{key}={value}\n")


def preflight(args: argparse.Namespace) -> None:
    """Resolve current exact-ref authority before scheduling protected jobs."""
    client = GitHubClient()
    authority = fetch_current_authority(args.repository, client)
    try:
        line = lines.select_line(authority["policy"], args.source_ref, args.version, purpose="qualification")
        snapshot = lines.freeze_line(
            authority["policy"], args.source_ref, args.version, workflow_path=args.workflow_path
        )
        toolchain = lines.inspect_toolchain(Path.cwd(), snapshot)
    except lines.LinePolicyError as exception:
        raise PublicationAdmissionError(str(exception)) from exception

    branch_name = args.source_ref.removeprefix("refs/heads/")
    branch = client.request(f"/repos/{args.repository}/branches/{quote(branch_name, safe='')}")
    commit = (branch.get("commit") or {}).get("sha")
    if branch.get("protected") is not True or not SHA1.fullmatch(str(commit)):
        raise PublicationAdmissionError("Qualification requires an exact protected source branch.")

    if os.environ.get("GITHUB_ACTIONS") == "true":
        expected = {
            "GITHUB_REPOSITORY": args.repository, "GITHUB_REF": args.source_ref,
            "GITHUB_SHA": commit,
            "GITHUB_WORKFLOW_REF": f"{args.repository}/{args.workflow_path}@{args.source_ref}",
        }

        if any(os.environ.get(key) != value for key, value in expected.items()):
            raise PublicationAdmissionError(
                "Hosted qualification must execute the exact current authorized source head and caller workflow."
            )

    publish_allowed = line["state"] == "active"
    if args.publish:
        if not publish_allowed:
            raise PublicationAdmissionError("Qualification-only lines cannot schedule a publisher.")

        validate_environment(args.repository, line, authority["policy"], client)

    write_json(args.output, snapshot)
    write_json(args.policy_output, authority["policy"])
    write_json(args.authority_output, authority)
    append_outputs(args.github_output, {"publish_allowed": str(args.publish and publish_allowed).lower()})
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        # WHY: these facts precede qualification. Do not call the candidate
        # qualified or ask for a signing key before the evidence lanes finish.
        facts = {
            "Line": f"{line['role']} / {line['packageMajor']}.x",
            "Source ref": args.source_ref, "Source commit": commit,
            "Version": args.version, "Expected tag": f"v{args.version}",
            "SDK": toolchain["approvedDotnetSdk"], "Target framework": toolchain["targetFramework"],
            "EF Core policy": toolchain["efCoreVersion"], "Test runner": toolchain["testRunner"],
            "Upstream stage": toolchain["upstreamStage"],
        }

        with Path(summary).open("a", encoding="utf-8") as stream:
            stream.write("## Release preflight\n\nPreflight only; this candidate is not yet qualified.\n\n")
            for name, value in facts.items():
                stream.write(f"- {name}: <code>{html.escape(str(value))}</code>\n")

            next_action = (
                "Qualification only: no publisher environment, credentials or release tag is requested."
                if not args.publish else
                "After qualification succeeds, create the personally signed tag on this exact commit and approve the protected publisher."
            )
            stream.write(f"\n{next_action}\n")


def admit(args: argparse.Namespace) -> None:
    """Restore same-run admission, or create it while authority is active."""
    require_lock()
    client = GitHubClient()
    identity = candidate_identity(args.candidate_root, args.candidate_receipt, os.environ.get("GITHUB_RUN_ATTEMPT", ""))
    restored = restore_admission(
        args.repository, identity["workflowRunId"], identity["sourceCommit"],
        int(identity["originalPublicationAttempt"]), client,
    )
    authority = fetch_current_authority(args.repository, client)
    if restored is None:
        prove_no_prior_admission(
            args.repository, identity["workflowRunId"], int(identity["originalPublicationAttempt"]), client
        )
        admission = create_admission(identity, authority)
        require_hosted_identity(identity)
        write_json(args.output, admission)
        write_json(args.observation_output, authority)
    else:
        admission, contents = restored
        identity["originalPublicationAttempt"] = admission["identity"]["originalPublicationAttempt"]
        if identity != admission["identity"]:
            raise PublicationAdmissionError("Retry would replace the originally admitted candidate bytes.")

        require_hosted_identity(identity)
        destinations = {
            "publication-admission.json": args.output,
            "authorization-observation.json": args.observation_output,
            "release-publication-receipt.json": args.output.parent / "release-publication-receipt.json",
        }

        for name, raw in contents.items():
            destination = destinations[name]
            destination.parent.mkdir(parents=True, exist_ok=True)
            if destination.is_symlink():
                raise PublicationAdmissionError("Admission output must not be a symlink.")

            destination.write_bytes(raw)

    validate_bound_publication(
        admission, authority, repository=args.repository, source_ref=identity["sourceRef"],
        source_commit=identity["sourceCommit"], version=identity["releaseVersion"], tag=identity["releaseTag"],
    )
    line = lines.select_line(
        authority["policy"], identity["sourceRef"], identity["releaseVersion"], purpose="publication"
    )
    validate_environment(args.repository, line, authority["policy"], client)


def recheck(args: argparse.Namespace) -> None:
    """Reload current authority inside the lock immediately before a boundary."""
    admission = read_json(args.admission)
    validate_admission(admission)
    identity = admission["identity"]
    require_hosted_identity(identity)
    candidate = candidate_identity(args.candidate_root, args.candidate_receipt, identity["originalPublicationAttempt"])
    if candidate != identity:
        raise PublicationAdmissionError("Publication candidate bytes changed after admission.")

    client = GitHubClient()
    authority = fetch_current_authority(args.repository, client)
    validate_bound_publication(
        admission, authority, repository=args.repository, source_ref=identity["sourceRef"],
        source_commit=identity["sourceCommit"], version=identity["releaseVersion"], tag=identity["releaseTag"],
    )
    line = lines.select_line(
        authority["policy"], identity["sourceRef"], identity["releaseVersion"], purpose="publication"
    )
    validate_environment(args.repository, line, authority["policy"], client)
    write_json(args.observation_output, authority)


def build_parser() -> argparse.ArgumentParser:
    """Build the ordinary qualification, admission and boundary-recheck commands."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    before = commands.add_parser("preflight")
    before.add_argument("--repository", required=True)
    before.add_argument("--source-ref", required=True)
    before.add_argument("--version", required=True)
    before.add_argument("--workflow-path", required=True)
    before.add_argument("--output", type=Path, required=True)
    before.add_argument("--policy-output", type=Path, required=True)
    before.add_argument("--authority-output", type=Path, required=True)
    before.add_argument("--github-output", type=Path)
    before.add_argument("--publish", action="store_true")
    before.set_defaults(function=preflight)
    for name, handler in (("admit", admit), ("recheck", recheck)):
        command = commands.add_parser(name)
        command.add_argument("--repository", required=True)
        command.add_argument("--candidate-root", type=Path, required=True)
        command.add_argument("--candidate-receipt", type=Path, required=True)
        command.add_argument(
            "--observation-output", "--output" if name == "recheck" else "--authority-output",
            dest="observation_output", type=Path, required=True,
        )
        if name == "admit":
            command.add_argument("--output", type=Path, required=True)
        else:
            command.add_argument("--admission", type=Path, required=True)

        command.set_defaults(function=handler)

    return parser


def main() -> int:
    """Run one fail-closed publication admission command."""
    args = build_parser().parse_args()
    try:
        args.function(args)
    except (PublicationAdmissionError, lines.LinePolicyError, OSError, ValueError) as exception:
        print(f"Publication admission failed: {exception}", file=sys.stderr)

        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
