#!/usr/bin/env python3
"""Dispatch branch-native maintenance workflows and await exact producers.

GitHub runs schedules on the default branch. Dispatching the other approved
branches instead of checking them out inside main's jobs preserves their own
SDK, test runner and workflow contract during a major-version transition.
"""

from __future__ import annotations

import argparse
import base64
import concurrent.futures
import json
import os
import re
import subprocess
import sys
import tempfile
import threading
import time
from functools import partial
from pathlib import Path
from typing import Any, Callable
from urllib.parse import quote

if not __package__:  # pragma: no cover - direct CLI execution
    sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from eng.release import lines


SECURITY_WORKFLOW = "security-audit.yml"
SECURITY_PRODUCER = "dependency-vulnerability-audit"
WORKFLOWS = ("ci.yml", "container-matrix.yml", "benchmark.yml", SECURITY_WORKFLOW)
SHA = re.compile(r"[0-9a-f]{40}\Z")
CORRELATION = re.compile(r"schedule-[1-9][0-9]*-[1-9][0-9]*\Z")
CONTRACT = re.compile(r"^# release-line-schedule: (\{[^\n]+\})$", re.MULTILINE)
Api = Callable[..., Any]


class ScheduleError(RuntimeError):
    """A maintenance producer cannot be proven successful and commit-exact."""


def github_api(endpoint: str, payload: dict | None = None) -> Any:
    """Use the job-scoped token without exposing it in command arguments."""
    command = ["gh", "api", "--method", "POST" if payload is not None else "GET",
               "-H", "X-GitHub-Api-Version: 2026-03-10", endpoint]

    if payload is not None:
        command.extend(["--input", "-"])

    result = subprocess.run(
        command, input=json.dumps(payload) if payload is not None else None,
        capture_output=True, text=True, check=False, timeout=60)

    if result.returncode:
        raise ScheduleError(f"GitHub request failed for {endpoint}: {result.stderr.strip()}")

    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise ScheduleError("GitHub did not return the required JSON identity.") from error


def scheduled_lines(policy: dict) -> list[dict]:
    """Select active exact refs only; the native default schedule runs once."""
    return [line for line in policy["lines"]
            if line["state"] == "active" and line["sourceRef"] != policy["authorityRef"]]


def producer_contract(workflow: str, *, workflow_name: str | None = None) -> list[str]:
    """Read a small JSON contract from the target's own workflow version."""
    matches = CONTRACT.findall(workflow)
    if len(matches) != 1:
        raise ScheduleError("Target workflow has no unique branch-native producer contract.")

    try:
        contract = json.loads(matches[0])
    except json.JSONDecodeError as error:
        raise ScheduleError("Target workflow producer contract is malformed.") from error

    if not isinstance(contract, dict):
        raise ScheduleError("Target workflow producer contract is not an object.")

    producers = contract.get("producers")
    if (type(contract.get("schemaVersion")) is not int
            or contract["schemaVersion"] != 1 or not isinstance(producers, list)
            or not producers
            or any(not isinstance(name, str) or not re.fullmatch(r"[a-z][a-z0-9-]*", name)
                   for name in producers)
            or len(set(producers)) != len(producers)):
        raise ScheduleError("Target workflow producer contract is invalid.")

    # WHY: A green replacement job must not silently stand in for the security
    # audit. Older branch workflows must add the producer before activation.
    if workflow_name == SECURITY_WORKFLOW and producers != [SECURITY_PRODUCER]:
        raise ScheduleError("Target security workflow must declare its dependency vulnerability audit producer.")

    # WHY: YAML formatting is not producer evidence. The dispatch endpoint
    # validates its inputs; exact child identity and actual successful jobs are
    # checked after execution, including the required identity guard.

    return ["schedule-identity", *producers]


def _require_identity(run: dict, run_id: int, workflow: str, branch: str,
                      commit: str, correlation: str) -> None:
    """Reject branch races and wrong producers before reading their verdict."""
    if (run.get("id") != run_id or run.get("event") != "workflow_dispatch"
            or run.get("path", "").split("@", 1)[0] != f".github/workflows/{workflow}"
            or run.get("head_branch") != branch or run.get("head_sha") != commit
            or run.get("display_title") != f"maintenance {correlation}"):
        raise ScheduleError("Scheduled child run identity does not match the dispatched producer.")


def _verify_producers(api: Api, repository: str, run_id: int,
                      producers: list[str]) -> None:
    """Require every matrix row of each declared producer, with pagination."""
    jobs: list[dict] = []
    page = 1
    while True:
        response = api(f"repos/{repository}/actions/runs/{run_id}/jobs?per_page=100&page={page}")
        entries = response.get("jobs")
        if not isinstance(entries, list):
            raise ScheduleError("Scheduled child producer job listing is missing.")

        jobs.extend(entries)
        if len(jobs) >= response.get("total_count", len(jobs)):
            break

        if not entries or page >= 100:
            raise ScheduleError("Scheduled child producer job listing is incomplete.")

        page += 1

    for producer in producers:
        matches = [job for job in jobs if job.get("name") == producer
                   or job.get("name", "").startswith(f"{producer} (")]
        if not matches or any(job.get("conclusion") != "success" for job in matches):
            raise ScheduleError(f"Scheduled producer '{producer}' is missing, skipped or unsuccessful.")


def wait_for_child(api: Api, repository: str, run_id: int, workflow: str,
                   branch: str, commit: str, correlation: str,
                   producers: list[str], *, timeout: int = 12600,
                   poll_interval: int = 15,
                   on_progress: Callable[[dict], None] | None = None) -> dict:
    """Bound queue plus execution time and retain observed run identity."""
    deadline = time.monotonic() + timeout
    while True:
        run = api(f"repos/{repository}/actions/runs/{run_id}")
        if on_progress:
            on_progress({"observedRunId": run.get("id"), "headSha": run.get("head_sha"),
                         "observedSourceRef": f"refs/heads/{run.get('head_branch')}",
                         "observedWorkflowPath": run.get("path"), "status": run.get("status"),
                         "conclusion": run.get("conclusion")})

        _require_identity(run, run_id, workflow, branch, commit, correlation)
        if run.get("status") == "completed":
            if run.get("conclusion") != "success":
                raise ScheduleError(
                    f"Scheduled child {run_id} ended with {run.get('conclusion')!r}; "
                    "cancellation or queue overflow is not successful qualification.")

            _verify_producers(api, repository, run_id, producers)

            return {"runId": run["id"], "headSha": run["head_sha"],
                    "sourceRef": f"refs/heads/{branch}", "workflow": workflow,
                    "conclusion": run["conclusion"], "correlation": correlation}

        if time.monotonic() >= deadline:
            raise ScheduleError(f"Scheduled child {run_id} exceeded its queue/execution timeout.")

        time.sleep(min(poll_interval, max(0, deadline - time.monotonic())))


def dispatch_line(api: Api, repository: str, workflow: str, source_ref: str,
                   correlation: str, *, timeout: int = 12600,
                   on_progress: Callable[[dict], None] | None = None) -> dict:
    """Retain exact dispatch identity even when its child cannot qualify."""
    observation = {"sourceRef": source_ref, "workflow": workflow,
                   "correlation": correlation, "verificationStatus": "pending"}

    def record(update: dict) -> None:
        """Publish snapshots so later updates cannot rewrite prior evidence."""
        observation.update(update)
        if on_progress:
            on_progress(dict(observation))

    record({})
    try:
        result = _dispatch_line(api, repository, workflow, source_ref,
                                correlation, timeout=timeout, record=record)
    except Exception as error:
        record({"verificationStatus": "failed", "failure": str(error)})
        raise

    result.update({"verificationStatus": "success", "status": "completed"})
    record(result)

    return result


def _dispatch_line(api: Api, repository: str, workflow: str, source_ref: str,
                    correlation: str, *, timeout: int,
                    record: Callable[[dict], None]) -> dict:
    """Resolve an exact head, dispatch its own YAML, then prove its outcome."""
    branch = source_ref.removeprefix("refs/heads/")
    head = api(f"repos/{repository}/git/ref/heads/{quote(branch, safe='')}")
    commit = head.get("object", {}).get("sha", "")
    if not SHA.fullmatch(commit):
        raise ScheduleError(f"Active scheduled branch {source_ref} is missing or invalid.")

    record({"expectedHeadSha": commit, "verificationStatus": "resolved"})
    content = api(f"repos/{repository}/contents/.github/workflows/{workflow}?ref={commit}")
    try:
        workflow_text = base64.b64decode(content["content"], validate=False).decode("utf-8")
    except (KeyError, ValueError, UnicodeDecodeError) as error:
        raise ScheduleError("Target branch workflow cannot be inspected.") from error

    producers = producer_contract(workflow_text, workflow_name=workflow)
    payload = {"ref": branch, "inputs": {"maintenance": True,
               "expected_commit": commit, "schedule_correlation": correlation}}

    if workflow == "benchmark.yml":
        payload["inputs"]["profile"] = "scorecard"

    response = api(f"repos/{repository}/actions/workflows/{workflow}/dispatches", payload)
    run_id = response.get("workflow_run_id") if isinstance(response, dict) else None
    if not isinstance(run_id, int) or isinstance(run_id, bool) or run_id <= 0:
        raise ScheduleError("Dispatch did not return its immutable workflow run ID.")

    # Persist the returned identity before polling: a cancelled coordinator or
    # a failed first observation must not erase the child that was dispatched.
    record({"runId": run_id, "verificationStatus": "dispatched"})

    return wait_for_child(api, repository, run_id, workflow, branch, commit,
                          correlation, producers, timeout=timeout, on_progress=record)


def _persist_report(output: Path | None, report: dict) -> None:
    """Atomically replace diagnostics so interrupted writes remain readable."""
    if output is None:
        return

    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
            mode="w", encoding="ascii", dir=output.parent,
            prefix=f".{output.name}.", delete=False) as temporary:
        json.dump(report, temporary, indent=2)
        temporary.write("\n")

    Path(temporary.name).replace(output)


def coordinate_lines(api: Api, policy: dict, workflow: str, correlation: str,
                       authority_commit: str, *, output: Path | None = None) -> dict:
    """Collect every bounded child outcome and retain failures before exiting."""
    targets = scheduled_lines(policy)
    report = {"schemaVersion": 1, "workflow": workflow,
              "authorityCommit": authority_commit, "conclusion": "in_progress",
              "children": [{"sourceRef": line["sourceRef"], "workflow": workflow,
                            "correlation": correlation, "verificationStatus": "pending"}
                           for line in targets]}

    lock = threading.Lock()

    def record(index: int, observation: dict) -> None:
        """Serialize sibling snapshots without losing their independent outcomes."""
        with lock:
            report["children"][index].update(observation)
            _persist_report(output, report)

    _persist_report(output, report)
    # Waits are independent and bounded. Collect all futures even after one
    # fails, otherwise a failed maintenance line hides preview's real outcome.
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
        children = [(index, executor.submit(
            dispatch_line, api, policy["repository"], workflow,
            line["sourceRef"], correlation, on_progress=partial(record, index)))
            for index, line in enumerate(targets)]

        for index, child in children:
            try:
                record(index, child.result())
            except Exception as error:
                record(index, {"verificationStatus": "failed", "failure": str(error)})

    report["conclusion"] = ("failure" if any(
        child["verificationStatus"] != "success" for child in report["children"]) else "success")
    _persist_report(output, report)

    return report


def validate_child(policy: dict, workflow: str, expected_commit: str,
                    correlation: str, environment: dict[str, str]) -> None:
    """Authorize maintenance intent against current authority, not input alone."""
    source_ref = environment.get("GITHUB_REF")
    if (environment.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
            or environment.get("GITHUB_REPOSITORY") != policy["repository"]
            or not SHA.fullmatch(expected_commit)
            or environment.get("GITHUB_SHA") != expected_commit
            or not CORRELATION.fullmatch(correlation)
            or environment.get("GITHUB_WORKFLOW_REF") !=
            f"{policy['repository']}/.github/workflows/{workflow}@{source_ref}"):
        raise ScheduleError("Maintenance child inputs do not match the actual hosted identity.")

    if source_ref not in {line["sourceRef"] for line in scheduled_lines(policy)}:
        raise ScheduleError("Maintenance child branch is not an active additional release line.")


def native_security_audit_allowed(policy: dict, environment: dict[str, str], *, authority_commit: str) -> bool:
    """Audit native default schedules only while that exact line is active.

    WHY: Additional active lines are dispatched independently; a temporarily
    inactive main must not run twice or hide their maintenance security work.
    """
    source_ref = policy["authorityRef"]
    if (environment.get("GITHUB_EVENT_NAME") != "schedule"
            or environment.get("GITHUB_REPOSITORY") != policy["repository"]
            or environment.get("GITHUB_REF") != source_ref
            or not SHA.fullmatch(environment.get("GITHUB_SHA", ""))
            or environment.get("GITHUB_WORKFLOW_REF") !=
            f"{policy['repository']}/.github/workflows/{SECURITY_WORKFLOW}@{source_ref}"):
        raise ScheduleError("Native security audit does not match the default-branch workflow identity.")

    # WHY: A queued old-main run must not audit its old framework graph
    # while borrowing the newly active major's current authorization.
    if not SHA.fullmatch(authority_commit) or environment["GITHUB_SHA"] != authority_commit:
        raise ScheduleError("Native security audit must match the current default-branch head.")

    configured = lines.select_line(policy, source_ref, purpose="inspect")

    return configured["state"] == "active"


def main(argv: list[str] | None = None) -> int:
    """Run the hosted coordinator or its branch-local identity guard."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("run", "validate-child", "validate-default"))
    parser.add_argument("--workflow", choices=WORKFLOWS, required=True)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--expected-commit", default="")
    parser.add_argument("--correlation", default="")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args(argv)
    try:
        # Resolve authority independently of the target branch's checkout. An
        # old maintenance policy must not reactivate an inactive line.
        from eng.release import publication

        authority = publication.fetch_current_authority(os.environ["GITHUB_REPOSITORY"])
        policy = authority["policy"]
        if args.command == "validate-child":
            validate_child(policy, args.workflow, args.expected_commit,
                           args.correlation, dict(os.environ))

            return 0

        if args.command == "validate-default":
            if args.workflow != SECURITY_WORKFLOW or args.github_output is None:
                raise ScheduleError("Native security validation requires its workflow and GitHub output file.")

            allowed = native_security_audit_allowed(
                policy, dict(os.environ), authority_commit=authority["controlCommit"])
            with args.github_output.open("a", encoding="ascii") as output:
                output.write(f"run_audit={str(allowed).lower()}\n")

            return 0

        if (os.environ.get("GITHUB_EVENT_NAME") != "schedule"
                or os.environ.get("GITHUB_REF") != policy["authorityRef"]
                or os.environ.get("GITHUB_REPOSITORY") != policy["repository"]):
            raise ScheduleError("Only the protected default-branch schedule can coordinate lines.")

        from eng.release import dependencies

        dependencies.validate_configuration(policy, (args.root / ".github/dependabot.yml").read_text())
        correlation = f"schedule-{os.environ['GITHUB_RUN_ID']}-{os.environ['GITHUB_RUN_ATTEMPT']}"
        result = coordinate_lines(github_api, policy, args.workflow, correlation,
                                  authority["controlCommit"], output=args.output)
        print(json.dumps(result))
        if result["conclusion"] != "success":
            print("Scheduled release-line verification failed; inspect child evidence.", file=sys.stderr)

            return 1

        return 0
    except (RuntimeError, lines.LinePolicyError, OSError, ValueError, KeyError) as error:
        print(f"Scheduled release-line verification failed: {error}", file=sys.stderr)

        return 1


if __name__ == "__main__":
    raise SystemExit(main())
