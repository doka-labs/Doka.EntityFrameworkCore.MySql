"""Verify exact scheduled child identity and fail-closed completion."""

from __future__ import annotations

import base64
import io
import json
import os
import tempfile
import threading
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

from eng.release import schedules
from eng.tests.test_release_lines import initial_policy, shared_policy


FIXTURES = Path(__file__).parent / "fixtures"


class ScheduledRunTests(unittest.TestCase):
    """Keep dispatch, workload completion and producer evidence inseparable."""

    def test_security_workflow_dispatch_uses_its_own_exact_audit_producer(self) -> None:
        """An additional active line audits its own source and SDK, not main's."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/security-audit.yml").read_bytes()
        run = {**child_run(), "path": ".github/workflows/security-audit.yml"}
        api = mock.Mock(side_effect=[{"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow).decode()}, {"workflow_run_id": 27},
            run, {"total_count": 2, "jobs": [
                {"name": "schedule-identity", "conclusion": "success"},
                {"name": "dependency-vulnerability-audit", "conclusion": "success"}]}])

        # Act
        result = schedules.dispatch_line(
            api, "owner/repo", "security-audit.yml", "refs/heads/maintenance/10.x",
            "schedule-1-1", timeout=10)

        # Assert
        self.assertEqual("success", result["verificationStatus"])
        self.assertEqual("security-audit.yml", result["workflow"])
        self.assertEqual({"ref": "maintenance/10.x", "inputs": {
            "maintenance": True, "expected_commit": "a" * 40,
            "schedule_correlation": "schedule-1-1"}}, api.call_args_list[2].args[1])

    def test_successful_workflow_without_security_audit_cannot_satisfy_the_schedule(self) -> None:
        """A removed or skipped audit is not covered by a green identity job."""
        # Arrange
        run = {**child_run(), "path": ".github/workflows/security-audit.yml"}
        api = mock.Mock(side_effect=[run, {"total_count": 1, "jobs": [
            {"name": "schedule-identity", "conclusion": "success"}]}])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "dependency-vulnerability-audit") as failure:
            schedules.wait_for_child(
                api, "owner/repo", 27, "security-audit.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", [schedules.SECURITY_PRODUCER], timeout=10)

        # Assert
        self.assertIn("missing, skipped or unsuccessful", str(failure.exception))

    def test_security_dispatch_rejects_a_replacement_producer_before_dispatch(self) -> None:
        """Self-declaration cannot replace an audit with an unrelated green job."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/security-audit.yml").read_text().replace(
                        "dependency-vulnerability-audit", "unrelated")
        api = mock.Mock(side_effect=[{"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow.encode()).decode()}])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "security workflow") as failure:
            schedules.dispatch_line(
                api, "owner/repo", "security-audit.yml", "refs/heads/maintenance/10.x",
                "schedule-1-1", timeout=10)

        # Assert
        self.assertIn("vulnerability audit producer", str(failure.exception))
        self.assertEqual(2, api.call_count)

    def test_completed_exact_child_returns_observed_identity(self) -> None:
        """Retain the dispatched ID and actual commit, not caller assertions."""
        # Arrange
        run = child_run()
        api = mock.Mock(side_effect=[run, {"total_count": 1, "jobs": [
            {"name": "compatibility-matrix", "conclusion": "success"}]}])

        # Act
        result = schedules.wait_for_child(
            api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
            "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        self.assertEqual(27, result["runId"])
        self.assertEqual("a" * 40, result["headSha"])
        self.assertEqual("success", result["conclusion"])

    def test_success_without_expected_producer_is_rejected(self) -> None:
        """A skipped or removed workload must not look like qualification."""
        # Arrange
        api = mock.Mock(side_effect=[child_run(), {"total_count": 0, "jobs": []}])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "producer") as failure:
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        self.assertIn("compatibility-matrix", str(failure.exception))

    def test_different_commit_is_rejected_before_accepting_success(self) -> None:
        """A branch move between resolution and dispatch cannot pass unnoticed."""
        # Arrange
        api = mock.Mock(return_value={**child_run(), "head_sha": "b" * 40})

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "identity"):
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        api.assert_called_once()

    def test_cancelled_child_is_a_visible_failure(self) -> None:
        """Cancellation, including queue replacement, never becomes success."""
        # Arrange
        api = mock.Mock(return_value={**child_run(), "conclusion": "cancelled"})

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "cancelled"):
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        api.assert_called_once()

    def test_queued_child_times_out_without_reporting_success(self) -> None:
        """Waiting for capacity is bounded just like execution."""
        # Arrange
        api = mock.Mock(return_value={**child_run(), "status": "queued", "conclusion": None})

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "timeout"):
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=0)

        # Assert
        api.assert_called_once()

    def test_wrong_workflow_is_rejected(self) -> None:
        """A successful unrelated workflow cannot satisfy the schedule."""
        # Arrange
        api = mock.Mock(return_value={**child_run(), "path": ".github/workflows/ci.yml"})

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "identity"):
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        api.assert_called_once()

    def test_wrong_correlation_is_rejected(self) -> None:
        """Another coordinator attempt cannot lend its green result."""
        # Arrange
        api = mock.Mock(return_value={**child_run(), "display_title": "maintenance schedule-2-1"})

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "identity"):
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        api.assert_called_once()

    def test_skipped_producer_is_rejected(self) -> None:
        """GitHub's successful workflow verdict does not imply a job ran."""
        # Arrange
        api = mock.Mock(side_effect=[child_run(), {"total_count": 1, "jobs": [
            {"name": "compatibility-matrix", "conclusion": "skipped"}]}])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "skipped"):
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        self.assertEqual(2, api.call_count)

    def test_failed_producer_is_rejected_even_if_the_workflow_is_successful(self) -> None:
        """A failed continue-on-error workload cannot satisfy producer evidence."""
        # Arrange
        api = mock.Mock(side_effect=[child_run(), {"total_count": 1, "jobs": [
            {"name": "compatibility-matrix", "conclusion": "failure"}]}])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "unsuccessful") as failure:
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        self.assertIn("compatibility-matrix", str(failure.exception))
        self.assertEqual(2, api.call_count)

    def test_dispatch_requires_the_actual_identity_guard_job(self) -> None:
        """Removing the textual job scan must not remove the executed identity guard."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/container-matrix.yml").read_bytes()
        api = mock.Mock(side_effect=[
            {"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow).decode("ascii")},
            {"workflow_run_id": 27},
            child_run(),
            {"total_count": 1, "jobs": [{"name": "compatibility-matrix", "conclusion": "success"}]},
        ])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "schedule-identity") as failure:
            schedules.dispatch_line(
                api, "owner/repo", "container-matrix.yml", "refs/heads/maintenance/10.x",
                "schedule-1-1", timeout=10)

        # Assert
        self.assertIn("missing", str(failure.exception))
        self.assertEqual(5, api.call_count)

    def test_matrix_producers_across_pages_are_accepted(self) -> None:
        """Large matrices must not silently lose later producer rows."""
        # Arrange
        first = {"total_count": 2, "jobs": [
            {"name": "compatibility-matrix (mysql84)", "conclusion": "success"}]}
        second = {"total_count": 2, "jobs": [
            {"name": "compatibility-matrix (mariadb118)", "conclusion": "success"}]}
        api = mock.Mock(side_effect=[child_run(), first, second])

        # Act
        result = schedules.wait_for_child(
            api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
            "a" * 40, "schedule-1-1", ["compatibility-matrix"], timeout=10)

        # Assert
        self.assertEqual("success", result["conclusion"])
        self.assertIn("page=2", api.call_args.args[0])

    def test_dispatch_uses_target_workflow_and_exact_returned_id(self) -> None:
        """No main-defined commands or latest-run lookup cross the line boundary."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/container-matrix.yml").read_bytes()
        api = mock.Mock(side_effect=[{"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow).decode()}, {"workflow_run_id": 27},
            child_run(), {"total_count": 2, "jobs": [
                {"name": "schedule-identity", "conclusion": "success"},
                {"name": "compatibility-matrix", "conclusion": "success"}]}])

        # Act
        result = schedules.dispatch_line(
            api, "owner/repo", "container-matrix.yml", "refs/heads/maintenance/10.x",
            "schedule-1-1", timeout=10)

        # Assert
        self.assertEqual(27, result["runId"])
        self.assertEqual({"ref": "maintenance/10.x", "inputs": {
            "maintenance": True, "expected_commit": "a" * 40,
            "schedule_correlation": "schedule-1-1"}}, api.call_args_list[2].args[1])
        self.assertTrue(api.call_args_list[3].args[0].endswith("/runs/27"))

    def test_missing_branch_prevents_dispatch(self) -> None:
        """An active policy row cannot mask an absent source ref."""
        # Arrange
        api = mock.Mock(return_value={})

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "missing"):
            schedules.dispatch_line(
                api, "owner/repo", "container-matrix.yml", "refs/heads/maintenance/10.x",
                "schedule-1-1", timeout=10)

        # Assert
        api.assert_called_once()

    def test_missing_dispatch_id_prevents_run_selection(self) -> None:
        """An empty old API response must not fall back to a racing latest run."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/container-matrix.yml").read_bytes()
        api = mock.Mock(side_effect=[{"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow).decode()}, {}])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "immutable workflow run ID"):
            schedules.dispatch_line(
                api, "owner/repo", "container-matrix.yml", "refs/heads/maintenance/10.x",
                "schedule-1-1", timeout=10)

        # Assert
        self.assertEqual(3, api.call_count)

    def test_cancelled_dispatch_retains_actual_child_identity(self) -> None:
        """Failure evidence preserves the child that was actually cancelled."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/container-matrix.yml").read_bytes()
        api = mock.Mock(side_effect=[{"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow).decode()}, {"workflow_run_id": 27},
            {**child_run(), "conclusion": "cancelled"}])
        observations = []

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "cancelled"):
            schedules.dispatch_line(
                api, "owner/repo", "container-matrix.yml", "refs/heads/maintenance/10.x",
                "schedule-1-1", on_progress=observations.append)

        # Assert
        self.assertEqual(27, observations[-1]["runId"])
        self.assertEqual("a" * 40, observations[-1]["headSha"])
        self.assertEqual("cancelled", observations[-1]["conclusion"])
        self.assertEqual("failed", observations[-1]["verificationStatus"])

    def test_timed_out_dispatch_retains_queued_child_identity(self) -> None:
        """A bounded queue timeout is not reported as an undispatched branch."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/container-matrix.yml").read_bytes()
        api = mock.Mock(side_effect=[{"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow).decode()}, {"workflow_run_id": 27},
            {**child_run(), "status": "queued", "conclusion": None}])
        observations = []

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "timeout"):
            schedules.dispatch_line(
                api, "owner/repo", "container-matrix.yml", "refs/heads/maintenance/10.x",
                "schedule-1-1", timeout=0, on_progress=observations.append)

        # Assert
        self.assertEqual(27, observations[-1]["runId"])
        self.assertEqual("a" * 40, observations[-1]["headSha"])
        self.assertEqual("queued", observations[-1]["status"])
        self.assertEqual("failed", observations[-1]["verificationStatus"])

    def test_coordinator_persists_failure_and_collects_successful_sibling(self) -> None:
        """One failed child cannot suppress the other line's completed evidence."""
        # Arrange
        policy = active_policy()
        policy["lines"][2]["state"] = "active"
        def dispatch(api, repository, workflow, source_ref, correlation, *, on_progress):
            observation = {"sourceRef": source_ref, "runId": 27 if "maintenance" in source_ref else 28,
                           "headSha": "a" * 40, "status": "completed", "conclusion": "success"}
            on_progress(observation)
            if "maintenance" in source_ref:
                raise schedules.ScheduleError("Scheduled child 27 ended with 'cancelled'.")

            return {**observation, "verificationStatus": "success"}

        # Act
        with tempfile.TemporaryDirectory() as temporary, mock.patch.object(
                schedules, "dispatch_line", side_effect=dispatch):
            output = Path(temporary) / "schedule.json"
            result = schedules.coordinate_lines(
                mock.Mock(), policy, "container-matrix.yml", "schedule-1-1",
                "b" * 40, output=output)
            persisted = json.loads(output.read_text())

        # Assert
        self.assertEqual(result, persisted)
        self.assertEqual("failure", persisted["conclusion"])
        self.assertEqual([27, 28], [child["runId"] for child in persisted["children"]])
        self.assertEqual(["failed", "success"], [
            child["verificationStatus"] for child in persisted["children"]])
        self.assertIn("cancelled", persisted["children"][0]["failure"])

    def test_dispatched_identity_is_persisted_before_first_child_poll(self) -> None:
        """A failed first observation cannot erase an already dispatched run."""
        # Arrange
        workflow = (Path(__file__).resolve().parents[2]
                    / ".github/workflows/container-matrix.yml").read_bytes()
        snapshots = []
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "schedule.json"
            def api(endpoint, payload=None):
                if "/git/ref/" in endpoint:
                    return {"object": {"sha": "a" * 40}}

                if "/contents/" in endpoint:
                    return {"content": base64.b64encode(workflow).decode()}

                if endpoint.endswith("/dispatches"):
                    return {"workflow_run_id": 27}

                snapshots.append(json.loads(output.read_text()))

                return {**child_run(), "conclusion": "cancelled"}

            # Act
            result = schedules.coordinate_lines(
                api, active_policy(), "container-matrix.yml", "schedule-1-1",
                "b" * 40, output=output)
            persisted = json.loads(output.read_text())

        # Assert
        self.assertEqual("dispatched", snapshots[0]["children"][0]["verificationStatus"])
        self.assertEqual(27, snapshots[0]["children"][0]["runId"])
        self.assertEqual("a" * 40, snapshots[0]["children"][0]["expectedHeadSha"])
        self.assertEqual("failure", result["conclusion"])
        self.assertEqual("cancelled", persisted["children"][0]["conclusion"])

    def test_main_twelve_and_two_maintenance_lines_keep_their_native_producers(self) -> None:
        """Compose native selection and real child verification across runners."""
        # Arrange
        policy = three_major_policy()
        environment = {**native_security_environment(), "GITHUB_SHA": "c" * 40}
        api, calls = native_workflow_api()

        # Act
        native_allowed = schedules.native_security_audit_allowed(
            policy, environment, authority_commit="c" * 40)
        result = schedules.coordinate_lines(
            api, policy, "ci.yml", "schedule-1-1", "c" * 40)

        # Assert
        self.assertTrue(native_allowed)
        self.assertEqual("success", result["conclusion"])
        self.assertEqual([
            ("refs/heads/maintenance/10.x", 27, "a" * 40, "success"),
            ("refs/heads/maintenance/11.x", 28, "b" * 40, "success")], [
                (child["sourceRef"], child["runId"], child["headSha"],
                 child["verificationStatus"]) for child in result["children"]])
        repository = policy["repository"]
        self.assertEqual({
            f"repos/{repository}/contents/.github/workflows/ci.yml?ref={'a' * 40}",
            f"repos/{repository}/contents/.github/workflows/ci.yml?ref={'b' * 40}"}, {
                endpoint for endpoint, _ in calls if "/contents/" in endpoint})
        dispatches = [(endpoint, payload) for endpoint, payload in calls if payload is not None]
        self.assertEqual(2, len(dispatches))
        self.assertEqual({"maintenance/10.x", "maintenance/11.x"}, {
            payload["ref"] for _, payload in dispatches})
        self.assertTrue(all(endpoint == f"repos/{repository}/actions/workflows/ci.yml/dispatches"
                            for endpoint, _ in dispatches))
        self.assertEqual({(27, "a" * 40), (28, "b" * 40)}, {
            (27 if payload["ref"] == "maintenance/10.x" else 28,
             payload["inputs"]["expected_commit"]) for _, payload in dispatches})
        self.assertTrue(all(payload["inputs"] == {
            "maintenance": True,
            "expected_commit": "a" * 40 if payload["ref"] == "maintenance/10.x" else "b" * 40,
            "schedule_correlation": "schedule-1-1"} for _, payload in dispatches))
        self.assertEqual({
            f"repos/{repository}/actions/runs/27/jobs?per_page=100&page=1",
            f"repos/{repository}/actions/runs/28/jobs?per_page=100&page=1"}, {
                endpoint for endpoint, _ in calls if "/jobs?" in endpoint})
        self.assertFalse(any("preview" in endpoint
                             or endpoint == f"repos/{repository}/git/ref/heads/main"
                             for endpoint, _ in calls))

    def test_swapped_native_child_evidence_preserves_the_successful_other_line(self) -> None:
        """Wrong workflow, ref, run or runner evidence fails the combined result."""
        for mismatch in ("workflow", "branch", "commit", "run", "producer-contract"):
            with self.subTest(mismatch=mismatch):
                # Arrange
                policy = three_major_policy()
                api, calls = native_workflow_api(mismatch=mismatch)

                # Act
                with tempfile.TemporaryDirectory() as temporary:
                    output = Path(temporary) / "schedule.json"
                    result = schedules.coordinate_lines(
                        api, policy, "ci.yml", "schedule-1-1", "c" * 40, output=output)
                    persisted = json.loads(output.read_text(encoding="ascii"))

                # Assert
                self.assertEqual(result, persisted)
                self.assertEqual("failure", result["conclusion"])
                successful, rejected = result["children"]
                self.assertEqual("refs/heads/maintenance/10.x", successful["sourceRef"])
                self.assertEqual(27, successful["runId"])
                self.assertEqual("a" * 40, successful["headSha"])
                self.assertEqual("success", successful["verificationStatus"])
                self.assertEqual("refs/heads/maintenance/11.x", rejected["sourceRef"])
                self.assertEqual(28, rejected["runId"])
                self.assertEqual("b" * 40, rejected["expectedHeadSha"])
                self.assertEqual("failed", rejected["verificationStatus"])
                self.assertIn("producer" if mismatch == "producer-contract" else "identity",
                              rejected["failure"])
                self.assertEqual(27 if mismatch == "run" else 28, rejected["observedRunId"])
                self.assertEqual(2, sum(payload is not None for _, payload in calls))


class NativeSecurityAuditTests(unittest.TestCase):
    """Keep default-line audits and additional-line dispatch disjoint."""

    def test_active_default_line_is_audited_by_its_native_schedule(self) -> None:
        """The native job covers main exactly once without self-dispatch."""
        # Arrange
        policy = initial_policy()

        # Act
        allowed = schedules.native_security_audit_allowed(
            policy, native_security_environment(), authority_commit="a" * 40)

        # Assert
        self.assertTrue(allowed)
        self.assertNotIn("refs/heads/main", [line["sourceRef"] for line in schedules.scheduled_lines(policy)])

    def test_inactive_default_line_does_not_run_a_new_native_audit(self) -> None:
        """Deactivation stops its workload without disabling other active lines."""
        # Arrange
        policy = initial_policy()
        policy["lines"][0]["state"] = "inactive"

        # Act
        allowed = schedules.native_security_audit_allowed(
            policy, native_security_environment(), authority_commit="a" * 40)

        # Assert
        self.assertFalse(allowed)

    def test_prepared_default_line_does_not_run_an_active_line_schedule(self) -> None:
        """Preparation is an explicit manual qualification, not activation."""
        # Arrange
        policy = initial_policy()
        policy["lines"][0]["state"] = "prepared"

        # Act
        allowed = schedules.native_security_audit_allowed(
            policy, native_security_environment(), authority_commit="a" * 40)

        # Assert
        self.assertFalse(allowed)

    def test_forged_default_schedule_ref_is_rejected(self) -> None:
        """A feature checkout cannot borrow the native main schedule guard."""
        # Arrange
        policy = initial_policy()
        environment = {**native_security_environment(), "GITHUB_REF": "refs/heads/feature/test"}

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "workflow identity") as failure:
            schedules.native_security_audit_allowed(policy, environment, authority_commit="a" * 40)

        # Assert
        self.assertIn("default-branch", str(failure.exception))

    def test_wrong_workflow_cannot_supply_native_security_identity(self) -> None:
        """The default schedule guard is specific to the audit entry point."""
        # Arrange
        policy = initial_policy()
        environment = {**native_security_environment(), "GITHUB_WORKFLOW_REF":
            f"{policy['repository']}/.github/workflows/ci.yml@refs/heads/main"}

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "workflow identity") as failure:
            schedules.native_security_audit_allowed(policy, environment, authority_commit="a" * 40)

        # Assert
        self.assertIn("security audit", str(failure.exception))

    def test_two_maintenance_lines_and_main_twelve_keep_their_own_audits(self) -> None:
        """The active-line security coverage is not fixed to the first handoff."""
        # Arrange
        policy = shared_policy()
        policy["lines"][0]["packageMajor"] = 12
        policy["lines"][1]["state"] = "active"
        policy["lines"][2]["state"] = "inactive"
        policy["lines"].append({**policy["lines"][1],
            "sourceRef": "refs/heads/maintenance/11.x", "packageMajor": 11})

        # Act
        native_allowed = schedules.native_security_audit_allowed(
            policy, native_security_environment(), authority_commit="a" * 40)
        additional_refs = {line["sourceRef"] for line in schedules.scheduled_lines(policy)}

        # Assert
        self.assertTrue(native_allowed)
        self.assertEqual({"refs/heads/maintenance/10.x", "refs/heads/maintenance/11.x"}, additional_refs)

    def test_native_validation_cli_writes_the_consumed_job_output(self) -> None:
        """A true guard decision reaches the workflow's audit job condition."""
        # Arrange
        authority = {"policy": initial_policy(), "controlCommit": "a" * 40}
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"

            # Act
            with (mock.patch.dict(os.environ, native_security_environment(), clear=True), mock.patch(
                    "eng.release.publication.fetch_current_authority", return_value=authority)):
                status = schedules.main(["validate-default", "--workflow", "security-audit.yml",
                                         "--github-output", str(output)])

            # Assert
            self.assertEqual(0, status)
            self.assertEqual("run_audit=true\n", output.read_text())

    def test_stale_native_run_cannot_audit_a_new_main_line_as_current(self) -> None:
        """A queued old-main run must not borrow a later main head's active grant."""
        # Arrange
        authority = {"policy": initial_policy(), "controlCommit": "b" * 40}
        stderr = io.StringIO()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"

            # Act
            with (mock.patch.dict(os.environ, native_security_environment(), clear=True), mock.patch(
                    "eng.release.publication.fetch_current_authority", return_value=authority),
                    mock.patch("sys.stderr", stderr)):
                status = schedules.main(["validate-default", "--workflow", "security-audit.yml",
                                         "--github-output", str(output)])

            # Assert
            self.assertEqual(1, status)
            self.assertFalse(output.exists())
            self.assertIn("current default-branch head", stderr.getvalue())


class MaintenanceIdentityTests(unittest.TestCase):
    """Current active policy gates only maintenance, not ordinary feature CI."""

    def test_contract_accepts_equivalent_workflow_job_formatting(self) -> None:
        """Comments, quoted keys and indentation must not alter producer identity."""
        for style in ("comment", "quoted-key", "indentation"):
            with self.subTest(style=style):
                # Arrange
                original = (Path(__file__).resolve().parents[2]
                            / ".github/workflows/security-audit.yml").read_text(encoding="ascii")
                declaration = "  dependency-vulnerability-audit:\n"
                if style == "comment":
                    workflow = original.replace(declaration, "  dependency-vulnerability-audit: # unchanged\n")
                elif style == "quoted-key":
                    workflow = original.replace(declaration, '  "dependency-vulnerability-audit":\n')
                else:
                    header, _, jobs = original.partition("\njobs:\n")
                    workflow = header + "\njobs:\n" + "".join(
                        "  " + line if line.strip() else line for line in jobs.splitlines(keepends=True)
                    )


                # Act
                producers = schedules.producer_contract(workflow, workflow_name="security-audit.yml")

                # Assert
                self.assertEqual(["schedule-identity", "dependency-vulnerability-audit"], producers)

    def test_active_exact_child_is_authorized(self) -> None:
        """The dispatch inputs agree with independently observed hosted identity."""
        # Arrange
        policy = active_policy()
        environment = child_environment()

        # Act
        result = schedules.validate_child(
            policy, "container-matrix.yml", "a" * 40, "schedule-1-1", environment)

        # Assert
        self.assertIsNone(result)

    def test_inactive_child_is_rejected(self) -> None:
        """User input does not activate a prepared or inactive release branch."""
        # Arrange
        policy = active_policy()
        policy["lines"][1]["state"] = "inactive"

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "not an active") as failure:
            schedules.validate_child(
                policy, "container-matrix.yml", "a" * 40, "schedule-1-1", child_environment())

        # Assert
        self.assertIn("branch", str(failure.exception))

    def test_input_commit_different_from_hosted_commit_is_rejected(self) -> None:
        """A caller cannot assert a commit other than the actual workflow head."""
        # Arrange
        environment = {**child_environment(), "GITHUB_SHA": "b" * 40}

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "hosted identity") as failure:
            schedules.validate_child(
                active_policy(), "container-matrix.yml", "a" * 40, "schedule-1-1", environment)

        # Assert
        self.assertIn("inputs", str(failure.exception))

    def test_default_branch_is_not_dispatched_twice(self) -> None:
        """Main has a native schedule; inactive preview has none."""
        # Arrange
        policy = active_policy()

        # Act
        result = schedules.scheduled_lines(policy)

        # Assert
        self.assertEqual([policy["lines"][1]], result)

    def test_actual_child_rejects_a_declared_but_missing_producer(self) -> None:
        """A valid declaration cannot substitute for a job that actually ran."""
        # Arrange
        workflow = '# release-line-schedule: {"schemaVersion":1,"producers":["missing"]}\n'
        producers = schedules.producer_contract(workflow)
        api = mock.Mock(side_effect=[child_run(), {"total_count": 1, "jobs": [
            {"name": "schedule-identity", "conclusion": "success"}]}])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "missing") as failure:
            schedules.wait_for_child(
                api, "owner/repo", 27, "container-matrix.yml", "maintenance/10.x",
                "a" * 40, "schedule-1-1", producers, timeout=10)

        # Assert
        self.assertIn("producer", str(failure.exception))

    def test_contract_rejects_non_string_producer(self) -> None:
        """Malformed contract input must become a named validation failure."""
        # Arrange
        workflow = '# release-line-schedule: {"schemaVersion":1,"producers":[{}]}\n'

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "invalid") as failure:
            schedules.producer_contract(workflow)

        # Assert
        self.assertIn("producer contract", str(failure.exception))

    def test_contract_rejects_boolean_schema_version(self) -> None:
        """JSON true is not an integer schema revision despite Python equality."""
        # Arrange
        workflow = ('# release-line-schedule: {"schemaVersion":true,"producers":["work"]}\n'
                    'on:\n  workflow_dispatch:\n    inputs:\n      maintenance:\n'
                    '      expected_commit:\n      schedule_correlation:\n'
                    'jobs:\n  schedule-identity:\n  work:\n')

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "invalid") as failure:
            schedules.producer_contract(workflow)

        # Assert
        self.assertIn("producer contract", str(failure.exception))

    def test_dispatch_input_rejection_remains_a_visible_failure(self) -> None:
        """A workflow without input support fails at the actual dispatch boundary."""
        # Arrange
        workflow = ('# release-line-schedule: {"schemaVersion":1,"producers":["work"]}\n'
                    'jobs:\n  schedule-identity:\n  work:\n')

        api = mock.Mock(side_effect=[
            {"object": {"sha": "a" * 40}},
            {"content": base64.b64encode(workflow.encode("ascii")).decode("ascii")},
            schedules.ScheduleError("GitHub rejected unsupported maintenance dispatch inputs (HTTP 422)."),
        ])

        # Act
        with self.assertRaisesRegex(schedules.ScheduleError, "dispatch inputs") as failure:
            schedules.dispatch_line(
                api, "owner/repo", "container-matrix.yml", "refs/heads/maintenance/10.x",
                "schedule-1-1", timeout=10)

        # Assert
        self.assertIn("maintenance", str(failure.exception))
        self.assertEqual(3, api.call_count)

    def test_main_schedule_loads_independent_authority_and_dispatches_no_inactive_lines(self) -> None:
        """The configured but inactive branches have no hosted side effects."""
        # Arrange
        root = bootstrap_repository(self)
        policy = schedules.lines.load_policy(FIXTURES / "release-lines-initial.json")
        authority = {"policy": policy, "controlCommit": "a" * 40}
        environment = {"GITHUB_REPOSITORY": policy["repository"],
            "GITHUB_EVENT_NAME": "schedule", "GITHUB_REF": "refs/heads/main",
            "GITHUB_RUN_ID": "12", "GITHUB_RUN_ATTEMPT": "1"}

        # Act
        with (tempfile.TemporaryDirectory() as temporary, mock.patch.dict(
                os.environ, environment, clear=True), mock.patch(
                "eng.release.publication.fetch_current_authority", return_value=authority) as loader,
                mock.patch.object(schedules, "dispatch_line") as dispatch,
                redirect_stdout(io.StringIO())):
            output = Path(temporary) / "schedule.json"
            status = schedules.main(["run", "--workflow", "ci.yml", "--root", str(root),
                                     "--output", str(output)])
            result = json.loads(output.read_text())

        # Assert
        self.assertEqual(0, status)
        self.assertEqual([], result["children"])
        self.assertEqual("a" * 40, result["authorityCommit"])
        loader.assert_called_once_with(policy["repository"])
        dispatch.assert_not_called()

    def test_failed_child_report_cannot_produce_successful_cli_exit(self) -> None:
        """Persisted diagnostics do not downgrade a failed schedule to success."""
        # Arrange
        root = bootstrap_repository(self)
        policy = schedules.lines.load_policy(FIXTURES / "release-lines-initial.json")
        authority = {"policy": policy, "controlCommit": "a" * 40}
        environment = {"GITHUB_REPOSITORY": policy["repository"],
            "GITHUB_EVENT_NAME": "schedule", "GITHUB_REF": "refs/heads/main",
            "GITHUB_RUN_ID": "12", "GITHUB_RUN_ATTEMPT": "1"}
        failure = {"conclusion": "failure", "children": [{
            "runId": 27, "verificationStatus": "failed", "failure": "cancelled"}]}
        stderr = io.StringIO()

        # Act
        with (mock.patch.dict(os.environ, environment, clear=True), mock.patch(
                "eng.release.publication.fetch_current_authority", return_value=authority),
                mock.patch.object(schedules, "coordinate_lines", return_value=failure),
                redirect_stdout(io.StringIO()), mock.patch("sys.stderr", stderr)):
            status = schedules.main(["run", "--workflow", "ci.yml", "--root", str(root)])

        # Assert
        self.assertEqual(1, status)
        self.assertIn("inspect child evidence", stderr.getvalue())


def bootstrap_repository(test: unittest.TestCase) -> Path:
    """Isolate bootstrap-only scenarios from mutable delivered activation."""
    temporary = tempfile.TemporaryDirectory()
    test.addCleanup(temporary.cleanup)
    root = Path(temporary.name)
    (root / ".github").mkdir()
    (root / ".github/dependabot.yml").write_text(
        (FIXTURES / "dependabot-initial.yml").read_text(encoding="ascii"), encoding="ascii")

    return root


def native_security_environment() -> dict[str, str]:
    """Return the actual default-branch identity expected by its audit guard."""
    return {"GITHUB_EVENT_NAME": "schedule", "GITHUB_REPOSITORY":
            "doka-labs/Doka.EntityFrameworkCore.MySql", "GITHUB_REF": "refs/heads/main",
            "GITHUB_SHA": "a" * 40, "GITHUB_WORKFLOW_REF":
            "doka-labs/Doka.EntityFrameworkCore.MySql/.github/workflows/security-audit.yml@refs/heads/main"}


def active_policy() -> dict:
    """Return active main/maintenance and inactive preview selection data."""
    return {"repository": "owner/repo", "authorityRef": "refs/heads/main", "lines": [
        {"sourceRef": "refs/heads/main", "state": "active"},
        {"sourceRef": "refs/heads/maintenance/10.x", "state": "active"},
        {"sourceRef": "refs/heads/preview/11.x", "state": "inactive"}]}


def three_major_policy() -> dict:
    """Describe active trunk 12 and maintained 10/11 without activating preview."""
    policy = shared_policy()
    policy["lines"][0]["packageMajor"] = 12
    policy["lines"][1]["state"] = "active"
    policy["lines"][2]["state"] = "inactive"
    policy["lines"].append({**policy["lines"][1],
        "sourceRef": "refs/heads/maintenance/11.x", "packageMajor": 11})

    return policy


def native_workflow_api(
        *, mismatch: str | None = None) -> tuple[schedules.Api, list[tuple[str, dict | None]]]:
    """Route independent hosted observations without thread-order assumptions.

    Fixture YAML declares distinct runner-specific producers. These tests prove
    source-owned dispatch and outcome verification, not execution of either SDK.
    """
    repository = "doka-labs/Doka.EntityFrameworkCore.MySql"
    rows = {
        "maintenance/10.x": {"sha": "a" * 40, "run": 27,
                              "producer": "tests-vstest", "runner": "dotnet test --settings tests.runsettings"},
        "maintenance/11.x": {"sha": "b" * 40, "run": 28,
                              "producer": "tests-mtp", "runner": "dotnet test --report-trx"}}

    calls: list[tuple[str, dict | None]] = []
    lock = threading.Lock()

    def api(endpoint: str, payload: dict | None = None) -> dict:
        """Fail unexpected routes instead of returning a shared green response."""
        # WHY: Both coordinator workers call concurrently; record exact routes
        # under a lock, but choose responses by immutable identity, not ordering.
        with lock:
            calls.append((endpoint, payload))

        for branch, row in rows.items():
            if endpoint == f"repos/{repository}/git/ref/heads/{branch.replace('/', '%2F')}":
                return {"object": {"sha": row["sha"]}}

            if endpoint == f"repos/{repository}/contents/.github/workflows/ci.yml?ref={row['sha']}":
                producer = (rows["maintenance/10.x"]["producer"]
                            if mismatch == "producer-contract" and row["run"] == 28
                            else row["producer"])
                workflow = (
                    '# release-line-schedule: {"schemaVersion":1,"producers":["' + producer + '"]}\n'
                    'on:\n  workflow_dispatch:\n    inputs:\n      maintenance:\n'
                    '      expected_commit:\n      schedule_correlation:\n'
                    'jobs:\n  schedule-identity:\n  ' + producer + ':\n'
                    '    steps:\n      - run: ' + row["runner"] + '\n')

                return {"content": base64.b64encode(workflow.encode("ascii")).decode("ascii")}

            if (endpoint == f"repos/{repository}/actions/workflows/ci.yml/dispatches"
                    and payload is not None and payload.get("ref") == branch):
                return {"workflow_run_id": row["run"]}

            if endpoint == f"repos/{repository}/actions/runs/{row['run']}":
                run = {"id": row["run"], "path": ".github/workflows/ci.yml",
                       "event": "workflow_dispatch", "head_branch": branch,
                       "head_sha": row["sha"], "display_title": "maintenance schedule-1-1",
                       "status": "completed", "conclusion": "success"}

                if row["run"] == 28:
                    swapped = {
                        "workflow": {"path": ".github/workflows/security-audit.yml"},
                        "branch": {"head_branch": "maintenance/10.x"},
                        "commit": {"head_sha": "a" * 40},
                        "run": {"id": 27}}

                    run.update(swapped.get(mismatch, {}))

                return run

            if endpoint == f"repos/{repository}/actions/runs/{row['run']}/jobs?per_page=100&page=1":
                return {"total_count": 2, "jobs": [
                    {"name": "schedule-identity", "conclusion": "success"},
                    {"name": row["producer"], "conclusion": "success"}]}

        raise AssertionError(f"Unexpected scripted GitHub request: {endpoint}")

    return api, calls


def child_environment() -> dict:
    """Return actual GitHub context for the maintenance child fixture."""
    return {"GITHUB_REPOSITORY": "owner/repo", "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_REF": "refs/heads/maintenance/10.x", "GITHUB_SHA": "a" * 40,
            "GITHUB_WORKFLOW_REF": "owner/repo/.github/workflows/container-matrix.yml@refs/heads/maintenance/10.x"}


def child_run() -> dict:
    """Return one exact, completed dispatch response."""
    return {"id": 27, "path": ".github/workflows/container-matrix.yml",
            "event": "workflow_dispatch", "head_branch": "maintenance/10.x",
            "head_sha": "a" * 40, "display_title": "maintenance schedule-1-1",
            "status": "completed", "conclusion": "success"}


if __name__ == "__main__":
    unittest.main()
