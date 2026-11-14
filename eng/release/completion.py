"""Complete an existing release using retained original evidence and current authority.

The control run is never the candidate run. Both identities are observed from
GitHub independently; original source is read as Git data, never executed.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
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

from eng.release import artifacts, github, lines, nuget, provenance, publication, trust


WORKFLOW = ".github/workflows/release-candidate.yml"
PREPARATION_KIND = "release-completion-preparation"
COMPLETION_KIND = "release-completion"
POSITIVE = re.compile(r"[1-9][0-9]*\Z")


class CompletionError(RuntimeError):
    """Indicate that retained evidence does not authorize completion."""


def positive(value: Any, label: str) -> int:
    """Require a real positive integer or its canonical decimal representation."""
    if isinstance(value, bool) or not POSITIVE.fullmatch(str(value)):
        raise CompletionError(f"{label} must be a positive numeric identity.")

    return int(value)


def checked_process(arguments: list[str]) -> subprocess.CompletedProcess[str]:
    """Require the external verifier to finish successfully within a bound."""
    try:
        result = subprocess.run(arguments, capture_output=True, text=True, check=False, timeout=180)
    except (OSError, subprocess.TimeoutExpired) as exception:
        raise CompletionError(f"Required verifier could not finish: {arguments[0]}") from exception

    if result.returncode != 0:
        raise CompletionError(f"Required verifier failed: {arguments[0]} (exit {result.returncode}).")

    return result


def read_git_file(client: publication.GitHubClient, repository: str, commit: str, path: str) -> bytes:
    """Read one exact regular Git blob, checking its object identity."""
    if not publication.SHA1.fullmatch(commit) or any(part in ("", ".", "..") for part in path.split("/")):
        raise CompletionError("Git source identity is invalid.")

    entry = client.request(f"/repos/{repository}/contents/{path}?ref={commit}")
    if (
        not isinstance(entry, dict) or entry.get("type") != "file" or entry.get("path") != path
        or entry.get("encoding") != "base64" or not publication.SHA1.fullmatch(str(entry.get("sha", "")))
        or not isinstance(entry.get("content"), str)
    ):
        raise CompletionError(f"Original Git data is missing or not regular: {path}")

    try:
        raw = base64.b64decode("".join(entry["content"].split()), validate=True)
    except (ValueError, TypeError) as exception:
        raise CompletionError("Git blob encoding is invalid.") from exception

    if hashlib.sha1(f"blob {len(raw)}\0".encode("ascii") + raw).hexdigest() != entry["sha"]:
        raise CompletionError("Git blob bytes disagree with their object identity.")

    return raw


def preserve_bytes(path: Path, raw: bytes) -> None:
    """Restore immutable bytes without following links or replacing conflicts."""
    if path.is_symlink() or any(parent.is_symlink() for parent in path.parents):
        raise CompletionError("Completion evidence must not traverse a symlink.")

    if path.exists():
        if not path.is_file() or path.read_bytes() != raw:
            raise CompletionError(f"Conflicting retained evidence: {path.name}")

        return

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(raw)


def validate_run(value: Any, repository: str, run_id: int, *, completed: bool) -> dict[str, Any]:
    """Require GitHub's actual dispatch identity, not caller-supplied labels."""
    if (
        not isinstance(value, dict) or type(value.get("id")) is not int or value.get("id") != run_id
        or type(value.get("run_attempt")) is not int
        or (value.get("repository") or {}).get("full_name") != repository
        or (value.get("head_repository") or {}).get("full_name") != repository
        or value.get("event") != "workflow_dispatch" or value.get("path") != WORKFLOW
        or not publication.SHA1.fullmatch(str(value.get("head_sha", "")))
        or not isinstance(value.get("head_branch"), str) or not value["head_branch"]
        or (completed and value.get("status") != "completed")
    ):
        raise CompletionError("GitHub workflow run identity is invalid.")

    positive(value.get("run_attempt"), "Workflow attempt")
    if completed and value.get("conclusion") not in ("success", "failure", "cancelled", "timed_out"):
        raise CompletionError("Original release run has no final outcome.")

    return {
        key: value[key] for key in ("id", "run_attempt", "head_sha", "head_branch", "event", "path")
    } | ({"conclusion": value["conclusion"]} if completed else {})


def current_control(repository: str, original_id: int, client: publication.GitHubClient) -> tuple[dict, dict]:
    """Authenticate the real protected-main control run and shared publisher lock."""
    publication.require_lock()
    run_id = positive(os.environ.get("GITHUB_RUN_ID"), "Control run")
    attempt = positive(os.environ.get("GITHUB_RUN_ATTEMPT"), "Control attempt")
    authority = publication.fetch_current_authority(repository, client)
    expected = {
        "GITHUB_REPOSITORY": repository, "GITHUB_REF": "refs/heads/main",
        "GITHUB_SHA": authority["controlCommit"], "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_WORKFLOW_REF": f"{repository}/{WORKFLOW}@refs/heads/main",
        "GITHUB_JOB": "complete",
    }

    if run_id == original_id or any(os.environ.get(key) != value for key, value in expected.items()):
        raise CompletionError("Completion must run independently at current protected main.")

    raw = client.request(f"/repos/{repository}/actions/runs/{run_id}/attempts/{attempt}")
    identity = validate_run(raw, repository, run_id, completed=False)
    if (
        identity["run_attempt"] != attempt or identity["head_sha"] != authority["controlCommit"]
        or identity["head_branch"] != "main" or raw.get("status") != "in_progress"
    ):
        raise CompletionError("The control API identity differs from the executing workflow.")

    jobs = client.pages(f"/repos/{repository}/actions/runs/{run_id}/attempts/{attempt}/jobs", "jobs")
    selected = [job for job in jobs if job.get("name") == "complete"]
    if (
        len(selected) != 1 or type(selected[0].get("id")) is not int or selected[0]["id"] <= 0
        or selected[0].get("run_id") != run_id or selected[0].get("run_attempt") != attempt
        or selected[0].get("status") != "in_progress"
    ):
        raise CompletionError("Completion requires its actual in-progress protected control job.")

    return identity, authority


def job_blocks(source: str) -> dict[str, str]:
    """Read the narrow declared job surface without executing original YAML."""
    jobs = re.search(r"(?m)^jobs:\s*$", source)
    if jobs is None:
        raise CompletionError("Original workflow declares no job contract.")

    matches = list(re.finditer(r"(?m)^  ([A-Za-z0-9_-]+):\s*$", source[jobs.end():]))
    result: dict[str, str] = {}
    tail = source[jobs.end():]
    for index, match in enumerate(matches):
        name = match[1]
        if name in result:
            raise CompletionError("Original workflow repeats a job identity.")

        end = matches[index + 1].start() if index + 1 < len(matches) else len(tail)
        result[name] = tail[match.end():end]

    return result


def qualification_names(source: str, policy: dict[str, Any]) -> set[str]:
    """Resolve all original qualifier cells from its retained source and policy.

    WHY: the supported workflow has two bounded matrix shapes. Unknown shapes
    fail closed instead of guessing cells or executing old producer scripts.
    """
    blocks = job_blocks(source)
    required = blocks.get("required", "")
    needs = re.search(r"(?m)^    needs:\s*\n((?:      - [A-Za-z0-9_-]+\s*\n)+)", required)
    if needs is None or set(re.findall(r"- ([A-Za-z0-9_-]+)", needs[1])) != set(blocks) - {"required"}:
        raise CompletionError("Original qualification aggregate does not cover every declared job.")

    names: set[str] = set()
    for job_id, block in blocks.items():
        explicit = re.search(r"(?m)^    name: ([^\n]+)$", block)
        name = explicit[1].strip().strip('"\'') if explicit else job_id
        if job_id == "spec-test-suite":
            if name != "spec-test-suite (${{ matrix.engine.name }})":
                raise CompletionError("Original specification matrix shape is unsupported.")

            cells = re.findall(r"(?m)^          - \{ name: ([A-Za-z0-9_-]+), target: \"([A-Za-z0-9_-]+)\" \}$", block)
            if not cells or len(set(cells)) != len(cells) or any(a != b for a, b in cells):
                raise CompletionError("Original specification matrix is ambiguous.")

            expanded = [f"spec-test-suite ({cell[0]})" for cell in cells]
        elif job_id == "engine-contracts":
            if name != "Engine contract (${{ matrix.stage }})" or "stage: ${{ fromJSON(needs.foundation.outputs.engine_stages) }}" not in block:
                raise CompletionError("Original engine matrix shape is unsupported.")

            gates = [gate["id"] for gate in policy.get("gates", []) if gate.get("kind") == "candidate-produced"]
            stages = ["runtime" if gate == "runtime-posture" else gate for gate in gates]
            if (
                not {"runtime", "migration-deployment", "mysqlconnector-patch-matrix"}.issubset(stages)
                or set(stages) - {"runtime", "migration-deployment", "mysqlconnector-patch-matrix", "efcore-patch-matrix"}
                or len(set(stages)) != len(stages)
            ):
                raise CompletionError("Original engine gate inventory is unsupported.")

            expanded = [f"Engine contract ({stage})" for stage in stages]
        else:
            if "matrix:" in block or "${{" in name:
                raise CompletionError("Original qualification adds an unsupported matrix shape.")

            expanded = [name]

        for value in expanded:
            full = f"qualification / {value}"
            if full in names:
                raise CompletionError("Original qualifier names are ambiguous.")

            names.add(full)

    if "qualification / Complete qualification" not in names:
        raise CompletionError("Original qualifier has no complete aggregate.")

    return names


def original_jobs(client: publication.GitHubClient, repository: str, run: dict, through: int) -> list[dict]:
    """Read each admitted attempt explicitly; later reruns cannot replace proof."""
    records: list[dict] = []
    seen: set[int] = set()
    for attempt in range(1, through + 1):
        endpoint = f"/repos/{repository}/actions/runs/{run['id']}/attempts/{attempt}"
        value = client.request(endpoint)
        observed = validate_run(value, repository, run["id"], completed=True)
        if observed["run_attempt"] != attempt or any(observed[key] != run[key] for key in ("head_sha", "head_branch", "event", "path")):
            raise CompletionError("Original attempt identity was replaced.")

        jobs = client.pages(endpoint + "/jobs", "jobs")
        for job in jobs:
            job_id = positive(job.get("id"), "Original job")
            if job_id in seen or job.get("run_id") != run["id"] or job.get("run_attempt") != attempt:
                raise CompletionError("Original job identity is duplicated or belongs to another attempt.")

            seen.add(job_id)
            records.append(job)

    return records


def successful_job(jobs: list[dict], name: str, through: int, *, exact: int | None = None) -> dict:
    """Require an actual successful producer at its retained original attempt."""
    selected = [job for job in jobs if job.get("name") == name and job["run_attempt"] <= through and (exact is None or job["run_attempt"] == exact)]
    if not selected:
        raise CompletionError(f"Original required producer is missing: {name}")

    attempt = max(job["run_attempt"] for job in selected)
    candidates = [job for job in selected if job["run_attempt"] == attempt]
    if len(candidates) != 1:
        raise CompletionError(f"Original producer is ambiguous: {name}")

    job = candidates[0]
    if job.get("status") != "completed" or job.get("conclusion") != "success":
        raise CompletionError(f"Original required producer did not succeed: {name}")

    if not any(step.get("status") == "completed" and step.get("conclusion") == "success" for step in job.get("steps", [])):
        raise CompletionError(f"Original producer has no executed successful steps: {name}")

    return {key: job[key] for key in ("id", "run_id", "run_attempt", "name", "status", "conclusion")}


def artifact_record(entry: dict, run: dict) -> dict:
    """Validate an unexpired artifact and freeze its API and archive identities."""
    artifact_id = positive(entry.get("id"), "Original artifact")
    if (
        type(entry.get("id")) is not int or entry.get("expired") is not False
        or (entry.get("workflow_run") or {}).get("id") != run["id"]
        or (entry.get("workflow_run") or {}).get("head_sha") != run["head_sha"]
        or not re.fullmatch(r"sha256:[0-9a-f]{64}", str(entry.get("digest", "")))
    ):
        raise CompletionError("Original artifact is expired, foreign, or has no archive digest.")

    expiry = entry.get("expires_at")
    try:
        timestamp = datetime.fromisoformat(str(expiry).replace("Z", "+00:00"))
        if timestamp.tzinfo is None or timestamp <= datetime.now(timezone.utc):
            raise ValueError("expired")
    except ValueError as exception:
        raise CompletionError("Original artifact expiry cannot authorize restoration.") from exception

    return {
        "id": artifact_id, "name": entry["name"], "digest": entry["digest"], "expiresAt": expiry,
        "runId": run["id"], "sourceCommit": run["head_sha"],
    }


def select_artifact(inventory: list[dict], name: str, run: dict) -> dict:
    """Require exactly one retained artifact with the admitted exact name."""
    matches = [entry for entry in inventory if entry.get("name") == name]
    if len(matches) != 1:
        raise CompletionError(f"Original artifact is missing or ambiguous: {name}")

    return artifact_record(matches[0], run)


def restore_artifact(client: publication.GitHubClient, repository: str, record: dict, destination: Path, allowed_roots: set[str]) -> None:
    """Digest-check and safely merge one exact archive in its canonical layout."""
    if destination.is_symlink() or any(parent.is_symlink() for parent in destination.parents):
        raise CompletionError("Original restoration must not traverse a substituted directory.")

    raw = client.download(f"/repos/{repository}/actions/artifacts/{record['id']}/zip")
    digest = record["digest"].removeprefix("sha256:")
    if hashlib.sha256(raw).hexdigest() != digest:
        raise CompletionError("Original artifact archive bytes changed.")

    try:
        with zipfile.ZipFile(io.BytesIO(raw)) as package:
            members = package.infolist()
            if not members or any(str(artifacts.validate_archive_member(member)).split("/")[0] not in allowed_roots for member in members):
                raise CompletionError("Original artifact contains an unexpected layout.")
    except zipfile.BadZipFile as exception:
        raise CompletionError("Original artifact is not a valid ZIP archive.") from exception

    archive_path = destination.parent / "archives" / f"{record['id']}.zip"
    preserve_bytes(archive_path, raw)
    artifacts.restore_archive(archive_path, destination, digest)


def file_inventory(root: Path) -> dict[str, str]:
    """Bind every restored file and reject substituted links or special files."""
    result: dict[str, str] = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or (not path.is_file() and not path.is_dir()):
            raise CompletionError("Original evidence contains a nonregular entry.")

        if path.is_file():
            result[path.relative_to(root).as_posix()] = artifacts.sha256(path)

    return result


def verify_tag_and_qualification(client: publication.GitHubClient, repository: str, admission: dict, authority: dict, root: Path, policy: dict) -> dict:
    """Verify old PR proof and signed tag against freshly read current signers."""
    identity = admission["identity"]
    commit = identity["sourceCommit"]
    tag = identity["releaseTag"]
    source_branch = identity["sourceRef"].removeprefix("refs/heads/")
    compared = client.request(f"/repos/{repository}/compare/{commit}...{quote(source_branch, safe='')}")
    if (
        (compared.get("base_commit") or {}).get("sha") != commit
        or (compared.get("merge_base_commit") or {}).get("sha") != commit
        or compared.get("status") not in ("ahead", "identical")
    ):
        raise CompletionError("Original source is no longer reachable on its authorized branch.")

    ref = client.request(f"/repos/{repository}/git/ref/tags/{quote(tag, safe='')}")
    target = ref.get("object") or {}
    tag_sha = target.get("sha", "")
    if target.get("type") != "tag" or not publication.SHA1.fullmatch(str(tag_sha)):
        raise CompletionError("Original release tag is not an annotated tag.")

    tag_object = client.request(f"/repos/{repository}/git/tags/{tag_sha}")
    target_object = tag_object.get("object") or {}
    if tag_object.get("sha") != tag_sha or tag_object.get("tag") != tag or target_object.get("type") != "commit" or target_object.get("sha") != commit:
        raise CompletionError("Original annotated tag points at another source.")

    trust.verify_api_signature(tag_object.get("verification"))
    current_policy_raw = read_git_file(client, repository, authority["controlCommit"], "eng/release/evidence-policy.json")
    current_policy = json.loads(current_policy_raw)
    signers = current_policy.get("trustedTagSigners") or {}
    configured = signers.get("allowedSignersFile")
    if configured != ".github/allowed_signers":
        raise CompletionError("Current signer policy has no supported allowed-signers identity.")

    signer_path = root.parent / "current-allowed-signers"
    preserve_bytes(signer_path, read_git_file(client, repository, authority["controlCommit"], configured))
    repo = Path(os.environ["GITHUB_WORKSPACE"])
    checked_process(["git", "-C", str(repo), "fetch", "--no-tags", "origin", tag_sha])
    local = trust.verify_local_signature(repo, tag_sha, allowed_signers=signer_path, signers=signers.get("signers") or [])
    commit_data = client.request(f"/repos/{repository}/git/commits/{commit}")
    tree = (commit_data.get("tree") or {}).get("sha", "")
    if commit_data.get("sha") != commit or not publication.SHA1.fullmatch(str(tree)):
        raise CompletionError("Original source tree identity is unavailable.")

    qualification_manifest = publication.read_json(root / "release-candidate" / f"github-{identity['workflowRunId']}" / "release-qualification-manifest.json")
    frozen = trust.fetch_frozen_qualification_receipt(
        repository, manifest=qualification_manifest, policy=policy, commit=commit,
        tree_id=tree, expected_release_tag=tag,
    )

    return {"tagObject": tag_sha, "sourceTree": tree, "signer": local["observed"], "qualification": frozen}


def release_snapshot(plan: dict, client: github.ReleaseClient) -> dict:
    """Require complete existing assets; preparation never creates or repairs them."""
    value = client.get_release(plan["repository"], plan["releaseTag"])
    if value is None:
        raise CompletionError("Completion requires an existing GitHub release.")

    release_id = positive(value.get("id"), "Existing release")
    github.verify_release_metadata(value, plan)
    if github.verify_release_assets(value, plan, client):
        raise CompletionError("Existing release assets are incomplete; completion cannot upload them.")

    if type(value.get("immutable")) is not bool or (value["draft"] is True and value["immutable"] is True):
        raise CompletionError("Existing release mutability is contradictory.")

    return {
        "id": release_id,
        "assets": sorted([
            {key: asset.get(key) for key in ("id", "name", "size", "digest", "state")}
            for asset in value["assets"]
        ], key=lambda asset: asset["name"]),
    }


def observe_original(repository: str, version: str, original_id: int, output: Path, client: publication.GitHubClient, release_client: github.ReleaseClient) -> tuple[dict, dict, dict]:
    """Restore and independently revalidate the exact original admitted evidence."""
    control, authority = current_control(repository, original_id, client)
    run = validate_run(client.request(f"/repos/{repository}/actions/runs/{original_id}"), repository, original_id, completed=True)
    restored = publication.restore_admission(repository, str(original_id), run["head_sha"], run["run_attempt"], client)
    if restored is None:
        raise CompletionError("Original publication admission is missing; completion cannot mint one.")

    admission, admission_contents = restored
    identity = admission["identity"]
    if identity["releaseVersion"] != version or identity["sourceRef"] != f"refs/heads/{run['head_branch']}" or identity["workflowPath"] != WORKFLOW:
        raise CompletionError("Original admission differs from the requested release run.")

    publication.validate_bound_publication(
        admission, authority, repository=repository, source_ref=identity["sourceRef"], source_commit=run["head_sha"],
        version=version, tag=f"v{version}",
    )
    line = lines.select_line(authority["policy"], identity["sourceRef"], version, purpose="publication")
    publication.validate_environment(repository, line, authority["policy"], client)
    root = output / "original"
    candidate = root / "release-candidate" / f"github-{original_id}"
    evidence = root / "release-publication"
    checkpoints = root / "release-candidate-checkpoints" / f"github-{original_id}"
    inventory = client.pages(f"/repos/{repository}/actions/runs/{original_id}/artifacts", "artifacts")
    if len({entry.get("id") for entry in inventory}) != len(inventory):
        raise CompletionError("Original artifact IDs are duplicated.")

    candidate_attempt = int(identity["candidateRunAttempt"])
    admitted_attempt = int(identity["originalPublicationAttempt"])
    publication_pattern = re.compile(re.escape(f"release-publication-{version}-attempt-") + r"([1-9][0-9]*)\Z")
    publication_attempts = [int(match[1]) for entry in inventory if (match := publication_pattern.fullmatch(str(entry.get("name", ""))))]
    if not publication_attempts or max(publication_attempts) > run["run_attempt"]:
        raise CompletionError("Original publication artifact has no valid retained attempt.")

    publication_attempt = max(publication_attempts)
    if publication_attempt < admitted_attempt:
        raise CompletionError("Publication evidence predates its original admission.")

    records = [
        select_artifact(inventory, f"release-candidate-{original_id}-attempt-{candidate_attempt}", run),
        select_artifact(inventory, f"release-checkpoints-{original_id}-attempt-{candidate_attempt}", run),
        select_artifact(inventory, f"release-publication-{version}-attempt-{publication_attempt}", run),
    ]

    restore_artifact(client, repository, records[0], root, {"release-candidate", "release-publication"})
    restore_artifact(client, repository, records[1], root / "release-candidate-checkpoints", {f"github-{original_id}"})
    restore_artifact(client, repository, records[2], evidence, {name for name in github.STAGED_EVIDENCE_FILES} | {
        "publication-admission.json", "authorization-observation.json", "current-authorization-observation.json",
        "publication-preflight.json", "nuget-publication-readback.json", "nuget-signature-verification.txt",
        "github-release-staged-plan.json", "github-release-draft.json", "github-release-readback.json", "release-publication-completion.json",
        "packages", "nuget-restore-verification.json", "public-restore", "release-provenance.intoto.jsonl",
    })
    for name, raw in admission_contents.items():
        preserve_bytes(evidence / name, raw)

    receipt = publication.read_json(evidence / "release-publication-receipt.json")
    nuget.validate_portable_receipt(receipt, candidate)
    nuget.validate_publication_authority(receipt, candidate, admission=admission, authority=authority)
    if artifacts.sha256(evidence / "candidate-receipt.json") != identity["candidateReceiptSha256"] or artifacts.sha256(candidate / "release-candidate-evidence.json") != identity["manifestSha256"]:
        raise CompletionError("Restored candidate differs from its immutable admission.")

    source_files = {
        "CHANGELOG.md": read_git_file(client, repository, run["head_sha"], "CHANGELOG.md"),
        "evidence-policy.json": read_git_file(client, repository, run["head_sha"], "eng/release/evidence-policy.json"),
        "quality-gates.yml": read_git_file(client, repository, run["head_sha"], ".github/workflows/quality-gates.yml"),
        "release-candidate.yml": read_git_file(client, repository, run["head_sha"], WORKFLOW),
    }

    for name, raw in source_files.items():
        preserve_bytes(root / "source" / name, raw)

    policy = json.loads(source_files["evidence-policy.json"])
    source_jobs = job_blocks(source_files["release-candidate.yml"].decode("utf-8"))
    if "uses: $/.github/workflows/quality-gates.yml" not in source_jobs.get("qualification", ""):
        raise CompletionError("Original release does not call the retained qualification workflow.")

    jobs = original_jobs(client, repository, run, run["run_attempt"])
    producers = [successful_job(jobs, "preflight", candidate_attempt)]
    producers.extend(successful_job(jobs, name, candidate_attempt) for name in sorted(qualification_names(source_files["quality-gates.yml"].decode("utf-8"), policy)))
    producers.append(successful_job(jobs, "assemble", candidate_attempt, exact=candidate_attempt))
    attest = successful_job(jobs, "attest", admitted_attempt)
    if attest["run_attempt"] < candidate_attempt:
        raise CompletionError("Attestation predates the frozen candidate.")

    producers.append(attest)
    publisher = [job for job in jobs if job.get("name") == "publish" and job["run_attempt"] == admitted_attempt]
    if len(publisher) != 1 or not any(step.get("name") == publication.ADMISSION_STEP_NAME and step.get("conclusion") == "success" and step.get("status") == "completed" for step in publisher[0].get("steps", [])):
        raise CompletionError("Original publication admission has no successful hosted upload.")

    provenance_record = select_artifact(inventory, f"release-provenance-{original_id}-attempt-{attest['run_attempt']}", run)
    records.append(provenance_record)
    restore_artifact(client, repository, provenance_record, evidence, {provenance.PORTABLE_PROVENANCE_NAME})
    # WHY: admission bytes remain exact across retries; every retained archive
    # that contributed them is frozen too, not just its convenient merged copy.
    records.extend(artifact_record(entry, run) for entry in inventory if publication.ADMISSION_ARTIFACT.fullmatch(str(entry.get("name", ""))))
    manifest = publication.read_json(candidate / "release-candidate-evidence.json")
    subjects = provenance.release_subjects(candidate, evidence, checkpoints)
    bundle_path = evidence / provenance.PORTABLE_PROVENANCE_NAME
    bundle = provenance.read_bundle(bundle_path, json_lines=True)
    provenance.validate_bundle(bundle, subjects, require_exact=True)
    provenance.validate_workflow_identity(bundle, manifest, signer_run_attempt=attest["run_attempt"], publication_run_attempt=admitted_attempt)
    for subject in subjects:
        checked_process([
            "gh", "attestation", "verify", str(subject), "--bundle", str(bundle_path), "--repo", repository,
            "--signer-workflow", f"{repository}/{WORKFLOW}", "--signer-digest", run["head_sha"],
            "--source-ref", identity["sourceRef"], "--source-digest", run["head_sha"], "--deny-self-hosted-runners",
        ])

    trusted = verify_tag_and_qualification(client, repository, admission, authority, root, policy)
    plan = github.build_restored_release_plan(repository, candidate, evidence, root / "source" / "CHANGELOG.md", attesting_attempt=attest["run_attempt"], publication_attempt=admitted_attempt)
    existing = release_snapshot(plan, release_client)
    frozen = {
        "repository": repository, "releaseVersion": version, "originalRun": run, "controlRun": control,
        "authority": {key: authority[key] for key in ("controlCommit", "authorizationDigest", "authorizationRevision")},
        "admission": admission, "artifacts": sorted(records, key=lambda record: record["id"]),
        "producers": sorted(producers, key=lambda job: (job["name"], job["run_attempt"])),
        "trust": trusted, "existingRelease": existing, "files": file_inventory(root),
        "planSha256": publication.canonical_digest(plan),
    }

    return frozen, plan, authority


def prepare(args: argparse.Namespace) -> dict:
    """Freeze independently authenticated original evidence for protected completion."""
    repository = publication.require_repository(args.repository)
    version = nuget.require_normalized_release_version(args.version)
    original_id = positive(args.original_run_id, "Original run")
    output = args.output_dir.resolve()
    frozen, _, _ = observe_original(repository, version, original_id, output, publication.GitHubClient(), github.GitHubCliClient())
    result = {"schemaVersion": 1, "kind": PREPARATION_KIND, "preparedUtc": publication.utc_now(), **frozen}
    path = output / "prepared.json"
    publication.write_json(path, result)

    return {"preparedReceipt": str(path), "sha256": artifacts.sha256(path)}


def public_readback(plan: dict, output: Path) -> dict:
    """Read all public signed primaries and exact PDBs without uploading anything."""
    readback_root = output / "public-readback"
    file_inventory(readback_root)
    if readback_root.is_symlink() or any(parent.is_symlink() for parent in readback_root.parents):
        raise CompletionError("Public readback must not traverse a substituted directory.")

    receipt, candidate = github.plan_publication_identity(plan)
    paths = {asset["name"]: Path(asset["path"]) for asset in plan["assets"]}
    symbols = nuget.validated_symbol_entries(publication.read_json(paths["symbol-readback-manifest.json"]), receipt["releaseVersion"])
    args = argparse.Namespace(
        candidate_root=candidate, timeout_seconds=3600, request_timeout_seconds=30, poll_interval_seconds=30,
        output_dir=output / "public-readback" / "packages", output=output / "public-readback" / "nuget-publication-readback.json",
    )

    nuget.readback_payloads(args, receipt, symbols)
    readback = publication.read_json(args.output)
    github.require_release_identity(readback, "Public completion readback", receipt, "verifiedUtc")
    github.validate_observation_set(readback, receipt, {entry["packageId"]: entry for entry in symbols}, require_matching=True)
    github.validate_public_readback_files(args.output_dir.parent, readback, receipt)
    verified = []
    for role in nuget.PACKAGE_IDENTITIES:
        path = Path(readback["packages"][role]["readbackPath"])
        result = checked_process(["dotnet", "nuget", "verify", str(path), "--all"])
        verification_path = output / "signature-verification" / f"{role}.txt"
        preserve_bytes(verification_path, (result.stdout + result.stderr).encode("utf-8"))
        verified.append({
            "role": role, "sha256": artifacts.sha256(path),
            "verificationPath": str(verification_path), "verificationSha256": artifacts.sha256(verification_path),
        })

    return {
        "receiptSha256": artifacts.sha256(args.output), "verifiedPackages": verified,
        "files": file_inventory(args.output_dir.parent),
    }


def complete(args: argparse.Namespace) -> dict:
    """Recheck frozen original proof and public bytes before the only release write."""
    path = args.receipt.resolve()
    if not publication.SHA256.fullmatch(args.receipt_sha256) or artifacts.sha256(path) != args.receipt_sha256:
        raise CompletionError("Preparation receipt checksum changed.")

    prepared = publication.read_json(path)
    if prepared.get("schemaVersion") != 1 or prepared.get("kind") != PREPARATION_KIND:
        raise CompletionError("Preparation receipt schema is invalid.")

    output = args.output_dir.resolve()
    if path != output / "prepared.json":
        raise CompletionError("Preparation must be completed in its original output layout.")

    expected = {key: value for key, value in prepared.items() if key not in ("schemaVersion", "kind", "preparedUtc")}
    client = publication.GitHubClient()
    release_client = github.GitHubCliClient()
    frozen, plan, _ = observe_original(prepared["repository"], prepared["releaseVersion"], positive(prepared["originalRun"]["id"], "Original run"), output, client, release_client)
    if frozen != expected:
        raise CompletionError("Original or control evidence changed after preparation.")

    readback = public_readback(plan, output)
    # WHY: public indexing can take time. Re-observe authority, original proof,
    # tag, and assets after that wait and directly before numeric-ID publication.
    final, plan, authority = observe_original(prepared["repository"], prepared["releaseVersion"], prepared["originalRun"]["id"], output, client, release_client)
    if final != expected or artifacts.sha256(path) != args.receipt_sha256:
        raise CompletionError("Completion authorization changed before publication.")

    if file_inventory(output / "public-readback") != readback["files"] or any(
        artifacts.sha256(Path(entry["verificationPath"])) != entry["verificationSha256"]
        for entry in readback["verifiedPackages"]
    ):
        raise CompletionError("Public verification bytes changed before publication.")

    release = github.complete_existing_release(plan, release_client, prepared["existingRelease"]["id"], authority=authority)
    result = {
        "schemaVersion": 1, "kind": COMPLETION_KIND, "completedUtc": publication.utc_now(),
        "preparationSha256": args.receipt_sha256, "repository": prepared["repository"],
        "releaseVersion": prepared["releaseVersion"], "originalRun": prepared["originalRun"],
        "controlRun": prepared["controlRun"], "originalArtifacts": prepared["artifacts"],
        "originalRunUrl": f"https://github.com/{prepared['repository']}/actions/runs/{prepared['originalRun']['id']}",
        "controlRunUrl": f"https://github.com/{prepared['repository']}/actions/runs/{prepared['controlRun']['id']}",
        "publicationAdmissionSha256": publication.canonical_digest(prepared["admission"]),
        "publicReadback": readback, "releaseReadback": release,
    }

    publication.write_json(output / "completion.json", result)

    return result


def build_parser() -> argparse.ArgumentParser:
    """Expose only retained-evidence preparation and existing-release completion."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    preparation = commands.add_parser("prepare")
    preparation.add_argument("--repository", required=True)
    preparation.add_argument("--version", required=True)
    preparation.add_argument("--original-run-id", required=True)
    preparation.add_argument("--output-dir", type=Path, required=True)
    finalization = commands.add_parser("complete")
    finalization.add_argument("--receipt", type=Path, required=True)
    finalization.add_argument("--receipt-sha256", required=True)
    finalization.add_argument("--output-dir", type=Path, required=True)

    return parser


def main(argv: list[str] | None = None) -> int:
    """Run the authenticated completion path and expose failures as nonzero exits."""
    args = build_parser().parse_args(argv)
    try:
        result = prepare(args) if args.command == "prepare" else complete(args)
    except (CompletionError, publication.PublicationAdmissionError, artifacts.ArtifactResolutionError,
            github.GitHubReleaseError, nuget.PublicationError, provenance.ProvenanceError,
            trust.TrustRootError, lines.LinePolicyError, OSError, ValueError, KeyError, TypeError) as exception:
        print(f"Release completion failed: {exception}", file=sys.stderr)

        return 1

    print(json.dumps(result, sort_keys=True, ensure_ascii=True))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
