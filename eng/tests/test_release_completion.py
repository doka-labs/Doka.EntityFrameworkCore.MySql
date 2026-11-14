"""Exercise completion with actual archives, retained plans, and hostile transports."""

from __future__ import annotations

import argparse
import base64
import copy
import hashlib
import io
import json
import os
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

from eng.release import artifacts, completion, github, nuget, provenance, publication
from eng.tests import test_github_release as release_fixtures
from eng.tests.test_release_publication_admission import publisher_environment_client


REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"
ROOT = Path(__file__).resolve().parents[2]


def git_blob(path: str, raw: bytes) -> dict:
    """Represent exact regular bytes returned by the GitHub Contents API."""
    return {
        "type": "file", "path": path, "encoding": "base64",
        "sha": hashlib.sha1(f"blob {len(raw)}\0".encode("ascii") + raw).hexdigest(),
        "content": base64.b64encode(raw).decode("ascii"),
    }


def captured_error(action) -> Exception | None:
    """Keep the failing Act separate from assertions in negative tests."""
    try:
        action()
    except (completion.CompletionError, publication.PublicationAdmissionError,
            github.GitHubReleaseError, nuget.PublicationError, artifacts.ArtifactResolutionError,
            provenance.ProvenanceError, completion.trust.TrustRootError) as exception:
        return exception

    return None


def run_record(run_id: int, attempt: int, commit: str, *, completed: bool = True) -> dict:
    """Describe one real dispatch attempt independently of the executing test."""
    return {
        "id": run_id, "run_attempt": attempt, "head_sha": commit, "head_branch": "main",
        "event": "workflow_dispatch", "path": completion.WORKFLOW,
        "repository": {"full_name": REPOSITORY}, "head_repository": {"full_name": REPOSITORY},
        "status": "completed" if completed else "in_progress", "conclusion": "failure" if completed else None,
    }


class CompletionPrimitiveTests(unittest.TestCase):
    """Pin source, numeric, producer, and restoration boundaries directly."""

    def test_positive_numeric_ids_reject_booleans_zero_and_noncanonical_text(self) -> None:
        """A truthy JSON boolean cannot stand in for an API resource ID."""
        for value in (False, True, 0, -1, "01", "1/2", "", None):
            with self.subTest(value=value):
                # Arrange
                action = lambda: completion.positive(value, "Resource")

                # Act
                error = captured_error(action)

                # Assert
                self.assertIsInstance(error, completion.CompletionError)

    def test_source_blob_requires_exact_git_bytes(self) -> None:
        """A valid-looking Contents API object cannot substitute other bytes."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = git_blob("CHANGELOG.md", b"original")
        client.request.return_value["content"] = base64.b64encode(b"changed").decode("ascii")

        # Act
        error = captured_error(lambda: completion.read_git_file(client, REPOSITORY, "a" * 40, "CHANGELOG.md"))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("object identity", str(error))

    def test_source_blob_returns_regular_original_bytes(self) -> None:
        """Git data is read without checking out or executing the old source."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = git_blob("CHANGELOG.md", b"original")

        # Act
        result = completion.read_git_file(client, REPOSITORY, "a" * 40, "CHANGELOG.md")

        # Assert
        self.assertEqual(b"original", result)

    def test_qualifier_inventory_expands_all_original_matrix_cells(self) -> None:
        """The aggregate alone does not authorize omitted engine or spec cells."""
        # Arrange
        source = (ROOT / ".github/workflows/quality-gates.yml").read_text(encoding="utf-8")
        policy = json.loads((ROOT / "eng/release/evidence-policy.json").read_text(encoding="utf-8"))

        # Act
        names = completion.qualification_names(source, policy)

        # Assert
        self.assertIn("qualification / Complete qualification", names)
        self.assertIn("qualification / spec-test-suite (mariadb123)", names)
        self.assertIn("qualification / Engine contract (efcore-patch-matrix)", names)
        self.assertEqual(17, len(names))

    def test_qualifier_inventory_rejects_aggregate_omission(self) -> None:
        """Dropping a required producer cannot leave an apparently complete aggregate."""
        # Arrange
        source = (ROOT / ".github/workflows/quality-gates.yml").read_text(encoding="utf-8").replace("      - coverage-gate\n", "")
        policy = json.loads((ROOT / "eng/release/evidence-policy.json").read_text(encoding="utf-8"))

        # Act
        error = captured_error(lambda: completion.qualification_names(source, policy))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("every declared job", str(error))

    def test_successful_producer_cannot_be_replaced_by_later_failure(self) -> None:
        """A later attempted producer must not borrow an earlier green result."""
        # Arrange
        jobs = [{
            "id": attempt, "run_id": 1, "run_attempt": attempt, "name": "assemble",
            "status": "completed", "conclusion": result,
            "steps": [{"status": "completed", "conclusion": "success"}],
        } for attempt, result in ((1, "success"), (2, "failure"))]

        # Act
        error = captured_error(lambda: completion.successful_job(jobs, "assemble", 2))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("did not succeed", str(error))

    def test_producer_before_bound_retains_its_actual_attempt(self) -> None:
        """An unchanged successful job can precede the assembling attempt."""
        # Arrange
        job = {
            "id": 7, "run_id": 1, "run_attempt": 1, "name": "preflight", "status": "completed",
            "conclusion": "success", "steps": [{"status": "completed", "conclusion": "success"}],
        }

        # Act
        result = completion.successful_job([job], "preflight", 2)

        # Assert
        self.assertEqual(1, result["run_attempt"])

    def test_preserve_bytes_rejects_conflict_and_symlink(self) -> None:
        """Restoration is identical-only, including when the target is a link."""
        for symlink in (False, True):
            with self.subTest(symlink=symlink), tempfile.TemporaryDirectory() as temporary:
                # Arrange
                root = Path(temporary)
                original = root / "original"
                original.write_bytes(b"original")
                target = root / "retained"
                target.symlink_to(original) if symlink else target.write_bytes(b"different")

                # Act
                error = captured_error(lambda: completion.preserve_bytes(target, b"original"))

                # Assert
                self.assertIsInstance(error, completion.CompletionError)
                self.assertEqual(b"original", original.read_bytes())


class CompletionOrchestrationTests(unittest.TestCase):
    """Drive real restoration and validation with fake external API transports."""

    def setUp(self) -> None:
        """Reuse the established package/receipt fixture and real archive readers."""
        self.fixture = release_fixtures.GitHubReleaseTests(methodName="runTest")
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.addCleanup(self.fixture.tearDown)
        self.plan = self.fixture._enable_release_line_receipts()
        self.root = self.fixture.root / "completion"
        self.original_id = 12345
        self.control_id = 98765
        self.source = self.fixture._COMMIT
        self.version = self.fixture._VERSION
        self.receipt = publication.read_json(self.fixture.publication / "release-publication-receipt.json")
        self.admission = self.receipt["publicationAdmission"]
        self.authority = self.receipt["publicationAuthority"]
        self.control_commit = self.authority["controlCommit"]
        self.policy_raw = (ROOT / "eng/release/evidence-policy.json").read_bytes()
        self.policy = json.loads(self.policy_raw)
        self.checkpoints = self.fixture.root / "checkpoints"
        self.checkpoints.mkdir()
        (self.checkpoints / "package.json").write_bytes(b'{"stage":"package","conclusion":"success"}\n')
        self._include_checkpoint_in_bundle()
        self.plan = github.build_restored_release_plan(
            REPOSITORY, self.fixture.candidate, self.fixture.publication, self.fixture.changelog,
            attesting_attempt=1, publication_attempt=1,
        )

        self.release_client = release_fixtures.FakeReleaseClient()
        self.release_client.create_draft(self.plan)
        self.release_client.upload_assets(REPOSITORY, self.fixture._TAG, [Path(asset["path"]) for asset in self.plan["assets"]])
        self.release_client.create_calls = 0
        self.release_client.upload_calls.clear()
        self.client, responses, collections = publisher_environment_client(self.authority["policy"])
        self.responses = responses
        self.collections = collections
        self.archives: dict[int, bytes] = {}
        self.artifact_entries: list[dict] = []
        self._write_external_api()
        self.client.request.side_effect = lambda endpoint: copy.deepcopy(self.responses[endpoint])
        self.client.pages.side_effect = lambda endpoint, collection: copy.deepcopy(self.collections[endpoint])
        self.client.download.side_effect = lambda endpoint: self.archives[int(endpoint.split("/")[-2])]
        self.enterContext(mock.patch.dict(os.environ, {
            "GITHUB_ACTIONS": "true", "GITHUB_REPOSITORY": REPOSITORY, "GITHUB_REF": "refs/heads/main",
            "GITHUB_SHA": self.control_commit, "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_RUN_ID": str(self.control_id), "GITHUB_RUN_ATTEMPT": "1", "GITHUB_WORKSPACE": str(ROOT),
            "GITHUB_JOB": "complete",
            "GITHUB_WORKFLOW_REF": f"{REPOSITORY}/{completion.WORKFLOW}@refs/heads/main",
            "DOKA_PUBLICATION_LOCK_GROUP": "doka-publication", "DOKA_PUBLICATION_LOCK_HELD": "1",
        }, clear=True))
        self.enterContext(mock.patch.object(publication, "GitHubClient", return_value=self.client))
        self.enterContext(mock.patch.object(github, "GitHubCliClient", return_value=self.release_client))
        self.verifier = self.enterContext(mock.patch.object(completion.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "verified", "")))
        fingerprint = self.policy["trustedTagSigners"]["signers"][0]["fingerprint"]
        self.enterContext(mock.patch.object(completion.trust, "run_git", return_value=subprocess.CompletedProcess([], 0, "", f'Good "git" signature for kdominic@gmx.de with ED25519 key {fingerprint}')))
        # WHY: The frozen PR API reader already has dedicated adversarial
        # fixtures. Keep it an external transport boundary here and assert its
        # original policy/commit/tree arguments rather than inventing a second
        # full GitHub check-suite fixture system.
        self.pr_reader = self.enterContext(mock.patch.object(completion.trust, "fetch_frozen_qualification_receipt", return_value={"originalFrozenCheckId": 17}))

    def _include_checkpoint_in_bundle(self) -> None:
        """Add the separately retained checkpoint to the exact subject statement."""
        path = self.fixture.publication / provenance.PORTABLE_PROVENANCE_NAME
        bundle = provenance.read_bundle(path, json_lines=True)
        statement = provenance.statement_from_bundle(bundle)
        checkpoint = self.checkpoints / "package.json"
        statement["subject"].append({"name": checkpoint.name, "digest": {"sha256": artifacts.sha256(checkpoint)}})
        bundle["dsseEnvelope"]["payload"] = base64.b64encode(json.dumps(statement).encode("ascii")).decode("ascii")
        path.write_text(json.dumps(bundle, separators=(",", ":")) + "\n", encoding="ascii")

    def _archive(self, name: str, files: dict[str, bytes]) -> None:
        """Expose one immutable ZIP with actual API archive SHA-256 metadata."""
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as package:
            for path, raw in files.items():
                package.writestr(path, raw)

        raw = stream.getvalue()
        artifact_id = len(self.archives) + 11
        self.archives[artifact_id] = raw
        self.artifact_entries.append({
            "id": artifact_id, "name": name, "expired": False, "expires_at": "2099-01-01T00:00:00Z",
            "digest": f"sha256:{hashlib.sha256(raw).hexdigest()}",
            "workflow_run": {"id": self.original_id, "head_sha": self.source},
        })

    def _rewrite_archive(self, name: str, replacements: dict[str, bytes], *, renamed: str | None = None) -> None:
        """Model a different original scenario while retaining real ZIP validation."""
        entry = next(entry for entry in self.artifact_entries if entry["name"] == name)
        with zipfile.ZipFile(io.BytesIO(self.archives[entry["id"]])) as archive:
            files = {item.filename: archive.read(item) for item in archive.infolist()}

        files.update(replacements)
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            for path, raw in files.items():
                archive.writestr(path, raw)

        raw = stream.getvalue()
        self.archives[entry["id"]] = raw
        entry["digest"] = f"sha256:{hashlib.sha256(raw).hexdigest()}"
        if renamed is not None:
            entry["name"] = renamed

    def _write_external_api(self) -> None:
        """Populate both real run identities and original source, artifacts, jobs."""
        self.responses[f"/repos/{REPOSITORY}/branches/main"] = {"name": "main", "protected": True, "commit": {"sha": self.control_commit}}
        self.responses[f"/repos/{REPOSITORY}/contents/{publication.POLICY_PATH}?ref={self.control_commit}"] = git_blob(publication.POLICY_PATH, json.dumps(self.authority["policy"]).encode("ascii"))
        self.responses[f"/repos/{REPOSITORY}/actions/runs/{self.control_id}/attempts/1"] = run_record(self.control_id, 1, self.control_commit, completed=False)
        self.collections[f"/repos/{REPOSITORY}/actions/runs/{self.control_id}/attempts/1/jobs"] = [{
            "id": 900, "run_id": self.control_id, "run_attempt": 1, "name": "complete", "status": "in_progress",
        }]

        self.responses[f"/repos/{REPOSITORY}/actions/runs/{self.original_id}"] = run_record(self.original_id, 2, self.source)
        for attempt in (1, 2):
            endpoint = f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/attempts/{attempt}"
            self.responses[endpoint] = run_record(self.original_id, attempt, self.source)
            self.collections[endpoint + "/jobs"] = []

        names = {"preflight", "assemble", "attest"} | completion.qualification_names((ROOT / ".github/workflows/quality-gates.yml").read_text(encoding="utf-8"), self.policy)
        jobs = [{
            "id": index, "run_id": self.original_id, "run_attempt": 1, "name": name,
            "status": "completed", "conclusion": "success",
            "steps": [{"name": "Actual work", "status": "completed", "conclusion": "success"}],
        } for index, name in enumerate(sorted(names), 1)]

        jobs.append({
            "id": 100, "run_id": self.original_id, "run_attempt": 1, "name": "publish", "status": "completed", "conclusion": "failure",
            "steps": [{"name": publication.ADMISSION_STEP_NAME, "status": "completed", "conclusion": "success"}],
        })
        self.collections[f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/attempts/1/jobs"] = jobs
        self._archive(f"release-candidate-{self.original_id}-attempt-1", {
            **{f"release-candidate/github-{self.original_id}/{path.relative_to(self.fixture.candidate).as_posix()}": path.read_bytes() for path in self.fixture.candidate.rglob("*") if path.is_file()},
            **{f"release-publication/{name}": (self.fixture.publication / name).read_bytes() for name in ("candidate-receipt.json", "candidate-publication-preflight.json", "symbol-readback-manifest.json")},
        })
        self._archive(f"release-checkpoints-{self.original_id}-attempt-1", {f"github-{self.original_id}/package.json": (self.checkpoints / "package.json").read_bytes()})
        self._archive(f"release-publication-{self.version}-attempt-2", {path.relative_to(self.fixture.publication).as_posix(): path.read_bytes() for path in self.fixture.publication.rglob("*") if path.is_file()})
        self._archive(f"release-provenance-{self.original_id}-attempt-1", {provenance.PORTABLE_PROVENANCE_NAME: (self.fixture.publication / provenance.PORTABLE_PROVENANCE_NAME).read_bytes()})
        self._archive("publication-admission-attempt-1", {name: (self.fixture.publication / name).read_bytes() for name in ("publication-admission.json", "authorization-observation.json")})
        self.collections[f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/artifacts"] = self.artifact_entries
        for path, raw in {
            "CHANGELOG.md": self.fixture.changelog.read_bytes(), "eng/release/evidence-policy.json": self.policy_raw,
            ".github/workflows/quality-gates.yml": (ROOT / ".github/workflows/quality-gates.yml").read_bytes(),
            completion.WORKFLOW: (ROOT / completion.WORKFLOW).read_bytes(),
        }.items():
            self.responses[f"/repos/{REPOSITORY}/contents/{path}?ref={self.source}"] = git_blob(path, raw)

        for path in ("eng/release/evidence-policy.json", ".github/allowed_signers"):
            self.responses[f"/repos/{REPOSITORY}/contents/{path}?ref={self.control_commit}"] = git_blob(path, (ROOT / path).read_bytes())

        self.responses[f"/repos/{REPOSITORY}/compare/{self.source}...main"] = {"status": "ahead", "base_commit": {"sha": self.source}, "merge_base_commit": {"sha": self.source}}
        self.responses[f"/repos/{REPOSITORY}/git/ref/tags/{self.fixture._TAG}"] = {"object": {"type": "tag", "sha": "d" * 40}}
        self.responses[f"/repos/{REPOSITORY}/git/tags/{'d' * 40}"] = {"sha": "d" * 40, "tag": self.fixture._TAG, "object": {"type": "commit", "sha": self.source}, "verification": {"verified": True, "reason": "valid"}}
        self.responses[f"/repos/{REPOSITORY}/git/commits/{self.source}"] = {"sha": self.source, "tree": {"sha": "c" * 40}}

    def _prepare_args(self) -> argparse.Namespace:
        """Use the same explicit preparation CLI contract as the hosted workflow."""
        return completion.build_parser().parse_args([
            "prepare", "--repository", REPOSITORY, "--version", self.version,
            "--original-run-id", str(self.original_id), "--output-dir", str(self.root),
        ])

    def _complete_args(self, digest: str) -> argparse.Namespace:
        """Bind completion to the exact file digest emitted by preparation."""
        return completion.build_parser().parse_args([
            "complete", "--receipt", str(self.root / "prepared.json"),
            "--receipt-sha256", digest, "--output-dir", str(self.root),
        ])

    def _public_transport(self) -> tuple[dict[str, bytes], dict[str, bytes]]:
        """Inject only HTTP responses into the real public-byte polling worker."""
        package_payloads = {
            nuget.remote_package_url(self.fixture._PACKAGE_BASE_ADDRESS, package_id, self.version):
                (self.fixture.publication / "packages" / nuget.package_file_name(package_id, self.version, "nupkg")).read_bytes()
            for package_id in nuget.PACKAGE_IDENTITIES.values()
        }

        symbol_entries = nuget.validated_symbol_entries(publication.read_json(self.fixture.publication / "symbol-readback-manifest.json"), self.version)
        symbol_payloads = {
            entry["packageId"]: (self.fixture.publication / "packages" / "symbols" / entry["pdbName"]).read_bytes()
            for entry in symbol_entries
        }

        packages = nuget.observe_remote_packages
        symbols = nuget.observe_remote_symbols
        self.enterContext(mock.patch.object(nuget, "resolve_package_base_address", return_value=self.fixture._PACKAGE_BASE_ADDRESS))
        self.enterContext(mock.patch.object(nuget, "observe_remote_packages", side_effect=lambda *args, **kwargs: packages(*args, **kwargs, fetcher=lambda url, timeout: package_payloads[url])))
        self.enterContext(mock.patch.object(nuget, "observe_remote_symbols", side_effect=lambda *args, **kwargs: symbols(*args, **kwargs, fetcher=lambda entry, timeout: symbol_payloads[entry["packageId"]])))

        return package_payloads, symbol_payloads

    def test_prepare_restores_original_proof_without_impersonating_its_run(self) -> None:
        """Original failed runs retain their identity while new control stays main."""
        # Arrange
        before = dict(os.environ)

        # Act
        result = completion.prepare(self._prepare_args())

        # Assert
        prepared = publication.read_json(Path(result["preparedReceipt"]))
        self.assertEqual(before, dict(os.environ))
        self.assertEqual(2, prepared["originalRun"]["run_attempt"])
        self.assertEqual("failure", prepared["originalRun"]["conclusion"])
        self.assertEqual(self.control_id, prepared["controlRun"]["id"])
        self.assertEqual("1", prepared["admission"]["identity"]["candidateRunAttempt"])
        self.assertEqual(5, len(prepared["artifacts"]))
        self.assertEqual(42, prepared["existingRelease"]["id"])
        self.assertEqual([], self.release_client.publish_existing_calls)
        self.assertEqual(self.source, self.pr_reader.call_args.kwargs["commit"])
        self.assertEqual(self.policy, self.pr_reader.call_args.kwargs["policy"])
        self.assertEqual(artifacts.sha256(self.root / "prepared.json"), result["sha256"])

    def test_prepare_rejects_noncontrol_contexts(self) -> None:
        """A source run, PR event, missing lock, or stale main cannot claim control."""
        for key, value in (
            ("GITHUB_RUN_ID", str(self.original_id)), ("GITHUB_EVENT_NAME", "pull_request"),
            ("GITHUB_SHA", self.source), ("GITHUB_REF", "refs/heads/maintenance/10.x"),
            ("GITHUB_JOB", "publish"),
            ("DOKA_PUBLICATION_LOCK_HELD", "0"),
        ):
            with self.subTest(key=key):
                # Arrange
                with mock.patch.dict(os.environ, {key: value}):
                    # Act
                    error = captured_error(lambda: completion.prepare(self._prepare_args()))

                # Assert
                self.assertIsNotNone(error)
                self.assertEqual([], self.release_client.publish_existing_calls)

    def test_prepare_rejects_ambiguous_skipped_or_foreign_control_jobs(self) -> None:
        """The main run alone cannot impersonate its protected complete job."""
        for mutation in ("absent", "duplicate", "skipped", "foreign"):
            with self.subTest(mutation=mutation):
                # Arrange
                endpoint = f"/repos/{REPOSITORY}/actions/runs/{self.control_id}/attempts/1/jobs"
                jobs = copy.deepcopy(self.collections[endpoint])
                if mutation == "absent":
                    jobs.clear()
                elif mutation == "duplicate":
                    jobs.append({**jobs[0], "id": 901})
                elif mutation == "skipped":
                    jobs[0]["status"] = "completed"
                    jobs[0]["conclusion"] = "skipped"
                else:
                    jobs[0]["run_id"] = self.original_id

                with mock.patch.dict(self.collections, {endpoint: jobs}):
                    # Act
                    error = captured_error(lambda: completion.prepare(self._prepare_args()))

                # Assert
                self.assertIsInstance(error, completion.CompletionError)
                self.assertIn("protected control job", str(error))

    def test_prepare_rejects_missing_expired_duplicate_or_foreign_artifacts(self) -> None:
        """Archive metadata is independently bound before local restoration."""
        for mutation in ("missing", "expired", "expiry", "duplicate", "foreign", "digest"):
            with self.subTest(mutation=mutation):
                # Arrange
                changed = copy.deepcopy(self.artifact_entries)
                if mutation == "missing":
                    changed.pop(0)
                elif mutation == "expired":
                    changed[0]["expired"] = True
                elif mutation == "expiry":
                    changed[0]["expires_at"] = "2000-01-01T00:00:00Z"
                elif mutation == "duplicate":
                    changed.append(copy.deepcopy(changed[0]))
                elif mutation == "foreign":
                    changed[0]["workflow_run"]["head_sha"] = "f" * 40
                else:
                    changed[0]["digest"] = "sha256:" + "0" * 64

                endpoint = f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/artifacts"
                with mock.patch.dict(self.collections, {endpoint: changed}):
                    # Act
                    error = captured_error(lambda: completion.prepare(self._prepare_args()))

                # Assert
                self.assertIsNotNone(error)
                self.assertEqual([], self.release_client.publish_existing_calls)

    def test_prepare_rejects_absent_or_failed_required_qualifier_cells(self) -> None:
        """A green aggregate cannot hide a missing or failed matrix producer."""
        for mutation in ("missing", "failure", "skipped"):
            with self.subTest(mutation=mutation):
                # Arrange
                endpoint = f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/attempts/1/jobs"
                changed = copy.deepcopy(self.collections[endpoint])
                index = next(index for index, job in enumerate(changed) if job["name"] == "qualification / spec-test-suite (mysql84)")
                if mutation == "missing":
                    changed.pop(index)
                else:
                    changed[index]["conclusion"] = mutation

                with mock.patch.dict(self.collections, {endpoint: changed}):
                    # Act
                    error = captured_error(lambda: completion.prepare(self._prepare_args()))

                # Assert
                self.assertIsInstance(error, completion.CompletionError)
                self.assertIn("producer", str(error))

    def test_prepare_rejects_missing_admission_instead_of_recreating_it(self) -> None:
        """No original admission means completion has no publication permission."""
        # Arrange
        endpoint = f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/artifacts"
        self.collections[endpoint] = [entry for entry in self.artifact_entries if not entry["name"].startswith("publication-admission-")]

        # Act
        error = captured_error(lambda: completion.prepare(self._prepare_args()))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("cannot mint", str(error))

    def test_prepare_rejects_changed_tag_or_detached_original_source(self) -> None:
        """Original SHA and annotated tag remain independently reachable identities."""
        for endpoint, replacement in (
            (f"/repos/{REPOSITORY}/git/ref/tags/{self.fixture._TAG}", {"object": {"type": "commit", "sha": self.source}}),
            (f"/repos/{REPOSITORY}/compare/{self.source}...main", {"status": "diverged"}),
        ):
            with self.subTest(endpoint=endpoint):
                # Arrange
                with mock.patch.dict(self.responses, {endpoint: replacement}):
                    # Act
                    error = captured_error(lambda: completion.prepare(self._prepare_args()))

                # Assert
                self.assertIsInstance(error, completion.CompletionError)

    def test_prepare_rejects_failed_cryptographic_verifier(self) -> None:
        """A well-formed attestation does not substitute for signature verification."""
        # Arrange
        self.verifier.return_value = subprocess.CompletedProcess([], 1, "", "signature rejected")

        # Act
        error = captured_error(lambda: completion.prepare(self._prepare_args()))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("verifier failed", str(error))
        self.assertEqual([], self.release_client.publish_existing_calls)

    def test_prepare_rejects_signer_revoked_by_current_main(self) -> None:
        """A previously admitted signer must still be trusted by current controls."""
        # Arrange
        current_policy = copy.deepcopy(self.policy)
        current_policy["trustedTagSigners"]["signers"] = []
        endpoint = f"/repos/{REPOSITORY}/contents/eng/release/evidence-policy.json?ref={self.control_commit}"
        self.responses[endpoint] = git_blob("eng/release/evidence-policy.json", json.dumps(current_policy).encode("ascii"))

        # Act
        error = captured_error(lambda: completion.prepare(self._prepare_args()))

        # Assert
        self.assertIsInstance(error, completion.trust.TrustRootError)
        self.assertEqual([], self.release_client.publish_existing_calls)

    def test_prepare_rejects_current_line_revocation(self) -> None:
        """Frozen qualification is not current permission to publish the line."""
        # Arrange
        policy = copy.deepcopy(self.authority["policy"])
        policy["lines"][0]["state"] = "inactive"
        endpoint = f"/repos/{REPOSITORY}/contents/{publication.POLICY_PATH}?ref={self.control_commit}"
        self.responses[endpoint] = git_blob(publication.POLICY_PATH, json.dumps(policy).encode("ascii"))

        # Act
        error = captured_error(lambda: completion.prepare(self._prepare_args()))

        # Assert
        self.assertIsNotNone(error)
        self.assertEqual([], self.release_client.publish_existing_calls)

    def test_prepare_rejects_attestation_from_after_original_admission(self) -> None:
        """Later portable invocation bytes cannot borrow the earlier admitted signer."""
        # Arrange
        path = self.fixture.publication / provenance.PORTABLE_PROVENANCE_NAME
        bundle = provenance.read_bundle(path, json_lines=True)
        statement = provenance.statement_from_bundle(bundle)
        statement["predicate"]["runDetails"]["metadata"]["invocationId"] = f"https://github.com/{REPOSITORY}/actions/runs/{self.original_id}/attempts/2"
        bundle["dsseEnvelope"]["payload"] = base64.b64encode(json.dumps(statement).encode("ascii")).decode("ascii")
        raw = (json.dumps(bundle) + "\n").encode("ascii")
        self._rewrite_archive(f"release-provenance-{self.original_id}-attempt-1", {provenance.PORTABLE_PROVENANCE_NAME: raw})
        self._rewrite_archive(f"release-publication-{self.version}-attempt-2", {provenance.PORTABLE_PROVENANCE_NAME: raw})

        # Act
        error = captured_error(lambda: completion.prepare(self._prepare_args()))

        # Assert
        self.assertIsInstance(error, provenance.ProvenanceError)
        self.assertIn("attesting attempt", str(error))

    def test_prepare_rejects_version_mismatch(self) -> None:
        """The request cannot retarget an admitted run to another release version."""
        # Arrange
        args = self._prepare_args()
        args.version = "10.0.0-rc.2"

        # Act
        error = captured_error(lambda: completion.prepare(args))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("requested release", str(error))

    def test_prepare_uses_retained_publication_after_a_later_early_failed_attempt(self) -> None:
        """A later failure need not erase the exact previously uploaded receipt."""
        # Arrange
        self.responses[f"/repos/{REPOSITORY}/actions/runs/{self.original_id}"]["run_attempt"] = 3
        endpoint = f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/attempts/3"
        self.responses[endpoint] = run_record(self.original_id, 3, self.source)
        self.collections[endpoint + "/jobs"] = []

        # Act
        result = completion.prepare(self._prepare_args())

        # Assert
        prepared = publication.read_json(Path(result["preparedReceipt"]))
        self.assertEqual(3, prepared["originalRun"]["run_attempt"])
        self.assertIn(f"release-publication-{self.version}-attempt-2", [entry["name"] for entry in prepared["artifacts"]])

    def test_prepare_retains_a_real_attest_retry_after_candidate_before_admission(self) -> None:
        """Candidate and signer attempts stay distinct when a legitimate retry attests."""
        # Arrange
        original = copy.deepcopy(self.admission)
        original["identity"]["originalPublicationAttempt"] = "2"
        original["admissionId"] = publication.canonical_digest(original["identity"])
        receipt = copy.deepcopy(self.receipt)
        receipt["publicationAdmission"] = original
        publication.write_json(self.fixture.publication / "release-publication-receipt.json", receipt)
        publication.write_json(self.fixture.publication / "publication-admission.json", original)
        path = self.fixture.publication / provenance.PORTABLE_PROVENANCE_NAME
        bundle = provenance.read_bundle(path, json_lines=True)
        statement = provenance.statement_from_bundle(bundle)
        statement["predicate"]["runDetails"]["metadata"]["invocationId"] = f"https://github.com/{REPOSITORY}/actions/runs/{self.original_id}/attempts/2"
        bundle["dsseEnvelope"]["payload"] = base64.b64encode(json.dumps(statement).encode("ascii")).decode("ascii")
        path.write_text(json.dumps(bundle) + "\n", encoding="ascii")
        self._rewrite_archive(f"release-provenance-{self.original_id}-attempt-1", {provenance.PORTABLE_PROVENANCE_NAME: path.read_bytes()}, renamed=f"release-provenance-{self.original_id}-attempt-2")
        self._rewrite_archive("publication-admission-attempt-1", {"publication-admission.json": (self.fixture.publication / "publication-admission.json").read_bytes()}, renamed="publication-admission-attempt-2")
        self._rewrite_archive(f"release-publication-{self.version}-attempt-2", {name: (self.fixture.publication / name).read_bytes() for name in ("publication-admission.json", "release-publication-receipt.json", provenance.PORTABLE_PROVENANCE_NAME)})
        endpoint = f"/repos/{REPOSITORY}/actions/runs/{self.original_id}/attempts/2/jobs"
        self.collections[endpoint] = [{
            "id": 200, "run_id": self.original_id, "run_attempt": 2, "name": "attest", "status": "completed", "conclusion": "success",
            "steps": [{"name": "Attest", "status": "completed", "conclusion": "success"}],
        }, {
            "id": 201, "run_id": self.original_id, "run_attempt": 2, "name": "publish", "status": "completed", "conclusion": "failure",
            "steps": [{"name": publication.ADMISSION_STEP_NAME, "status": "completed", "conclusion": "success"}],
        }]

        plan = github.build_restored_release_plan(REPOSITORY, self.fixture.candidate, self.fixture.publication, self.fixture.changelog, attesting_attempt=2, publication_attempt=2)
        self.release_client.create_draft(plan)
        self.release_client.upload_assets(REPOSITORY, self.fixture._TAG, [Path(asset["path"]) for asset in plan["assets"]])
        self.release_client.create_calls = 0
        self.release_client.upload_calls.clear()

        # Act
        result = completion.prepare(self._prepare_args())

        # Assert
        prepared = publication.read_json(Path(result["preparedReceipt"]))
        self.assertEqual("1", prepared["admission"]["identity"]["candidateRunAttempt"])
        self.assertEqual(2, next(job["run_attempt"] for job in prepared["producers"] if job["name"] == "attest"))
        self.assertIn(f"release-provenance-{self.original_id}-attempt-2", [entry["name"] for entry in prepared["artifacts"]])

    def test_complete_rejects_receipt_mutation_before_any_remote_write(self) -> None:
        """The transition binds preparation bytes rather than trusting a path."""
        # Arrange
        result = completion.prepare(self._prepare_args())
        (self.root / "prepared.json").write_text("{}\n", encoding="ascii")

        # Act
        error = captured_error(lambda: completion.complete(self._complete_args(result["sha256"])))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertEqual([], self.release_client.publish_existing_calls)
        self.assertFalse((self.root / "completion.json").exists())

    def test_complete_rejects_replaced_release_id(self) -> None:
        """Matching names and assets cannot substitute a different Release ID."""
        # Arrange
        result = completion.prepare(self._prepare_args())
        self.release_client.release["id"] = 999

        # Act
        error = captured_error(lambda: completion.complete(self._complete_args(result["sha256"])))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertEqual([], self.release_client.publish_existing_calls)

    def test_complete_rejects_changed_original_run_attempt(self) -> None:
        """A new original rerun cannot replace a frozen preparation's evidence."""
        # Arrange
        result = completion.prepare(self._prepare_args())
        self.responses[f"/repos/{REPOSITORY}/actions/runs/{self.original_id}"]["head_sha"] = "f" * 40

        # Act
        error = captured_error(lambda: completion.complete(self._complete_args(result["sha256"])))

        # Assert
        self.assertIsNotNone(error)
        self.assertEqual([], self.release_client.publish_existing_calls)

    def test_complete_verifies_public_bytes_then_only_publishes_frozen_numeric_id(self) -> None:
        """Use real byte/readback validators before a single existing-ID PATCH."""
        # Arrange
        prepared = completion.prepare(self._prepare_args())
        self._public_transport()

        # Act
        result = completion.complete(self._complete_args(prepared["sha256"]))

        # Assert
        self.assertEqual([42], self.release_client.publish_existing_calls)
        self.assertEqual(0, self.release_client.create_calls)
        self.assertEqual([], self.release_client.upload_calls)
        self.assertEqual([], self.release_client.delete_calls)
        self.assertEqual("failure", result["originalRun"]["conclusion"])
        self.assertEqual(prepared["sha256"], result["preparationSha256"])
        self.assertTrue((self.root / "completion.json").is_file())
        self.assertEqual(3, len(result["publicReadback"]["verifiedPackages"]))
        self.assertTrue(all(Path(entry["verificationPath"]).read_text(encoding="utf-8") == "verified" for entry in result["publicReadback"]["verifiedPackages"]))
        self.assertTrue(result["originalRunUrl"].endswith(str(self.original_id)))
        self.assertEqual(3, sum(call.args[0][:3] == ["dotnet", "nuget", "verify"] for call in self.verifier.call_args_list))

    def test_complete_rejects_conflicting_public_primary(self) -> None:
        """A present but different package must fail before release mutation."""
        # Arrange
        prepared = completion.prepare(self._prepare_args())
        packages, _ = self._public_transport()
        packages[next(iter(packages))] = self.fixture._zip_payload("changed.txt", b"conflicting primary")

        # Act
        error = captured_error(lambda: completion.complete(self._complete_args(prepared["sha256"])))

        # Assert
        self.assertIsInstance(error, nuget.PublicationError)
        self.assertEqual([], self.release_client.publish_existing_calls)
        self.assertFalse((self.root / "completion.json").exists())

    def test_complete_rejects_conflicting_public_symbols(self) -> None:
        """A present PDB with another checksum cannot count as symbol availability."""
        # Arrange
        prepared = completion.prepare(self._prepare_args())
        _, symbols = self._public_transport()
        symbols[next(iter(symbols))] = b"BSJB conflicting portable PDB"

        # Act
        error = captured_error(lambda: completion.complete(self._complete_args(prepared["sha256"])))

        # Assert
        self.assertIsInstance(error, nuget.PublicationError)
        self.assertEqual([], self.release_client.publish_existing_calls)
        self.assertFalse((self.root / "completion.json").exists())

    def test_complete_rejects_failed_public_signature_verification(self) -> None:
        """A signature-shaped ZIP entry cannot replace successful dotnet verification."""
        # Arrange
        prepared = completion.prepare(self._prepare_args())
        self._public_transport()
        self.verifier.side_effect = lambda args, **kwargs: subprocess.CompletedProcess(args, 1 if args[:3] == ["dotnet", "nuget", "verify"] else 0, "", "untrusted")

        # Act
        error = captured_error(lambda: completion.complete(self._complete_args(prepared["sha256"])))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertEqual([], self.release_client.publish_existing_calls)
        self.assertFalse((self.root / "completion.json").exists())

    def test_complete_rechecks_current_main_after_public_indexing_wait(self) -> None:
        """Authority changes during public readback prevent the sole remote write."""
        # Arrange
        prepared = completion.prepare(self._prepare_args())
        self._public_transport()
        readback = completion.public_readback

        def advance_main(plan: dict, output: Path) -> dict:
            result = readback(plan, output)
            new_commit = "e" * 40
            self.responses[f"/repos/{REPOSITORY}/branches/main"]["commit"]["sha"] = new_commit
            self.responses[f"/repos/{REPOSITORY}/contents/{publication.POLICY_PATH}?ref={new_commit}"] = git_blob(publication.POLICY_PATH, json.dumps(self.authority["policy"]).encode("ascii"))

            return result

        # Act
        with mock.patch.object(completion, "public_readback", side_effect=advance_main):
            error = captured_error(lambda: completion.complete(self._complete_args(prepared["sha256"])))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("current protected main", str(error))
        self.assertEqual([], self.release_client.publish_existing_calls)
        self.assertFalse((self.root / "completion.json").exists())

    def test_complete_rehashes_public_bytes_after_final_original_revalidation(self) -> None:
        """Local public readback cannot be substituted while original proof is reread."""
        # Arrange
        prepared = completion.prepare(self._prepare_args())
        self._public_transport()
        observe = completion.observe_original
        observations = 0

        def substitute_after_final_check(*args) -> tuple[dict, dict, dict]:
            nonlocal observations
            result = observe(*args)
            observations += 1
            if observations == 2:
                path = next((self.root / "public-readback" / "packages").glob("*.nupkg"))
                path.write_bytes(b"substituted public bytes")

            return result

        # Act
        with mock.patch.object(completion, "observe_original", side_effect=substitute_after_final_check):
            error = captured_error(lambda: completion.complete(self._complete_args(prepared["sha256"])))

        # Assert
        self.assertIsInstance(error, completion.CompletionError)
        self.assertIn("Public verification bytes changed", str(error))
        self.assertEqual([], self.release_client.publish_existing_calls)
        self.assertFalse((self.root / "completion.json").exists())

    def test_cli_reports_failure_as_nonzero_without_completion_receipt(self) -> None:
        """Hosted shell success cannot hide a rejected completion boundary."""
        # Arrange
        arguments = ["prepare", "--repository", REPOSITORY, "--version", self.version, "--original-run-id", str(self.control_id), "--output-dir", str(self.root)]

        # Act
        with mock.patch("sys.stderr", new_callable=io.StringIO) as errors:
            result = completion.main(arguments)

        # Assert
        self.assertEqual(1, result)
        self.assertIn("failed", errors.getvalue())
        self.assertFalse((self.root / "completion.json").exists())


if __name__ == "__main__":
    unittest.main()
