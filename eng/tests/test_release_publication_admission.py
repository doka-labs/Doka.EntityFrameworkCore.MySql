"""Positive and adversarial checks for exact-run publication admission."""

from __future__ import annotations

import base64
import copy
import hashlib
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock
from urllib.parse import quote

from eng.release import lines, publication
from eng.tests.test_release_lines import initial_policy, shared_policy


def authority(policy: dict | None = None) -> dict:
    """Build one independent protected-main policy observation."""
    policy = initial_policy() if policy is None else policy

    return {
        "schemaVersion": 1, "kind": publication.AUTHORITY_KIND,
        "repository": policy["repository"], "controlCommit": "a" * 40,
        "authorizationRevision": policy["revision"],
        "authorizationDigest": lines.policy_digest(policy), "policy": policy,
        "observedUtc": "2026-10-22T12:00:00Z",
    }


def identity() -> dict:
    """Build the complete immutable identity for one original hosted attempt."""
    policy = initial_policy()
    snapshot = lines.freeze_line(policy, "refs/heads/main", "10.6.2")

    return {
        "repository": policy["repository"], "sourceRef": "refs/heads/main",
        "sourceCommit": "b" * 40, "releaseVersion": "10.6.2", "releaseTag": "v10.6.2",
        "workflowPath": ".github/workflows/release-candidate.yml", "workflowRunId": "123",
        "candidateRunAttempt": "1", "originalPublicationAttempt": "2",
        "manifestSha256": "c" * 64, "candidateReceiptSha256": "d" * 64,
        "releaseLineSha256": publication.canonical_digest(snapshot),
    }


def admission() -> dict:
    """Admit one active-line fixture through the production validator."""
    return publication.create_admission(identity(), authority())


def environment() -> dict:
    """Build the exact trusted workflow context for a same-run retry."""
    candidate = identity()

    return {
        "GITHUB_ACTIONS": "true", "GITHUB_REPOSITORY": candidate["repository"],
        "GITHUB_REF": candidate["sourceRef"], "GITHUB_SHA": candidate["sourceCommit"],
        "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "3",
        "GITHUB_WORKFLOW_REF": f"{candidate['repository']}/{candidate['workflowPath']}@refs/heads/main",
        "DOKA_PUBLICATION_LOCK_GROUP": "doka-publication", "DOKA_PUBLICATION_LOCK_HELD": "1",
    }


def failure(action) -> Exception:
    """Capture a domain failure in Act so assertions remain a separate phase."""
    try:
        action()
    except publication.PublicationAdmissionError as exception:
        return exception

    raise AssertionError("The action unexpectedly authorized unproven publication evidence.")


def validate_bound(original: dict, observed: dict, **extra) -> None:
    """Invoke the full boundary with the original immutable candidate identity."""
    candidate = original["identity"]
    publication.validate_bound_publication(
        original, observed, repository=candidate["repository"], source_ref=candidate["sourceRef"],
        source_commit=candidate["sourceCommit"], version=candidate["releaseVersion"],
        tag=candidate["releaseTag"], **extra,
    )


def policy_file(raw: bytes | None = None) -> dict:
    """Build exact Contents API metadata for one regular policy Git blob."""
    raw = json.dumps(initial_policy()).encode("ascii") if raw is None else raw

    return {
        "type": "file", "path": publication.POLICY_PATH, "encoding": "base64",
        "sha": hashlib.sha1(f"blob {len(raw)}\0".encode("ascii") + raw).hexdigest(),
        "content": base64.b64encode(raw).decode("ascii"),
    }


def hosted_environment(name: str) -> dict:
    """Build the existing single-maintainer gate without disabling admin bypass."""
    return {
        "name": name, "can_admins_bypass": True,
        "protection_rules": [{
            "type": "required_reviewers", "prevent_self_review": False, "reviewers": [{"id": 1}],
        }],
        "deployment_branch_policy": {"custom_branch_policies": True, "protected_branches": False},
    }


def publisher_environment_client(policy: dict | None = None) -> tuple[mock.Mock, dict, dict]:
    """Expose only the existing publisher environment and exact source grant."""
    policy = initial_policy() if policy is None else policy
    repository = lines.REPOSITORY
    responses = {
        f"/repos/{repository}/environments/nuget": hosted_environment("nuget"),
    }

    active_branches = [line["sourceRef"].removeprefix("refs/heads/")
                       for line in policy["lines"] if line["state"] == "active"]

    for name in active_branches:
        responses[f"/repos/{repository}/branches/{quote(name, safe='')}"] = {"protected": True}

    grants = {
        f"/repos/{repository}/environments/nuget/deployment-branch-policies": [
            {"name": name, "type": "branch"} for name in active_branches
        ],
    }

    client = mock.Mock(spec=publication.GitHubClient)
    client.request.side_effect = responses.__getitem__
    client.pages.side_effect = lambda endpoint, _: grants[endpoint]

    return client, responses, grants


class CurrentAuthorityTests(unittest.TestCase):
    """Require current protected-main data without historic control services."""

    def test_client_requests_are_read_only(self) -> None:
        """The publication reader exposes no controller write/payload API."""
        # Arrange
        endpoint = f"/repos/{lines.REPOSITORY}/branches/main"
        response = mock.Mock(returncode=0, stdout=b'{"name":"main"}')

        # Act
        with mock.patch.object(publication.subprocess, "run", return_value=response) as run:
            result = publication.GitHubClient().request(endpoint)

        # Assert
        self.assertEqual({"name": "main"}, result)
        run.assert_called_once_with(
            ["gh", "api", "--method", "GET", endpoint], capture_output=True, check=False, timeout=60
        )

    def test_client_rejects_api_failure_without_exposing_response_details(self) -> None:
        """Authentication/network errors fail closed without logging command output."""
        # Arrange
        endpoint = f"/repos/{lines.REPOSITORY}/branches/main"
        response = mock.Mock(returncode=1, stderr=b"sensitive response (HTTP 403)", stdout=b"")

        # Act
        with mock.patch.object(publication.subprocess, "run", return_value=response):
            error = failure(lambda: publication.GitHubClient().request(endpoint))

        # Assert
        self.assertIn("status 403", str(error))
        self.assertNotIn("sensitive response", str(error))

    def test_client_rejects_malformed_api_json(self) -> None:
        """Successful transport does not make malformed GitHub evidence usable."""
        # Arrange
        response = mock.Mock(returncode=0, stdout=b"not-json")

        # Act
        with mock.patch.object(publication.subprocess, "run", return_value=response):
            error = failure(lambda: publication.GitHubClient().request("/repos/owner/repository"))

        # Assert
        self.assertIn("invalid JSON", str(error))

    def test_current_policy_needs_no_historic_api(self) -> None:
        """A normal current read works even when every history API is unavailable."""
        # Arrange
        policy = shared_policy()
        policy["revision"] = "2027-01-01.1"
        commit = "a" * 40
        endpoints = {
            f"/repos/{lines.REPOSITORY}/branches/main": {
                "name": "main", "protected": True, "commit": {"sha": commit}
            },
            f"/repos/{lines.REPOSITORY}/contents/{publication.POLICY_PATH}?ref={commit}":
                policy_file(json.dumps(policy).encode("ascii")),
        }
        client = mock.Mock(spec=publication.GitHubClient)
        # WHY: any attempt to reconstruct history fails instead of being mocked successful.
        client.request.side_effect = lambda endpoint: endpoints[endpoint]

        # Act
        result = publication.fetch_current_authority(lines.REPOSITORY, client)

        # Assert
        self.assertEqual(policy, result["policy"])
        self.assertEqual(commit, result["controlCommit"])
        self.assertEqual(lines.policy_digest(policy), result["authorizationDigest"])
        self.assertEqual([mock.call(endpoint) for endpoint in endpoints], client.request.call_args_list)
        client.pages.assert_not_called()
        client.download.assert_not_called()

    def test_missing_unprotected_or_mismatched_main_is_denied(self) -> None:
        """Never infer protected-main identity from a candidate or another branch."""
        # Arrange
        branches = (
            None, [], {},
            {"name": "main", "protected": False, "commit": {"sha": "a" * 40}},
            {"name": "preview/11.x", "protected": True, "commit": {"sha": "a" * 40}},
            {"name": "main", "protected": True, "commit": "a" * 40},
            {"name": "main", "protected": True, "commit": {"sha": "main"}},
        )
        clients = [mock.Mock(spec=publication.GitHubClient) for _ in branches]
        for client, branch in zip(clients, branches):
            client.request.return_value = branch

        # Act
        errors = [failure(lambda: publication.fetch_current_authority(lines.REPOSITORY, client)) for client in clients]

        # Assert
        self.assertTrue(all("protected main" in str(error) for error in errors))
        self.assertTrue(all(client.request.call_count == 1 for client in clients))

    def test_policy_requires_exact_regular_file_metadata(self) -> None:
        """Missing, symlinked, wrong-path and malformed Contents responses fail closed."""
        # Arrange
        entry = policy_file()
        entries = [None, [], {}] + [dict(entry, **change) for change in (
            {"type": "symlink"}, {"path": "candidate/policy.json"}, {"encoding": "none"},
            {"content": None}, {"sha": "main"},
        )]
        clients = [mock.Mock(spec=publication.GitHubClient) for _ in entries]
        for client, value in zip(clients, entries):
            client.request.side_effect = [
                {"name": "main", "protected": True, "commit": {"sha": "a" * 40}}, value
            ]

        # Act
        errors = [failure(lambda: publication.fetch_current_authority(lines.REPOSITORY, client)) for client in clients]

        # Assert
        self.assertTrue(all("regular file" in str(error) for error in errors))

    def test_malformed_policy_data_is_denied(self) -> None:
        """Reject malformed JSON, duplicate fields, wrong schemas and other repositories."""
        # Arrange
        policy = initial_policy()
        duplicated = (json.dumps(policy)[:-1] + ', "revision": "2026-10-22.1"}').encode("ascii")
        malformed = [b"{", b"[]", b"\xff", duplicated]
        malformed += [json.dumps(dict(policy, **change)).encode("ascii") for change in (
            {"schemaVersion": True}, {"repository": "another/repository"},
        )]
        clients = [mock.Mock(spec=publication.GitHubClient) for _ in malformed]
        for client, raw in zip(clients, malformed):
            client.request.side_effect = [
                {"name": "main", "protected": True, "commit": {"sha": "a" * 40}}, policy_file(raw)
            ]

        # Act
        errors = [failure(lambda: publication.fetch_current_authority(lines.REPOSITORY, client)) for client in clients]

        # Assert
        self.assertTrue(all("cannot be verified" in str(error) for error in errors))

    def test_policy_blob_must_match_downloaded_bytes(self) -> None:
        """Current metadata must not bless substituted valid policy content."""
        # Arrange
        entry = policy_file()
        entry["sha"] = "b" * 40
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.side_effect = [
            {"name": "main", "protected": True, "commit": {"sha": "a" * 40}}, entry
        ]

        # Act
        error = failure(lambda: publication.fetch_current_authority(lines.REPOSITORY, client))

        # Assert
        self.assertIn("Git blob", str(error))

    def test_invalid_base64_policy_is_denied(self) -> None:
        """Invalid transport bytes cannot fall back to any candidate-local file."""
        # Arrange
        entry = policy_file()
        entry["content"] = "!not-base64!"
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.side_effect = [
            {"name": "main", "protected": True, "commit": {"sha": "a" * 40}}, entry
        ]

        # Act
        error = failure(lambda: publication.fetch_current_authority(lines.REPOSITORY, client))

        # Assert
        self.assertIn("cannot be verified", str(error))

    def test_requested_repository_must_match_current_policy(self) -> None:
        """Do not load this repository's policy as authority for another owner."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.side_effect = [
            {"name": "main", "protected": True, "commit": {"sha": "a" * 40}}, policy_file()
        ]

        # Act
        error = failure(lambda: publication.fetch_current_authority("another/repository", client))

        # Assert
        self.assertIn("another repository", str(error))

    def test_current_authority_api_failures_are_not_optional(self) -> None:
        """Network/auth errors at either current read remain publication failures."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.side_effect = publication.PublicationAdmissionError("GitHub unavailable")

        # Act
        error = failure(lambda: publication.fetch_current_authority(lines.REPOSITORY, client))

        # Assert
        self.assertEqual("GitHub unavailable", str(error))
        self.assertEqual(1, client.request.call_count)

    def test_current_policy_api_failure_does_not_use_history_or_checkout(self) -> None:
        """A valid branch response does not make an absent policy recoverable."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.side_effect = [
            {"name": "main", "protected": True, "commit": {"sha": "a" * 40}},
            publication.PublicationAdmissionError("Policy unavailable"),
        ]

        # Act
        error = failure(lambda: publication.fetch_current_authority(lines.REPOSITORY, client))

        # Assert
        self.assertEqual("Policy unavailable", str(error))
        self.assertEqual(2, client.request.call_count)


class PublicationAdmissionTests(unittest.TestCase):
    """Keep active publication and revoked identities distinct."""

    def test_active_line_admits_exact_bytes(self) -> None:
        """Create a stable digest over the complete original attempt identity."""
        # Arrange
        candidate = identity()
        observed = authority()

        # Act
        result = publication.create_admission(candidate, observed)

        # Assert
        self.assertEqual(candidate, result["identity"])
        self.assertEqual(publication.canonical_digest(candidate), result["admissionId"])

    def test_active_admission_can_resume_after_an_authority_revision(self) -> None:
        """Current active rights may resume the same immutable admitted candidate."""
        # Arrange
        original = admission()
        policy = initial_policy()
        policy["revision"] = "2026-10-22.2"
        observed = authority(policy)

        # Act
        result = validate_bound(original, observed)

        # Assert
        self.assertIsNone(result)

    def test_prepared_rejects_publication(self) -> None:
        """Preparation is qualification permission, never a publisher grant."""
        # Arrange
        policy = initial_policy()
        policy["lines"][0]["state"] = "prepared"

        # Act
        error = failure(lambda: publication.create_admission(identity(), authority(policy)))

        # Assert
        self.assertIn("not eligible for publication", str(error))

    def test_inactive_rejects_publication(self) -> None:
        """An inactive line cannot use an old candidate snapshot as authority."""
        # Arrange
        policy = initial_policy()
        policy["lines"][0]["state"] = "inactive"

        # Act
        error = failure(lambda: publication.create_admission(identity(), authority(policy)))

        # Assert
        self.assertIn("not eligible for publication", str(error))

    def test_inactive_line_rejects_original_admission(self) -> None:
        """Deactivation revokes even an otherwise valid previously admitted run."""
        # Arrange
        policy = initial_policy()
        policy["lines"][0]["state"] = "inactive"
        original = admission()

        # Act
        error = failure(lambda: validate_bound(original, authority(policy)))

        # Assert
        self.assertIn("not eligible for publication", str(error))

    def test_identity_mutation_invalidates_admission_digest(self) -> None:
        """Keep source, tag, attempt chain, and sealed bytes in one identity."""
        # Arrange
        original = admission()
        original["identity"]["manifestSha256"] = "f" * 64

        # Act
        error = failure(lambda: publication.validate_admission(original))

        # Assert
        self.assertIn("digest", str(error))

    def test_boundary_rejects_other_candidate_receipt_bytes(self) -> None:
        """A valid admission cannot authorize replacement candidate metadata."""
        # Arrange
        original = admission()
        # Act
        error = failure(lambda: validate_bound(original, authority(), candidate_receipt_sha256="f" * 64))

        # Assert
        self.assertIn("immutable admission", str(error))

    def test_same_run_retry_context_is_valid(self) -> None:
        """A later attempt may resume the original run without a new identity."""
        # Arrange
        original = identity()
        hosted = environment()

        # Act
        result = publication.require_hosted_identity(original, hosted)

        # Assert
        self.assertIsNone(result)

    def test_other_run_cannot_borrow_an_admission(self) -> None:
        """Do not share an immutable admission across workflow dispatches."""
        # Arrange
        hosted = environment()
        hosted["GITHUB_RUN_ID"] = "124"

        # Act
        error = failure(lambda: publication.require_hosted_identity(identity(), hosted))

        # Assert
        self.assertIn("same-run", str(error))

    def test_missing_shared_lock_rejects_publication(self) -> None:
        """Per-version concurrency is not the repository publication exclusion."""
        # Arrange
        hosted = environment()
        hosted["DOKA_PUBLICATION_LOCK_GROUP"] = "release-v10.6.2"

        # Act
        error = failure(lambda: publication.require_hosted_identity(identity(), hosted))

        # Assert
        self.assertIn("shared", str(error))

    def test_authority_digest_tampering_fails(self) -> None:
        """Do not trust a candidate-local policy relabeled as a current readback."""
        # Arrange
        observed = authority()
        observed["policy"]["revision"] = "2026-10-22.2"

        # Act
        error = failure(lambda: publication.validate_authority(observed))

        # Assert
        self.assertIn("identity", str(error))

    def test_bool_schema_is_not_an_integer_schema(self) -> None:
        """Reject Python's otherwise equal True/1 schema alias."""
        # Arrange
        observed = authority()
        observed["schemaVersion"] = True

        # Act
        error = failure(lambda: publication.validate_authority(observed))

        # Assert
        self.assertIn("identity", str(error))

    def test_nonobject_authority_has_typed_failure(self) -> None:
        """Malformed external evidence must not escape as AttributeError."""
        # Arrange
        observed = []

        # Act
        error = failure(lambda: publication.validate_authority(observed))

        # Assert
        self.assertIn("must be an object", str(error))

    def test_prepared_snapshot_cannot_be_relabelled_active(self) -> None:
        """Freeze qualification state from the independently fetched policy."""
        # Arrange
        policy = initial_policy()
        policy["lines"][0]["state"] = "prepared"
        snapshot = lines.freeze_line(policy, "refs/heads/main", "10.6.2")
        snapshot["stateAtQualification"] = "active"

        # Act
        error = failure(lambda: publication.validate_qualification_authority(authority(policy), snapshot))

        # Assert
        self.assertIn("Qualification state", str(error))

    def test_matching_independent_qualification_authority_passes(self) -> None:
        """Qualification accepts the exact frozen observation, not current rights."""
        # Arrange
        observed = authority()
        snapshot = lines.freeze_line(observed["policy"], "refs/heads/main", "10.6.2")

        # Act
        result = publication.validate_qualification_authority(observed, snapshot, version="10.6.2")

        # Assert
        self.assertIsNone(result)

    def test_environment_requires_existing_reviewers(self) -> None:
        """Avoid scheduling a job that would auto-create an unprotected environment."""
        # Arrange
        policy = initial_policy()
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = {
            "name": "nuget", "protection_rules": [],
            "deployment_branch_policy": {"custom_branch_policies": True, "protected_branches": False},
        }

        # Act
        error = failure(lambda: publication.validate_environment(
            policy["repository"], policy["lines"][0], policy, client
        ))

        # Assert
        self.assertIn("already exist", str(error))

    def test_environment_rejects_wildcard_branch_grants(self) -> None:
        """Only exact policy refs may receive publisher credentials."""
        # Arrange
        policy = initial_policy()
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = {
            "name": "nuget",
            "protection_rules": [{"type": "required_reviewers", "reviewers": [{"id": 1}]}],
            "deployment_branch_policy": {"custom_branch_policies": True, "protected_branches": False},
        }

        client.pages.return_value = [{"name": "*", "type": "branch"}]

        # Act
        error = failure(lambda: publication.validate_environment(
            policy["repository"], policy["lines"][0], policy, client
        ))

        # Assert
        self.assertIn("wildcard", str(error))

    def test_single_maintainer_environment_accepts_admin_bypass_true_false_or_absent(self) -> None:
        """Every release line keeps existing admin bypass and self-review settings."""
        for line_index in (0, 1, 2):
            for bypass in (True, False, None):
                with self.subTest(line_index=line_index, bypass=bypass):
                    # Arrange
                    policy = shared_policy()
                    if line_index == 1:
                        policy["lines"][0]["packageMajor"] = 11

                    selected = policy["lines"][line_index]
                    selected["state"] = "active"
                    branch = selected["sourceRef"].removeprefix("refs/heads/")
                    client, responses, _ = publisher_environment_client(policy)
                    hosted = responses[f"/repos/{lines.REPOSITORY}/environments/nuget"]
                    if bypass is None:
                        hosted.pop("can_admins_bypass")
                    else:
                        hosted["can_admins_bypass"] = bypass

                    original = copy.deepcopy(hosted)

                    # Act
                    result = publication.validate_environment(policy["repository"], selected, policy, client)

                    # Assert
                    self.assertIsNone(result)
                    self.assertEqual(original, hosted)
                    self.assertFalse(hosted["protection_rules"][0]["prevent_self_review"])
                    self.assertEqual(1, len(hosted["protection_rules"][0]["reviewers"]))
                    self.assertEqual([
                        mock.call(f"/repos/{lines.REPOSITORY}/environments/nuget"),
                        mock.call(f"/repos/{lines.REPOSITORY}/branches/{quote(branch, safe='')}"),
                    ], client.request.call_args_list)
                    client.pages.assert_called_once_with(
                        f"/repos/{lines.REPOSITORY}/environments/nuget/deployment-branch-policies", "branch_policies"
                    )

    def test_existing_environment_rejects_missing_or_malformed_protection(self) -> None:
        """Keeping bypass must not remove the ordinary reviewer and branch-policy checks."""
        changes = (
            {"protection_rules": []},
            {"protection_rules": {}},
            {"protection_rules": [{"type": "required_reviewers", "reviewers": []}]},
            {"protection_rules": [{"type": "required_reviewers", "reviewers": "unknown"}]},
            {"deployment_branch_policy": None},
            {"deployment_branch_policy": {"custom_branch_policies": False, "protected_branches": False}},
            {"deployment_branch_policy": {"custom_branch_policies": True, "protected_branches": True}},
        )

        for change in changes:
            with self.subTest(change=change):
                # Arrange
                policy = shared_policy()
                client, responses, _ = publisher_environment_client()
                responses[f"/repos/{lines.REPOSITORY}/environments/nuget"].update(copy.deepcopy(change))

                # Act
                error = failure(lambda: publication.validate_environment(
                    policy["repository"], policy["lines"][0], policy, client
                ))

                # Assert
                self.assertIn("already exist", str(error))

    def test_existing_environment_rejects_a_tag_grant(self) -> None:
        """An approved tag cannot substitute for an exact authorized source branch."""
        # Arrange
        policy = shared_policy()
        client, _, grants = publisher_environment_client()
        grants[f"/repos/{lines.REPOSITORY}/environments/nuget/deployment-branch-policies"] = [
            {"name": "v10.6.2", "type": "tag"}
        ]

        # Act
        error = failure(lambda: publication.validate_environment(
            policy["repository"], policy["lines"][0], policy, client
        ))

        # Assert
        self.assertIn("non-branch grant", str(error))

    def test_existing_environment_rejects_an_additional_unregistered_branch(self) -> None:
        """Shared credentials do not authorize arbitrary feature branches."""
        # Arrange
        policy = shared_policy()
        client, _, grants = publisher_environment_client()
        grants[f"/repos/{lines.REPOSITORY}/environments/nuget/deployment-branch-policies"].append(
            {"name": "feature/unregistered", "type": "branch"}
        )

        # Act
        error = failure(lambda: publication.validate_environment(
            policy["repository"], policy["lines"][0], policy, client
        ))

        # Assert
        self.assertIn("disagree", str(error))

    def test_existing_environment_readback_failure_is_not_optional(self) -> None:
        """An unavailable real publisher environment never becomes an implicit grant."""
        # Arrange
        policy = shared_policy()
        client, _, _ = publisher_environment_client()
        client.request.side_effect = publication.PublicationAdmissionError("Environment unavailable")

        # Act
        error = failure(lambda: publication.validate_environment(
            policy["repository"], policy["lines"][0], policy, client
        ))

        # Assert
        self.assertEqual("Environment unavailable", str(error))

    def test_existing_environment_rejects_unprotected_source_branch(self) -> None:
        """A protected environment cannot make an unprotected source eligible."""
        # Arrange
        policy = shared_policy()
        client, responses, _ = publisher_environment_client()
        responses[f"/repos/{lines.REPOSITORY}/branches/main"] = {"protected": False}

        # Act
        error = failure(lambda: publication.validate_environment(
            policy["repository"], policy["lines"][0], policy, client
        ))

        # Assert
        self.assertIn("source branch is not protected", str(error))

    def archive(self, original: dict) -> tuple[dict, bytes]:
        """Build a byte-verified two-document admission artifact fixture."""
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("publication-admission.json", json.dumps(original))
            archive.writestr("authorization-observation.json", json.dumps(authority()))

        raw = buffer.getvalue()
        metadata = {
            "id": 12, "name": "publication-admission-attempt-2", "expired": False,
            "digest": f"sha256:{hashlib.sha256(raw).hexdigest()}",
            "workflow_run": {"id": 123, "head_sha": "b" * 40},
        }

        return metadata, raw

    def admit_arguments(self, root: Path):
        """Build a real admission command whose outputs remain isolated."""
        return publication.build_parser().parse_args([
            "admit", "--repository", lines.REPOSITORY,
            "--candidate-root", str(root / "candidate"),
            "--candidate-receipt", str(root / "candidate-receipt.json"),
            "--output", str(root / "publication-admission.json"),
            "--authority-output", str(root / "authorization-observation.json"),
        ])

    def archived_admission_client(self, original: dict) -> mock.Mock:
        """Supply a same-run archive plus the unchanged publisher environment gate."""
        metadata, raw = self.archive(original)
        client = mock.Mock(spec=publication.GitHubClient)
        client.download.return_value = raw
        client.pages.side_effect = [[metadata], [{"name": "main", "type": "branch"}]]
        client.request.side_effect = [hosted_environment("nuget"), {"protected": True}]

        return client

    def test_admit_resumes_exact_archive_under_current_active_policy(self) -> None:
        """Actual retry admission preserves original proof while rechecking current rights."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = self.admit_arguments(root)
            original = admission()
            client = self.archived_admission_client(original)
            candidate = dict(original["identity"], originalPublicationAttempt="3")
            policy = initial_policy()
            policy["revision"] = "2026-10-23.2"
            current = authority(policy)

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "candidate_identity", return_value=candidate
            ), mock.patch.object(publication, "fetch_current_authority", return_value=current), mock.patch.dict(
                "os.environ", environment(), clear=True
            ):
                publication.admit(args)

            # Assert
            self.assertEqual(json.dumps(original).encode(), args.output.read_bytes())
            self.assertEqual(json.dumps(authority()).encode(), args.observation_output.read_bytes())
            self.assertNotEqual(current["authorizationDigest"], publication.read_json(
                args.observation_output
            )["authorizationDigest"])
            self.assertEqual(original["admissionId"], publication.read_json(args.output)["admissionId"])
            client.download.assert_called_once()
            self.assertEqual(2, client.request.call_count)
            self.assertEqual(2, client.pages.call_count)

    def test_admit_rejects_changed_candidate_receipt_on_same_run_retry(self) -> None:
        """Real command orchestration cannot replace admitted receipt bytes during recovery."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = self.admit_arguments(root)
            original = admission()
            client = self.archived_admission_client(original)
            candidate = dict(original["identity"], originalPublicationAttempt="3", candidateReceiptSha256="f" * 64)

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "candidate_identity", return_value=candidate
            ), mock.patch.object(publication, "fetch_current_authority", return_value=authority()), mock.patch.dict(
                "os.environ", environment(), clear=True
            ):
                error = failure(lambda: publication.admit(args))

            # Assert
            self.assertIn("originally admitted candidate bytes", str(error))
            self.assertFalse(args.output.exists())
            client.request.assert_not_called()

    def test_admit_rejects_archived_retry_after_line_deactivation(self) -> None:
        """Restoring exact historic proof does not override present deactivation."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = self.admit_arguments(root)
            original = admission()
            client = self.archived_admission_client(original)
            candidate = dict(original["identity"], originalPublicationAttempt="3")
            policy = initial_policy()
            policy["revision"] = "2026-10-23.2"
            policy["lines"][0]["state"] = "inactive"
            current = authority(policy)

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "candidate_identity", return_value=candidate
            ), mock.patch.object(publication, "fetch_current_authority", return_value=current), mock.patch.dict(
                "os.environ", environment(), clear=True
            ):
                error = failure(lambda: publication.admit(args))

            # Assert
            self.assertIn("not eligible for publication", str(error))
            self.assertEqual(original, publication.read_json(args.output))
            client.request.assert_not_called()

    def test_admit_rejects_archived_retry_when_line_is_prepared_only(self) -> None:
        """A qualification-only line cannot resume writes using an earlier admission."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = self.admit_arguments(root)
            original = admission()
            client = self.archived_admission_client(original)
            candidate = dict(original["identity"], originalPublicationAttempt="3")
            policy = initial_policy()
            policy["revision"] = "2026-10-23.2"
            policy["lines"][0]["state"] = "prepared"
            current = authority(policy)

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "candidate_identity", return_value=candidate
            ), mock.patch.object(publication, "fetch_current_authority", return_value=current), mock.patch.dict(
                "os.environ", environment(), clear=True
            ):
                error = failure(lambda: publication.admit(args))

            # Assert
            self.assertIn("not eligible for publication", str(error))
            self.assertEqual(original, publication.read_json(args.output))
            client.request.assert_not_called()

    def test_recheck_rejects_an_unregistered_grant_before_boundary(self) -> None:
        """Actual recheck reads current hosted restrictions after immutable admission validation."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            policy = shared_policy()
            current = authority(policy)
            candidate = identity()
            candidate["workflowPath"] = lines.LINE_WORKFLOW_PATH
            candidate["releaseLineSha256"] = publication.canonical_digest(
                lines.freeze_line(policy, "refs/heads/main", candidate["releaseVersion"])
            )
            original = publication.create_admission(candidate, current)
            publication.write_json(root / "publication-admission.json", original)
            args = publication.build_parser().parse_args([
                "recheck", "--repository", lines.REPOSITORY,
                "--admission", str(root / "publication-admission.json"),
                "--candidate-root", str(root / "candidate"),
                "--candidate-receipt", str(root / "candidate-receipt.json"),
                "--observation-output", str(root / "current-observation.json"),
            ])
            hosted = environment()
            hosted["GITHUB_WORKFLOW_REF"] = f"{lines.REPOSITORY}/{lines.LINE_WORKFLOW_PATH}@refs/heads/main"
            client, _, grants = publisher_environment_client()
            grants[f"/repos/{lines.REPOSITORY}/environments/nuget/deployment-branch-policies"] = [
                {"name": "unregistered", "type": "branch"}
            ]

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "candidate_identity", return_value=candidate
            ), mock.patch.object(publication, "fetch_current_authority", return_value=current), mock.patch.dict(
                "os.environ", hosted, clear=True
            ):
                error = failure(lambda: publication.recheck(args))

            # Assert
            self.assertIn("disagree", str(error))
            self.assertFalse(args.observation_output.exists())
            self.assertEqual(original, publication.read_json(args.admission))
            self.assertIn(mock.call(
                f"/repos/{lines.REPOSITORY}/environments/nuget/deployment-branch-policies", "branch_policies"
            ), client.pages.call_args_list)

    def test_restore_preserves_original_admission_and_authority_bytes(self) -> None:
        """A retry restores initial observation, never freezes today's policy."""
        # Arrange
        original = admission()
        metadata, raw = self.archive(original)
        client = mock.Mock(spec=publication.GitHubClient)
        client.pages.return_value = [metadata]
        client.download.return_value = raw

        # Act
        restored = publication.restore_admission(lines.REPOSITORY, "123", "b" * 40, 3, client)

        # Assert
        self.assertEqual(original, restored[0])
        self.assertEqual(json.dumps(original).encode(), restored[1]["publication-admission.json"])
        self.assertEqual(authority(), json.loads(restored[1]["authorization-observation.json"]))

    def test_expired_admission_cannot_be_replaced(self) -> None:
        """An expired artifact is an explicit evidence failure, not new admission."""
        # Arrange
        metadata, _ = self.archive(admission())
        metadata["expired"] = True
        client = mock.Mock(spec=publication.GitHubClient)
        client.pages.return_value = [metadata]

        # Act
        error = failure(lambda: publication.restore_admission(lines.REPOSITORY, "123", "b" * 40, 3, client))

        # Assert
        self.assertIn("expired", str(error))

    def test_admission_artifact_wrong_run_is_rejected(self) -> None:
        """Matching names cannot override GitHub artifact run metadata."""
        # Arrange
        metadata, _ = self.archive(admission())
        metadata["workflow_run"]["id"] = 999
        client = mock.Mock(spec=publication.GitHubClient)
        client.pages.return_value = [metadata]

        # Act
        error = failure(lambda: publication.restore_admission(lines.REPOSITORY, "123", "b" * 40, 3, client))

        # Assert
        self.assertIn("another run", str(error))

    def test_archive_digest_mismatch_fails_before_receipt_read(self) -> None:
        """Do not parse a substituted admission archive as trusted evidence."""
        # Arrange
        metadata, raw = self.archive(admission())
        metadata["digest"] = "sha256:" + "f" * 64
        client = mock.Mock(spec=publication.GitHubClient)
        client.pages.return_value = [metadata]
        client.download.return_value = raw

        # Act
        error = failure(lambda: publication.restore_admission(lines.REPOSITORY, "123", "b" * 40, 3, client))

        # Assert
        self.assertIn("archive digest", str(error))

    def test_paginated_inventory_reads_beyond_first_hundred(self) -> None:
        """A growing matrix must not hide the immutable admission after page one."""
        # Arrange
        client = publication.GitHubClient()
        pages = [
            {"total_count": 101, "artifacts": [{"id": number} for number in range(100)]},
            {"total_count": 101, "artifacts": [{"id": 100}]},
        ]

        # Act
        with mock.patch.object(client, "request", side_effect=pages) as request:
            result = client.pages("/artifacts", "artifacts")

        # Assert
        self.assertEqual(101, len(result))
        self.assertIn("page=2", request.call_args_list[1].args[0])

    def test_shared_pagination_rejects_duplicate_or_incomplete_identity_sets(self) -> None:
        """Malformed pages must not hide an immutable admission artifact."""
        for response, reason in (
            ({"total_count": 2, "artifacts": [{"id": 1}, {"id": 1}]}, "duplicate"),
            ({"total_count": 2, "artifacts": [{"id": 1}]}, "incomplete"),
            ({"total_count": True, "artifacts": []}, "count"),
        ):
            with self.subTest(reason=reason):
                # Arrange
                client = publication.GitHubClient()

                # Act
                with mock.patch.object(client, "request", return_value=response):
                    error = failure(lambda: client.pages("/artifacts", "artifacts"))

                # Assert
                self.assertIn(reason, str(error))

    def test_shared_pagination_rejects_a_changed_total_between_pages(self) -> None:
        """A concurrent inventory change cannot be accepted as a complete snapshot."""
        # Arrange
        client = publication.GitHubClient()
        pages = [
            {"total_count": 101, "artifacts": [{"id": number} for number in range(100)]},
            {"total_count": 102, "artifacts": [{"id": 100}, {"id": 101}]},
        ]

        # Act
        with mock.patch.object(client, "request", side_effect=pages):
            error = failure(lambda: client.pages("/artifacts", "artifacts"))

        # Assert
        self.assertIn("count changed", str(error))

    def test_shared_pagination_does_not_trust_an_exact_full_page_total(self) -> None:
        """A stale count must not hide another publisher after a full first page."""
        # Arrange
        client = publication.GitHubClient()
        pages = [
            {"total_count": 100, "artifacts": [{"id": number} for number in range(100)]},
            {"total_count": 100, "artifacts": [{"id": 100}]},
        ]

        # Act
        with mock.patch.object(client, "request", side_effect=pages) as request:
            error = failure(lambda: client.pages("/artifacts", "artifacts"))

        # Assert
        self.assertIn("inconsistent", str(error))
        self.assertEqual(2, request.call_count)

    def test_first_admission_rejects_a_prior_publisher_without_a_skipped_boundary(self) -> None:
        """A waiting, legacy or completed publisher cannot receive replacement admission."""
        for job in (
            {"name": "publish", "status": "waiting", "steps": []},
            {"name": "publish", "status": "completed", "conclusion": "failure", "steps": []},
            {"name": "publish", "status": "completed", "conclusion": "success", "steps": [{
                "name": publication.PUBLISHER_STEP_NAME, "status": "completed", "conclusion": "success",
            }]},
        ):
            with self.subTest(job=job):
                # Arrange
                client = mock.Mock(spec=publication.GitHubClient)
                client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
                client.pages.return_value = [dict(job, run_id=123)]

                # Act
                error = failure(lambda: publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client))

                # Assert
                self.assertIsInstance(error, publication.PublicationAdmissionError)

    def test_first_admission_rejects_ambiguous_or_missing_boundary_history(self) -> None:
        """Duplicate boundaries or missing step evidence must not imply absence."""
        boundary = {"name": publication.PUBLISHER_STEP_NAME, "conclusion": "success"}
        for steps in (None, [None], [boundary, boundary]):
            with self.subTest(steps=steps):
                # Arrange
                client = mock.Mock(spec=publication.GitHubClient)
                client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
                client.pages.return_value = [{"run_id": 123, "name": "publish", "steps": steps}]

                # Act
                error = failure(lambda: publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client))

                # Assert
                self.assertIsInstance(error, publication.PublicationAdmissionError)

    def test_first_admission_rejects_foreign_job_identity(self) -> None:
        """A job from another run cannot prove this run never reached publication."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
        client.pages.return_value = [{"run_id": 456, "name": "qualify", "steps": []}]

        # Act
        error = failure(lambda: publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client))

        # Assert
        self.assertIn("job identity", str(error))

    def test_first_admission_accepts_an_ended_job_with_an_unstarted_boundary(self) -> None:
        """An explicit never-started step can reach its first admission on retry."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
        client.pages.return_value = [{
            "run_id": 123, "name": "publish", "status": "completed", "conclusion": "cancelled",
            "steps": [{"name": name, "status": "pending", "started_at": None} for name in (
                publication.ADMISSION_STEP_NAME, publication.PUBLISHER_STEP_NAME,
            )],
        }]

        # Act
        result = publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client)

        # Assert
        self.assertIsNone(result)

    def test_first_admission_rejects_unfinished_publisher_with_an_unstarted_boundary(self) -> None:
        """An attempt summary cannot override a still-active publisher job."""
        for status in ("waiting", "queued", "in_progress", None):
            with self.subTest(status=status):
                # Arrange
                client = mock.Mock(spec=publication.GitHubClient)
                client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
                client.pages.return_value = [{
                    "run_id": 123, "name": "publish", "status": status,
                    "steps": [{"name": name, "status": "pending", "started_at": None} for name in (
                        publication.ADMISSION_STEP_NAME, publication.PUBLISHER_STEP_NAME,
                    )],
                }]

                # Act
                error = failure(lambda: publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client))

                # Assert
                self.assertIn("refusing replacement identity", str(error))

    def test_disappeared_admission_after_publisher_started_cannot_be_reminted(self) -> None:
        """Deleted and API-hidden expired artifacts are not proof of non-admission."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
        client.pages.return_value = [{
            "run_id": 123, "name": "publish", "status": "completed", "conclusion": "failure",
            "steps": [
                {"name": publication.ADMISSION_STEP_NAME, "status": "completed",
                 "conclusion": "success", "started_at": "2026-10-22T10:00:00Z"},
                {"name": publication.PUBLISHER_STEP_NAME, "status": "completed",
                 "conclusion": "failure", "started_at": "2026-10-22T10:00:01Z"},
            ],
        }]

        # Act
        error = failure(lambda: publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client))

        # Assert
        self.assertIn("refusing replacement identity", str(error))

    def test_retry_after_failed_qualification_can_reach_first_admission(self) -> None:
        """An earlier attempt that provably skipped publication needs no admission archive."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
        client.pages.return_value = [{
            "run_id": 123, "name": "publish", "conclusion": "skipped",
            "steps": [{"name": name, "status": "completed", "conclusion": "skipped"} for name in (
                publication.ADMISSION_STEP_NAME, publication.PUBLISHER_STEP_NAME,
            )],
        }]

        # Act
        result = publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client)

        # Assert
        self.assertIsNone(result)

    def test_absent_prior_job_history_rejects_a_replacement_admission(self) -> None:
        """Unavailable history must not silently become a never-started assertion."""
        # Arrange
        client = mock.Mock(spec=publication.GitHubClient)
        client.request.return_value = {"id": 123, "run_attempt": 1, "status": "completed"}
        client.pages.return_value = []

        # Act
        error = failure(lambda: publication.prove_no_prior_admission(lines.REPOSITORY, "123", 2, client))

        # Assert
        self.assertIn("no retained job history", str(error))

    def preflight_arguments(self, root: Path):
        """Build the exact workflow preflight invocation without hosted writes."""
        return publication.build_parser().parse_args([
            "preflight", "--repository", lines.REPOSITORY,
            "--source-ref", "refs/heads/main", "--version", "10.6.2",
            "--workflow-path", lines.LEGACY_WORKFLOW_PATH,
            "--output", str(root / "line.json"),
            "--policy-output", str(root / "policy.json"),
            "--authority-output", str(root / "authority.json"),
            "--github-output", str(root / "outputs"),
        ])

    def test_preflight_qualification_does_not_schedule_a_publisher(self) -> None:
        """An active line's read-only preflight remains explicitly qualification-only."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = self.preflight_arguments(root)
            hosted = environment()
            hosted["GITHUB_SHA"] = "a" * 40
            client = mock.Mock(spec=publication.GitHubClient)
            client.request.return_value = {"protected": True, "commit": {"sha": "a" * 40}}

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "fetch_current_authority", return_value=authority()
            ), mock.patch.dict("os.environ", hosted, clear=True):
                publication.preflight(args)

            # Assert
            self.assertIn("publish_allowed=false", (root / "outputs").read_text())
            self.assertEqual(authority(), publication.read_json(root / "authority.json"))
            client.pages.assert_not_called()

    def test_preflight_rejects_stale_hosted_source_commit(self) -> None:
        """An old workflow checkout cannot borrow the new protected-main grant."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            args = self.preflight_arguments(Path(directory))
            client = mock.Mock(spec=publication.GitHubClient)
            client.request.return_value = {"protected": True, "commit": {"sha": "a" * 40}}

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "fetch_current_authority", return_value=authority()
            ), mock.patch.dict("os.environ", environment(), clear=True):
                error = failure(lambda: publication.preflight(args))

            # Assert
            self.assertIn("exact current authorized source head", str(error))

    def test_preflight_rejects_wrong_hosted_caller_workflow(self) -> None:
        """The qualifier's reusable signer must not replace the actual caller binding."""
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            args = self.preflight_arguments(Path(directory))
            hosted = environment()
            hosted["GITHUB_SHA"] = "a" * 40
            hosted["GITHUB_WORKFLOW_REF"] = f"{lines.REPOSITORY}/.github/workflows/other.yml@refs/heads/main"
            client = mock.Mock(spec=publication.GitHubClient)
            client.request.return_value = {"protected": True, "commit": {"sha": "a" * 40}}

            # Act
            with mock.patch.object(publication, "GitHubClient", return_value=client), mock.patch.object(
                publication, "fetch_current_authority", return_value=authority()
            ), mock.patch.dict("os.environ", hosted, clear=True):
                error = failure(lambda: publication.preflight(args))

            # Assert
            self.assertIn("caller workflow", str(error))


if __name__ == "__main__":
    unittest.main()
